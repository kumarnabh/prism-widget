import contextlib,hashlib,io,json,pathlib,sys,tempfile,time,unittest
from types import SimpleNamespace
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT));TMP=ROOT/'build/test-tmp';TMP.mkdir(parents=True,exist_ok=True)
import auto_sources as a
import claude_cli as c
import claude_feed as feed

class ReviewRegressions(unittest.TestCase):
    def cached(self,folder,error=None,expired=False):
        now=time.time()
        cache={'scope':hashlib.sha256(b'fake-test-credential').hexdigest(),'retryAt':now+200,'reading':{'status':'Live','at':now-60,'windows':[{'remaining':25,'reset':now-1 if expired else now+3600,'label':'5-hour'}]}}
        if error:cache['error']=error
        (pathlib.Path(folder)/'opencode-automatic.json').write_text(json.dumps(cache))
    def test_expired_recent_cache_never_returns_balance(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(a,'DATA',pathlib.Path(folder)),patch.object(a,'enabled',return_value=True),patch.object(a,'go_token',return_value='fake-test-credential'),patch.object(a,'get_json') as fetch:
            self.cached(folder,expired=True);result=a.collect_account('opencode')
            self.assertNotIn('windows',result);fetch.assert_not_called()
    def test_failed_refresh_backoff_stays_stale(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(a,'DATA',pathlib.Path(folder)),patch.object(a,'enabled',return_value=True),patch.object(a,'go_token',return_value='fake-test-credential'):
            self.cached(folder,error='Network unavailable');result=a.collect_account('opencode')
            self.assertEqual(result['status'],'Stale');self.assertEqual(result['detail'],'Network unavailable')
    def test_api_login_requires_subscription_login(self):
        responses=[SimpleNamespace(stdout=json.dumps({'loggedIn':True,'authMethod':'api_key'})),SimpleNamespace(returncode=0),SimpleNamespace(stdout=json.dumps({'loggedIn':True,'authMethod':'claude.ai'})),SimpleNamespace(returncode=0)]
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(c,'DATA',pathlib.Path(folder)),patch.object(c,'WORKSPACE',pathlib.Path(folder)),patch.object(c,'executable',return_value='claude.exe'),patch.object(c.subprocess,'run',side_effect=responses) as run,contextlib.redirect_stdout(io.StringIO()):
            c.login()
            self.assertEqual(run.call_args_list[1].args[0],['claude.exe','auth','login'])
            self.assertEqual(run.call_args_list[-1].args[0][1],'/usage')
    def test_api_override_after_login_does_not_open_panel(self):
        responses=[SimpleNamespace(stdout='{"loggedIn":true,"authMethod":"api_key"}'),SimpleNamespace(returncode=0),SimpleNamespace(stdout='{"loggedIn":true,"authMethod":"api_key"}')]
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(c,'DATA',pathlib.Path(folder)),patch.object(c,'WORKSPACE',pathlib.Path(folder)),patch.object(c,'executable',return_value='claude.exe'),patch.object(c.subprocess,'run',side_effect=responses) as run,contextlib.redirect_stdout(io.StringIO()):
            c.login();self.assertEqual(run.call_count,3)
    def test_feed_discards_unexpected_nested_fields(self):
        data={'rate_limits':{'five_hour':{'used_percentage':10,'resets_at':100,'token':'do-not-copy'},'api_key':'do-not-copy'},'transcript':'do-not-copy'}
        self.assertEqual(feed.normalize(data),{'five_hour':{'used_percentage':10,'resets_at':100}})
    def test_feed_rejects_malformed_numeric_fields(self):
        for v in (True,-1,101,float('nan'),'40'):
            self.assertEqual(feed.normalize({'rate_limits':{'five_hour':{'used_percentage':v}}}),{})

if __name__=='__main__':unittest.main()
