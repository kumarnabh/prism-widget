import copy
import datetime as dt
import io
import json
import pathlib
import tempfile
import time
import unittest
import urllib.error
from unittest.mock import patch

import github_provider as gh
from github_metrics import CATALOG, validate
from provider_contract import normalize
from provider_registry import manifest

ROOT = pathlib.Path(__file__).resolve().parents[1]
TMP = ROOT / 'build' / 'test-tmp'
TMP.mkdir(parents=True, exist_ok=True)
TOKEN = 'fixture_credential_value_12345678'
PERIOD = dt.datetime.now(dt.timezone.utc).strftime('%Y-%m')


def item(product='Actions', sku='Actions Linux', quantity=100, unit='minutes', net=.8, **extra):
    return {'date': PERIOD + '-01', 'product': product, 'sku': sku, 'quantity': quantity,
            'unitType': unit, 'netAmount': net, 'grossAmount': .8, 'discountAmount': 0, **extra}


def reading(value=0, age=0):
    return {'status': 'Live', 'at': time.time()-age, 'scope': 'a'*64, 'period': PERIOD,
            'metrics': [{'id': 'github-spend', 'unit': 'USD', 'value': value, 'state': 'Live'}]}


class BoundaryTests(unittest.TestCase):
    def test_typed_contract_zero_missing_unknown_and_stale(self):
        spec = next(p for p in manifest() if p['id']=='github')
        for value in (0, 1, 1000000):
            result = normalize(spec, reading(value))
            self.assertEqual(result['windows'], [])
            self.assertEqual(result['metrics'][0]['value'], value)
            self.assertEqual(result['metrics'][0]['unit'], 'USD')
        self.assertEqual(normalize(spec, reading(5,3601))['status'],'Stale')
        self.assertEqual(normalize(spec, {'status':'Connect','metrics':reading()['metrics']})['metrics'], [])
        self.assertIsNone(validate({**reading(), 'metrics':[{'id':'github-actions-allowance','unit':'%','value':None,'state':'Unknown'}]})['metrics'][0]['value'])

    def test_bad_values_shapes_duplicates_and_private_fields_fail_closed(self):
        for invalid in (True, float('nan'),float('inf'),-1,'20',1e16):
            raw=reading();raw['metrics'][0]['value']=invalid
            with self.subTest(invalid=str(invalid)),self.assertRaises(ValueError):validate(raw)
        for invalid in ({},None,'changed'):
            raw=reading();raw['metrics']=[invalid]
            with self.assertRaises(ValueError):validate(raw)
        raw=reading();raw['metrics']*=2
        with self.assertRaises(ValueError):validate(raw)
        raw=reading();raw['metrics'][0]['unit']='GB'
        with self.assertRaises(ValueError):validate(raw)
        raw={**reading(),'token':TOKEN,'account_label':'private-account','detail':'private billing','repositoryName':'secret/repo'}
        result=json.dumps(validate(raw));self.assertNotIn(TOKEN,result);self.assertNotIn('private',result);self.assertNotIn('secret/repo',result)
        for raw in ('{"a":1,"a":2}', '{"value":NaN}', '{"value":Infinity}'):
            with self.assertRaises(ValueError):gh.strict_json(raw)

    def test_billing_compute_discounts_storage_and_product_units(self):
        rows=[item(net=0),item(quantity=50,net=.4),
              item('Actions','Actions Artifact Storage',2,'GB-hours',.1),
              item('Packages','Packages storage',24,'GB-hours',.2),
              item('Packages','Packages transfer',3,'GB',.3),
              item('Codespaces','Codespaces compute',4,'core-hours',.4),
              item('Codespaces','Codespaces storage',8,'GB-hours',.5),
              item('Git LFS','LFS storage',12,'GB-hours',.6),
              item('Git LFS','LFS bandwidth',2,'GB',.7)]
        values,_=gh.billing({'usageItems':rows},PERIOD)
        self.assertEqual(values['github-actions-minutes'],150)
        self.assertEqual(values['github-actions-spend'],.4)
        self.assertAlmostEqual(values['github-spend'],3.2)
        self.assertEqual(values['github-packages-storage'],24)
        self.assertEqual(values['github-packages-transfer'],3)
        self.assertEqual(values['github-codespaces-compute'],4)
        self.assertEqual(values['github-codespaces-storage'],8)
        self.assertEqual(values['github-lfs-storage'],12)
        self.assertEqual(values['github-lfs-bandwidth'],2)
        self.assertNotIn('github-actions-allowance',values)

    def test_free_paid_unknown_allowances_and_report_zero(self):
        for plan in ('free','pro','team','enterprise'):
            with self.subTest(plan=plan):
                values,_=gh.billing({'plan':plan,'usageItems':[item(quantity=0,net=0)]},PERIOD)
                self.assertEqual(values['github-actions-minutes'],0)
                self.assertEqual(values['github-spend'],0)
                self.assertNotIn('github-actions-allowance',values)
        values,_=gh.billing({'usageItems':[]},PERIOD)
        self.assertEqual(values,{'github-spend':0})
        values,_=gh.billing({'usageItems':[item('Codespaces','machine',3,'hours',1)]},PERIOD)
        self.assertNotIn('github-codespaces-compute',values)

    def test_partial_malformed_currency_and_date_are_never_summed(self):
        for payload in ({}, {'usageItems':None}, {'usageItems':[item(),{}]},
                        {'usageItems':[item(currency='EUR')]}, {'usageItems':[item(date='2000-01-01')]},
                        {'usageItems':[item(quantity=True)]}):
            with self.subTest(payload=payload),self.assertRaises(gh.Failure):gh.billing(payload,PERIOD)

    def test_configuration_normalizes_case_and_rejects_untrusted_paths(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(gh,'DATA',pathlib.Path(folder)):
            config={'Enabled':True,'Mode':'cli','Organization':'Fixture-Org','Repositories':['Owner/Repo','owner/repo']}
            gh.save(gh.DATA/'github-connection.json',config)
            self.assertEqual(gh.configuration(),('cli','fixture-org',['owner/repo']))
            for repository in ('a/..','a/.','a/b/other','https://evil.test/repo'):
                gh.save(gh.DATA/'github-connection.json',{**config,'Repositories':[repository]})
                with self.assertRaises(gh.Failure):gh.configuration()

    def test_repository_counts_snapshots_and_partial_permission(self):
        client=gh.Client(TOKEN)
        runs=[{'id':i+1,'created_at':PERIOD+'-01T00:00:00Z','conclusion':c} for i,c in enumerate(('success','failure','cancelled','timed_out',None))]
        artifacts=[{'id':1,'size_in_bytes':100,'expired':False},{'id':2,'size_in_bytes':200,'expired':True}]
        with patch.object(client,'get',side_effect=[({'id':1},{}),({'active_caches_size_in_bytes':0},{})]),patch.object(client,'pages',side_effect=[runs,artifacts]):
            values,failures=gh.repository(client,'fixture/repo',PERIOD+'-01T00:00:00Z')
            self.assertEqual(failures,[]);self.assertEqual(values['github-runs'],5);self.assertEqual(values['github-successes'],1);self.assertEqual(values['github-failures'],1);self.assertEqual(values['github-artifacts'],100);self.assertEqual(values['github-cache'],0)
        with patch.object(client,'get',side_effect=[({'id':1},{}),({'active_caches_size_in_bytes':2},{})]),patch.object(client,'pages',side_effect=[gh.Failure('permission'),artifacts]):
            values,failures=gh.repository(client,'fixture/repo',PERIOD+'-01T00:00:00Z');self.assertNotIn('github-runs',values);self.assertEqual(values['github-artifacts'],100);self.assertIn('permission',failures)
        with patch.object(client,'get',return_value=({'id':True},{})),self.assertRaises(gh.Failure):gh.repository(client,'fixture/repo',PERIOD+'-01T00:00:00Z')
        with patch.object(client,'get',side_effect=[({'id':1},{}),({'active_caches_size_in_bytes':2},{})]),patch.object(client,'pages',side_effect=[runs*200,artifacts]):
            values,failures=gh.repository(client,'fixture/repo',PERIOD+'-01T00:00:00Z');self.assertNotIn('github-runs',values);self.assertIn('format',failures)


class TransportTests(unittest.TestCase):
    def test_exact_get_only_hosts_and_paths(self):
        client=gh.Client(TOKEN)
        with patch.object(gh.urllib.request,'build_opener') as opener:
            for path in ('https://evil.test/user','//evil.test/user','/repos/a/b/actions/runs/1/cancel','/user#fragment','/repos/a/b/../../user'):
                with self.assertRaises(gh.Failure):client.get(path)
            opener.assert_not_called()
        class Response(io.BytesIO):
            headers={}
        requests=[]
        class Opener:
            def open(self, request, timeout):
                requests.append(request);return Response(b'{"id":1}')
        with patch.object(gh.urllib.request,'build_opener',return_value=Opener()):client.get('/user')
        self.assertEqual(requests[0].full_url,'https://api.github.com/user');self.assertEqual(requests[0].method,'GET')
        self.assertIsNone(gh.NoRedirect().redirect_request(None,None,None,None,None,None))

    def test_http_statuses_sanitized_with_backoff(self):
        for status,headers,code in ((401,{},'auth'),(403,{},'permission'),(404,{},'permission'),
                                    (429,{'Retry-After':'900'},'rate'),(403,{'X-RateLimit-Remaining':'0'},'rate'),
                                    (500,{},'network'),(302,{'Location':'https://evil.test'},'network')):
            error=urllib.error.HTTPError('https://api.github.com/user',status,'private response',headers,io.BytesIO(b'private billing'))
            with self.subTest(status=status),patch.object(gh.urllib.request,'build_opener') as opener:
                opener.return_value.open.side_effect=error
                with self.assertRaises(gh.Failure) as failure:gh.Client(TOKEN).get('/user')
                self.assertEqual(failure.exception.code,code);self.assertNotIn('private',str(failure.exception))
                if status==429:self.assertEqual(failure.exception.retry,900)

    def test_bounded_network_malformed_json_and_deadline(self):
        class Response(io.BytesIO):headers={}
        for data in (b'{',b'[]',b'{"a":NaN}',b'x'*(gh.MAX_BYTES+1)):
            with patch.object(gh.urllib.request,'build_opener') as opener:
                opener.return_value.open.return_value=Response(data)
                with self.assertRaises(gh.Failure):gh.Client(TOKEN).get('/user')
        client=gh.Client(TOKEN);client.deadline=0
        with self.assertRaises(gh.Failure):client.get('/user')

    def test_pagination_and_incomplete_or_capped_results(self):
        client=gh.Client(TOKEN)
        with patch.object(client,'get',side_effect=[({'total_count':101,'artifacts':[{}]*100},{'Link':'<ignored>; rel="next"'}),({'total_count':101,'artifacts':[{}]}, {})]) as get:
            self.assertEqual(len(client.pages('/repos/a/b/actions/artifacts','artifacts')),101)
            self.assertIn('page=2',get.call_args.args[0])
        for total,batch in ((3,[{}]),(1001,[])):
            with patch.object(client,'get',return_value=({'total_count':total,'workflow_runs':batch},{})),self.assertRaises(gh.Failure):client.pages('/repos/a/b/actions/runs','workflow_runs',1000)


class CollectionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(dir=TMP);self.directory=pathlib.Path(self.temp.name)
        self.data=patch.object(gh,'DATA',self.directory);self.data.start()
        self.tokens=patch.object(gh,'token_for',return_value=TOKEN);self.token=self.tokens.start()
        self.identities=patch.object(gh,'identity',return_value=('1','fixture-user'));self.identities.start()
        self.config=patch.object(gh,'configuration',return_value=('cli','',[]));self.config.start()
    def tearDown(self):
        self.config.stop();self.identities.stop();self.tokens.stop();self.data.stop();self.temp.cleanup()
    def values(self,result):return {m['id']:m['value'] for m in result['metrics']}
    def test_no_consent_no_token_no_network(self):
        with patch.object(gh,'configuration',return_value=None),patch.object(gh.Client,'get') as network:
            self.assertEqual(gh.collect()['status'],'Connect');self.token.assert_not_called();network.assert_not_called()
    def test_personal_organization_scopes_and_private_repository_redaction(self):
        scopes=[]
        for organization in ('','fixture-org'):
            with patch.object(gh,'configuration',return_value=('cli',organization,[])),patch.object(gh.Client,'get',return_value=({'usageItems':[item(repositoryName='private/repository')]},{})) as get:
                result=gh.collect();scopes.append(result['scope']);self.assertEqual(result['status'],'Live')
                self.assertIn('/organizations/fixture-org/' if organization else '/users/fixture-user/',get.call_args.args[0])
                self.assertNotIn('private/repository',json.dumps(result));self.assertNotIn(TOKEN,(self.directory/'github-cache.json').read_text())
                self.assertEqual(result['repositories'][0]['usd'],.8)
        self.assertNotEqual(*scopes)
    def test_partial_billing_permission_still_collects_repository_stats(self):
        with patch.object(gh,'configuration',return_value=('cli','',['private/repository'])),patch.object(gh.Client,'get',side_effect=gh.Failure('permission')),patch.object(gh,'repository',return_value=({'github-runs':0},[])):
            result=gh.collect();self.assertEqual(self.values(result)['github-runs'],0);self.assertIsNone(self.values(result)['github-spend']);self.assertEqual(result['status'],'Live');self.assertEqual(result['message'],'permission')
    def test_partial_repository_total_is_unknown_not_partial_sum(self):
        with patch.object(gh,'configuration',return_value=('cli','',['a/one','a/two'])),patch.object(gh.Client,'get',return_value=({'usageItems':[]},{})),patch.object(gh,'repository',side_effect=[({'github-runs':8},[]),({},['permission'])]):
            result=gh.collect();self.assertIsNone(self.values(result)['github-runs']);self.assertEqual(self.values(result)['github-spend'],0)
    def test_switch_during_fetch_invalidates_and_clears(self):
        with patch.object(gh,'identity',side_effect=[('1','one'),('2','two')]),patch.object(gh.Client,'get',return_value=({'usageItems':[item()]},{})):
            result=gh.collect();self.assertEqual(result['status'],'Connect');self.assertEqual(result['metrics'],[]);self.assertFalse((self.directory/'github-cache.json').exists())
    def test_cache_isolation_freshness_rate_backoff_and_auth_failure(self):
        with patch.object(gh.Client,'get',return_value=({'usageItems':[item()]},{})) as get:
            first=gh.collect();second=gh.collect();self.assertEqual(first['at'],second['at']);self.assertEqual(get.call_count,1)
        cache=gh.load(self.directory/'github-cache.json');cache['retry_at']=0;gh.save(self.directory/'github-cache.json',cache)
        with patch.object(gh.Client,'get',side_effect=gh.Failure('rate',900)) as get:
            stale=gh.collect();self.assertEqual(stale['status'],'Stale');self.assertEqual(stale['at'],first['at']);self.assertEqual(self.values(stale)['github-spend'],.8)
            again=gh.collect();self.assertEqual(again['status'],'Stale');self.assertEqual(get.call_count,1)
        with patch.object(gh,'token_for',return_value='fixture_second_credential_12345678'),patch.object(gh.Client,'get',side_effect=gh.Failure('network')):
            self.assertEqual(gh.collect()['metrics'],[])
        with patch.object(gh,'token_for',side_effect=gh.Failure('auth')):
            self.assertEqual(gh.collect()['status'],'Connect');self.assertFalse((self.directory/'github-cache.json').exists())
    def test_rate_limit_without_previous_reading_backs_off(self):
        with patch.object(gh.Client,'get',side_effect=gh.Failure('rate',900)) as get:
            self.assertEqual(gh.collect()['status'],'Error');gh.collect();self.assertEqual(get.call_count,1)

if __name__=='__main__':unittest.main()
