import importlib.util,json,pathlib,sys,tempfile,time,unittest
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
TMP=ROOT/'build/test-tmp'
TMP.mkdir(parents=True,exist_ok=True)
import auto_sources as a
import native_host as host

class AutomaticQuotaTests(unittest.TestCase):
    def test_go_known_zero_and_exhausted(self):
        reset='2099-10-01T12:00:00Z'
        data={'usage':{'rolling':{'status':'ok','percent':0,'resetsAt':reset},'weekly':{'status':'rate-limited','percent':99,'resetsAt':reset},'monthly':{'status':'ok','percent':40,'resetsAt':reset}}}
        self.assertEqual([w['remaining'] for w in a.parse_go(data)],[100,0,60])
    def test_go_missing_never_full(self):
        with self.assertRaises(a.UsageError):a.parse_go({'usage':{}})
    def test_cursor_current_schema(self):
        data={'billingCycleEnd':'2099-10-14T18:47:55.000Z','individualUsage':{'plan':{'enabled':True,'used':2000,'limit':2000,'totalPercentUsed':50.4888888,'autoPercentUsed':51.7266667,'apiPercentUsed':25.7333333}}}
        self.assertEqual([w['remaining'] for w in a.parse_cursor(data)],[49.5,48.3,74.3])
    def test_cursor_missing_never_full(self):
        with self.assertRaises(a.UsageError):a.parse_cursor({'individualUsage':{}})
    def test_expired_window_not_available(self):self.assertIsNone(a.usage_window(0,time.time()-20,'x'))
    def test_invalid_numeric(self):
        for value in (None,True,float('nan'),-1,'30'):self.assertIsNone(a.usage_window(value,None,'x'))
    def test_credentials_not_read_without_consent(self):
        with patch.object(a,'enabled',return_value=False),patch.object(a,'go_token') as token:
            self.assertEqual(a.collect_account('opencode')['status'],'Connect');token.assert_not_called()
    def test_redirects_never_forward_auth(self):self.assertIsNone(a.NoRedirect().redirect_request(None,None,None,None,None,None))
    def test_unapproved_endpoint_rejected(self):
        with self.assertRaises(a.UsageError):a.get_json('https://example.com',{'Authorization':'test'})
    def test_native_host_strips_extra_fields(self):
        data=host.normalize({'provider':'claude','at':time.time(),'cookies':'must-not-persist','windows':[{'used':0,'label':'5-hour','reset':None,'token':'must-not-persist'}]})
        self.assertNotIn('cookies',data);self.assertNotIn('token',data['windows'][0])
    def test_native_host_rejects_stale_input(self):
        with self.assertRaises(ValueError):host.normalize({'provider':'claude','at':time.time()-400,'windows':[{'used':0,'label':'5-hour'}]})
    def test_cache_backoff_and_no_credential_storage(self):
        data={'usage':{k:{'status':'ok','percent':10,'resetsAt':'2099-10-01T12:00:00Z'} for k in ('rolling','weekly','monthly')}}
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(a,'DATA',pathlib.Path(folder)),patch.object(a,'enabled',return_value=True),patch.object(a,'go_token',return_value='test-secret-value'),patch.object(a,'get_json',return_value=data) as fetch:
            self.assertEqual(a.collect_account('opencode')['status'],'Live');self.assertEqual(a.collect_account('opencode')['status'],'Recent');self.assertEqual(fetch.call_count,1)
            self.assertNotIn('test-secret-value',(pathlib.Path(folder)/'opencode-automatic.json').read_text())

if __name__=='__main__': unittest.main()
