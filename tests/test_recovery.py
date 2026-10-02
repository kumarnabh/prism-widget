import json,pathlib,sys,tempfile,time,unittest
from types import SimpleNamespace
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
TMP=ROOT/'build/test-tmp';TMP.mkdir(parents=True,exist_ok=True)
import providers as p
import auto_sources as a
import claude_cli as c
from quota_cache import valid_reading

def reading(status='CLI',remaining=50,age=0):
    return {'status':status,'at':time.time()-age,'windows':[{'remaining':remaining,'label':'5-hour','reset':None}]}

class RecoveryTests(unittest.TestCase):
    def test_unreadable_codex_telemetry_uses_api(self):
        with patch.object(p,'codex_telemetry',side_effect=OSError('unreadable')),patch.object(p,'codex_api',return_value=reading()) as api:
            self.assertIn('windows',p.codex());api.assert_called_once()
    def test_failed_cli_uses_healthy_browser(self):
        browser=reading('Browser',10)
        with patch.object(c,'collect',side_effect=RuntimeError('failed')),patch.object(a,'browser_claude',return_value=browser):
            self.assertEqual(p.claude(),browser)
    def test_stale_cli_cannot_mask_current_browser(self):
        browser=reading('Browser',10)
        with patch.object(c,'collect',return_value=reading('Stale',75,500)),patch.object(a,'browser_claude',return_value=browser):
            self.assertEqual(p.claude(),browser)
    def test_newest_stale_fallback_is_preserved(self):
        browser=reading('Stale',20,700)
        with patch.object(c,'collect',return_value=reading('Stale',75,900)),patch.object(a,'browser_claude',return_value=browser),patch.object(p,'claude_statusline',return_value=None):
            self.assertEqual(p.claude(),browser)
    def test_malformed_cli_cache_is_refetched(self):
        for malformed in ([],None,{'identity':'x','checked':'bad'}, {'identity':'x','checked':time.time(),'reading':[]}):
            with self.subTest(cache=malformed),tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(c,'DATA',pathlib.Path(folder)),patch.object(c,'executable',return_value='claude.exe'),patch.object(c.subprocess,'run',return_value=SimpleNamespace(stdout='{"loggedIn":true,"authMethod":"claude.ai"}')),patch.object(c,'read_panel',return_value=reading()) as panel:
                (pathlib.Path(folder)/'claude-cli.json').write_text(json.dumps(malformed));self.assertEqual(c.collect()['status'],'CLI');panel.assert_called_once()
    def test_malformed_account_cache_is_refetched(self):
        data={'usage':{k:{'status':'ok','percent':10,'resetsAt':'2099-10-01T12:00:00Z'} for k in ('rolling','weekly','monthly')}}
        for malformed in ([],None,{'scope':'x','retryAt':'bad'}):
            with self.subTest(cache=malformed),tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(a,'DATA',pathlib.Path(folder)),patch.object(a,'enabled',return_value=True),patch.object(a,'go_token',return_value='fake-test-credential'),patch.object(a,'get_json',return_value=data) as fetch:
                (pathlib.Path(folder)/'opencode-automatic.json').write_text(json.dumps(malformed));self.assertEqual(a.collect_account('opencode')['status'],'Live');fetch.assert_called_once()
    def test_malformed_windows_and_future_capture_are_rejected(self):
        for value in ([None],[{'remaining':True,'label':'quota'}],[{'remaining':120,'label':'quota'}],[{'remaining':50,'label':'quota','reset':'bad'}]):
            self.assertFalse(valid_reading({'status':'CLI','at':time.time(),'windows':value}))
        data=reading();data['at']=time.time()+3600;self.assertFalse(valid_reading(data))

if __name__=='__main__':unittest.main()
