import json, pathlib, sys, tempfile, time, unittest
from types import SimpleNamespace
from unittest.mock import patch
ROOT=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
TMP=ROOT/'build/test-tmp'
TMP.mkdir(parents=True,exist_ok=True)
import claude_cli as c

class CliTests(unittest.TestCase):
    def test_subscription_windows_and_zero(self):
        screen='Current session\n████ 0% used\nResets 9pm\n\nCurrent week (all models)\n████ 100% used\nCurrent week (Sonnet only)\n10.5% used\n'
        self.assertEqual([w['remaining'] for w in c.parse_screen(screen)],[100,0,89.5])
    def test_cost_is_not_quota(self):
        self.assertEqual(c.parse_screen('Total cost: $0.00\nUsage: 0 input, 0 output\n'),[])
    def test_rejects_stale_error_and_invalid(self):
        for text in ['Current session\n101% used','Current session\n-1% used','Current session\nlast known\n20% used','Current session\nFailed to load\n20% used']:
            self.assertEqual(c.parse_screen(text),[])
    def test_no_inference_command(self):
        args=c.arguments('claude.exe')
        self.assertEqual(args[1],'/usage')
        self.assertNotIn('-p',args)
        self.assertNotIn('--no-session-persistence',args)
        self.assertIn('{"disableAllHooks":true}',args)
    def test_rendered_terminal_cursor_updates(self):
        import pyte
        screen=pyte.Screen(150,55);stream=pyte.Stream(screen)
        stream.feed('Current session\r\n25% used');stream.feed('\x1b[2;1H75% used')
        self.assertEqual(c.parse_screen('\n'.join(screen.display))[0]['remaining'],25)
    def test_signed_out_does_not_start_panel(self):
        with patch.object(c,'executable',return_value='claude.exe'),patch.object(c.subprocess,'run',return_value=SimpleNamespace(stdout='{"loggedIn":false}')),patch.object(c,'read_panel') as panel:
            self.assertEqual(c.collect()['status'],'Connect');panel.assert_not_called()
    def test_fresh_cache_and_failure_preserve_timestamp(self):
        auth=json.dumps({'loggedIn':True,'authMethod':'claude.ai','email':'private@example.test'})
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(c,'DATA',pathlib.Path(folder)),patch.object(c,'executable',return_value='claude.exe'),patch.object(c.subprocess,'run',return_value=SimpleNamespace(stdout=auth)),patch.object(c,'read_panel') as panel:
            at=time.time();panel.return_value={'status':'CLI','at':at,'windows':[{'label':'5-hour','remaining':50,'reset':None}]}
            self.assertEqual(c.collect()['status'],'CLI');c.collect();self.assertEqual(panel.call_count,1)
            path=pathlib.Path(folder)/'claude-cli.json';data=json.loads(path.read_text());self.assertNotIn('private@example',path.read_text());data['checked']=at-301;path.write_text(json.dumps(data));panel.return_value={'status':'Unavailable','detail':'failed'}
            reading=c.collect();self.assertEqual(reading['status'],'Stale');self.assertEqual(reading['at'],at)
    def test_account_change_discards_old_reading(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(c,'DATA',pathlib.Path(folder)),patch.object(c,'executable',return_value='claude.exe'),patch.object(c.subprocess,'run',return_value=SimpleNamespace(stdout='{"loggedIn":true,"authMethod":"claude.ai","email":"new@example.test"}')),patch.object(c,'read_panel',return_value={'status':'Unavailable','detail':'failed'}):
            (pathlib.Path(folder)/'claude-cli.json').write_text(json.dumps({'identity':'different','checked':time.time(),'reading':{'status':'CLI','at':time.time(),'windows':[{'remaining':90}]}}))
            self.assertNotIn('windows',c.collect())
    def test_account_switch_during_panel_discards_scope_and_cache(self):
        first=SimpleNamespace(stdout=json.dumps({'loggedIn':True,'authMethod':'claude.ai','email':'first@example.test'}))
        second=SimpleNamespace(stdout=json.dumps({'loggedIn':True,'authMethod':'claude.ai','email':'second@example.test'}))
        for after in (second,SimpleNamespace(stdout='{"loggedIn":false}'),SimpleNamespace(stdout='malformed')):
            with self.subTest(after=after),tempfile.TemporaryDirectory(dir=TMP) as folder,patch.object(c,'DATA',pathlib.Path(folder)),patch.object(c,'executable',return_value='claude.exe'),patch.object(c.subprocess,'run',side_effect=[first,after]),patch.object(c,'read_panel',return_value={'status':'CLI','at':time.time(),'windows':[{'label':'Session','remaining':50,'reset':None}]}):
                result=c.collect();self.assertNotIn('windows',result);self.assertNotIn('scope',result)
                self.assertFalse((pathlib.Path(folder)/'claude-cli.json').exists())

if __name__=='__main__': unittest.main()
