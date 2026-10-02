import json, pathlib, sys, tempfile, time, unittest
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
TMP=ROOT/'build/test-tmp'
TMP.mkdir(parents=True,exist_ok=True)
import providers as p

class QuotaTests(unittest.TestCase):
    def test_selected_provider_does_not_poll_other_accounts(self):
        with patch.object(p,'codex',return_value={'status':'Live'}) as codex, patch.object(p,'claude') as claude, patch.object(p.auto_sources,'collect_account') as accounts:
            self.assertEqual(p.collect(['codex']),{'codex':{'status':'Live'}})
            codex.assert_called_once();claude.assert_not_called();accounts.assert_not_called()
    def test_invalid_or_empty_selection(self):
        with patch.object(p,'codex') as codex, patch.object(p,'claude') as claude, patch.object(p.auto_sources,'collect_account') as accounts:
            with self.assertRaises(ValueError): p.collect(['unknown'])
            self.assertEqual(p.collect([]),{})
            codex.assert_not_called();claude.assert_not_called();accounts.assert_not_called()
    def test_zero_used_is_full(self): self.assertEqual(p.window(0,None,'x')['remaining'],100)
    def test_fully_used_is_zero(self): self.assertEqual(p.window(100,None,'x')['remaining'],0)
    def test_missing_is_unknown(self): self.assertIsNone(p.window(None,None,'x'))
    def test_invalid_is_unknown(self):
        for value in (-1,101,float('nan'),True,'50'): self.assertIsNone(p.window(value,None,'x'))
    def test_expired_is_unknown(self): self.assertIsNone(p.window(10,time.time()-1,'x'))
    def test_all_buckets_preserved(self):
        result={'rateLimitsByLimitId':{'a':{'primary':{'usedPercent':0,'windowDurationMins':300}},'b':{'primary':{'usedPercent':90,'windowDurationMins':10080},'secondary':{'usedPercent':None}}}}
        values=p.normalize_codex(result);self.assertEqual([v['remaining'] for v in values],[100,10])
    def test_null_primary_does_not_invent_balance(self): self.assertEqual(p.normalize_codex({'rateLimits':{'primary':None}}),[])
    def test_manual_and_stale(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder, patch.object(p,'DATA',pathlib.Path(folder)):
            f=pathlib.Path(folder)/'cursor-manual.json'
            f.write_text(json.dumps({'remaining':0,'total':100,'at':time.time()}));self.assertEqual(p.manual('cursor')['windows'][0]['remaining'],0)
            f.write_text(json.dumps({'remaining':20,'total':100,'at':time.time()-90000}));self.assertEqual(p.manual('cursor')['status'],'Stale manual')
            f.write_text(json.dumps({'remaining':101,'total':100,'at':time.time()}));self.assertIsNone(p.manual('cursor'))
    def test_claude_absent_and_expired_windows(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder, patch.object(p,'DATA',pathlib.Path(folder)), patch.object(p.claude_cli,'collect',return_value={'status':'Connect'}), patch.object(p.auto_sources,'browser_claude',return_value=None):
            f=pathlib.Path(folder)/'claude-feed.json'
            f.write_text(json.dumps({'at':time.time(),'rate_limits':{'five_hour':{'used_percentage':20,'resets_at':time.time()-1},'seven_day':{'used_percentage':100,'resets_at':time.time()+3600}}}))
            result=p.claude();self.assertEqual(len(result['windows']),1);self.assertEqual(result['windows'][0]['remaining'],0)

if __name__=='__main__': unittest.main()
