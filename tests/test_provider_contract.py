import copy
import json
import pathlib
import tempfile
import threading
import time
import unittest
import urllib.error
from unittest.mock import patch

import auto_sources
import claude_cli
import providers
from provider_contract import normalize
from provider_registry import adapters, manifest
from quota_cache import valid_cache

ROOT = pathlib.Path(__file__).resolve().parents[1]
FIXTURES = json.loads((ROOT/'tests/fixtures/provider-responses.json').read_text())

class ContractTests(unittest.TestCase):
    def test_every_adapter_normal_fixture(self):
        parsers={'codex':providers.normalize_codex,'cursor':auto_sources.parse_cursor,'opencode':auto_sources.parse_go,'claude':claude_cli.parse_screen,'openrouter':auto_sources.parse_openrouter}
        for spec in manifest():
            with self.subTest(provider=spec['id']):
                value=normalize(spec,{'status':'Live','at':1000,'windows':parsers[spec['id']](FIXTURES[spec['id']])},1000)
                self.assertEqual(value['schema_version'],1);self.assertEqual(value['windows'][0]['remaining'],75)
                self.assertNotIn('DO NOT FORWARD',json.dumps(value));self.assertEqual(value['source'],spec['source'])

    def test_every_adapter_contract_edge_cases(self):
        for spec in manifest():
            good={'status':'Live','at':1000,'windows':[{'id':'session','label':'Session','remaining':42,'reset':2000}]}
            with self.subTest(provider=spec['id']):
                for remaining in (0,100):
                    raw=copy.deepcopy(good);raw['windows'][0]['remaining']=remaining
                    self.assertEqual(normalize(spec,raw,1000)['windows'][0]['remaining'],remaining)
                for raw in (None,[],{}, {'status':'Live'}, {**good,'at':1001}, {**good,'at':True}, {**good,'windows':'changed format'}, {**good,'windows':[{}]}):
                    self.assertEqual(normalize(spec,raw,1000)['windows'],[])
                for status in ('Connect','Unavailable','Waiting','Disabled'):
                    self.assertEqual(normalize(spec,{**good,'status':status},1000)['windows'],[])
                self.assertEqual(normalize(spec,good,1700)['status'],'Stale')
                self.assertEqual(normalize(spec,good,2100)['windows'],[])
                raw=copy.deepcopy(good);raw['windows'][0]['reset']=None
                self.assertIsNone(normalize(spec,raw,1000)['windows'][0]['reset'])
                second={**good['windows'][0],'id':'weekly','label':'Weekly'}
                self.assertEqual(len(normalize(spec,{**good,'windows':good['windows']+[second]},1000)['windows']),2)
                self.assertEqual(normalize(spec,{**good,'windows':good['windows']*2},1000)['windows'],[])
                for bad in (float('nan'),float('inf'),True,-1,101,'50'):
                    raw=copy.deepcopy(good);raw['windows'][0]['remaining']=bad
                    self.assertEqual(normalize(spec,raw,1000)['windows'],[])
                raw={**good,'authorization':'PRIVATE','account_label':'person@example.test','detail':'person@example.test','scope':'invalid','metadata':{'secret':'PRIVATE'}}
                value=normalize(spec,raw,1000)
                self.assertNotIn('PRIVATE',json.dumps(value));self.assertNotIn('person@',json.dumps(value));self.assertNotIn('scope',value)
                self.assertEqual(normalize(spec,{**good,'scope':'a'*64},1000)['account_id'],'a'*64)

    def test_every_adapter_exceptions_are_isolated_and_sanitized(self):
        registry=adapters()
        for failed in registry:
            for error in (PermissionError('private account'),TimeoutError('private account'),ConnectionError('private account'),ValueError('format change')):
                def collect(adapter):
                    if adapter.metadata['id']==failed: raise error
                    return {'status':'Live','at':time.time(),'windows':[{'label':'Session','remaining':50}]}
                with self.subTest(provider=failed,error=type(error).__name__),patch('provider_registry.ProviderAdapter.collect',collect):
                    result=providers.collect(list(registry));self.assertEqual(result[failed]['status'],'Unavailable')
                    self.assertNotIn('private account',json.dumps(result))
                    self.assertTrue(all(value['windows'] for key,value in result.items() if key!=failed))

    def test_single_provider_worker_keeps_cleanup_on_the_owning_thread(self):
        owner=threading.get_ident();cleaned=[]
        def collect():
            try:
                self.assertEqual(threading.get_ident(),owner)
                raise TimeoutError('fixture timeout')
            finally:cleaned.append(True)
        with patch.object(providers,'codex',side_effect=collect):
            self.assertEqual(providers.collect(['codex'])['codex']['status'],'Unavailable')
        self.assertEqual(cleaned,[True])

    def test_cache_rejects_duplicate_private_and_invalid_windows(self):
        reading={'status':'Live','at':time.time(),'windows':[{'label':'Session','remaining':42}]}
        base={'scope':'fixture','retryAt':time.time()+300,'reading':reading}
        self.assertTrue(valid_cache(base,'scope','retryAt'))
        for windows in ([{'label':'person@example.test','remaining':42}], reading['windows']*2,[{'label':'Session','remaining':42,'reset':True}],[{'label':'Session','remaining':42,'rolling':'true'}]):
            self.assertFalse(valid_cache({**base,'reading':{**reading,'windows':windows}},'scope','retryAt'))

class OpenRouterTests(unittest.TestCase):
    def test_no_consent_never_reads_environment_or_network(self):
        with patch.object(auto_sources,'openrouter_enabled',return_value=False),patch.object(auto_sources,'openrouter_token') as token,patch.object(auto_sources,'get_json') as network:
            self.assertEqual(auto_sources.collect_account('openrouter')['status'],'Connect');token.assert_not_called();network.assert_not_called()

    def test_credits_unknown_reset_and_format_change(self):
        for remaining in (0,100):self.assertEqual(auto_sources.parse_openrouter({'data':{'limit':100,'limit_remaining':remaining}})[0]['remaining'],remaining)
        self.assertIsNone(auto_sources.parse_openrouter(FIXTURES['openrouter'])[0]['reset'])
        for value in ({},{'data':None},{'data':{'limit':None}},{'data':{'limit':0,'limit_remaining':0}},{'data':{'limit':10,'limit_remaining':11}},{'data':{'limit':True,'limit_remaining':1}}):
            with self.assertRaises(auto_sources.UsageError):auto_sources.parse_openrouter(value)

    def test_cache_scope_and_unauthorized_invalidation(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'build/test-tmp') as folder,patch.object(auto_sources,'DATA',pathlib.Path(folder)),patch.object(auto_sources,'openrouter_enabled',return_value=True),patch.object(auto_sources,'openrouter_token',return_value='fixture-not-a-real-credential'),patch.object(auto_sources,'get_json',return_value=FIXTURES['openrouter']) as network:
            result=auto_sources.collect_account('openrouter');self.assertEqual(result['windows'][0]['remaining'],75);first=result['at'];self.assertEqual(auto_sources.collect_account('openrouter')['at'],first);self.assertEqual(network.call_count,1)
            cache=pathlib.Path(folder)/'openrouter-automatic.json';self.assertNotIn('fixture-not-a-real-credential',cache.read_text());data=json.loads(cache.read_text());data['retryAt']=0;cache.write_text(json.dumps(data));network.side_effect=auto_sources.UsageError('Sign-in needs attention.')
            self.assertNotIn('windows',auto_sources.collect_account('openrouter'));self.assertNotIn('reading',json.loads(cache.read_text()))

    def test_only_fixed_endpoint_no_redirects(self):
        for url in ('https://example.test/api/v1/key','http://openrouter.ai/api/v1/key','https://openrouter.ai/api/v1/key?redirect=x'):
            with self.assertRaises(auto_sources.UsageError):auto_sources.get_json(url,{})
        self.assertIsNone(auto_sources.NoRedirect().redirect_request(None,None,None,None,None,None))
        for code in (401,403,429,500,302):
            with patch('urllib.request.OpenerDirector.open',side_effect=urllib.error.HTTPError('https://openrouter.ai/api/v1/key',code,'private message',{},None)):
                with self.assertRaises(auto_sources.UsageError) as error:auto_sources.get_json('https://openrouter.ai/api/v1/key',{})
                self.assertNotIn('private message',str(error.exception))

if __name__=='__main__':unittest.main()
