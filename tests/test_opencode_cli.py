import hashlib
import json
import os
import pathlib
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import opencode_cli as oc
from provider_registry import adapters

TMP = ROOT / 'build/test-tmp'
TMP.mkdir(parents=True, exist_ok=True)
AUTH = b'[{"integrationID":"opencode","secret":"fixture-secret","active":true}]'


def limits():
    return {'location': {'directory': 'private-folder'}, 'data': {
        'workspace': {'id': 'private-workspace', 'name': 'Private account'},
        'go': {k: {'percent': p, 'limited': k == 'weekly', 'resetsAt': 4099680000000}
               for k, p in zip(('rolling', 'weekly', 'monthly'), (0, 99, 40))},
        'budgets': []}}


class ConsoleTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=TMP)
        self.addCleanup(self.temp.cleanup)
        self.folder = pathlib.Path(self.temp.name)
        for target, value in (('DATA', self.folder), ('executable', 'opencode.exe')):
            mock = patch.object(oc, target, return_value=value) if target == 'executable' else patch.object(oc, target, value)
            mock.start(); self.addCleanup(mock.stop)
        consent = patch.object(oc.auto_sources, 'enabled', return_value=True)
        consent.start(); self.addCleanup(consent.stop)
        self.calls = []

    def run_cli(self, exe, args, folder, env, deadline, payload=None, limit=None):
        self.calls.append((args, folder, env, payload))
        if args == ['--version']: return b'opencode v2.2.2\n'
        if args[:2] == ['auth', 'export']: return AUTH
        if args[:2] == ['auth', 'import']: return b'{}'
        if args == ['api', 'GET', oc.USAGE_PATH]: return json.dumps(limits()).encode()
        self.fail('Unexpected CLI invocation')

    def test_no_consent_never_discovers_cli_or_reads_auth(self):
        with patch.object(oc.auto_sources, 'enabled', return_value=False), patch.object(oc, 'executable') as discover, patch.object(oc, 'run') as run:
            self.assertEqual(oc.collect()['status'], 'Connect')
            discover.assert_not_called(); run.assert_not_called()

    def test_live_then_cache_uses_stdin_private_db_and_no_model_calls(self):
        with patch.object(oc, 'run', side_effect=self.run_cli):
            first = oc.collect(); second = oc.collect()
        self.assertEqual(first['status'], 'Live'); self.assertEqual(second['status'], 'Recent')
        self.assertEqual([w['remaining'] for w in first['windows']], [100, 0, 60])
        self.assertEqual(first['at'], second['at']); self.assertEqual(first['scope'], second['scope'])
        self.assertEqual(sum(args[0] == 'api' for args, *_ in self.calls), 1)
        imports = [row for row in self.calls if row[0][:2] == ['auth', 'import']]
        self.assertEqual(len(imports), 1); self.assertEqual(imports[0][3], AUTH)
        for args, folder, env, payload in self.calls:
            self.assertNotIn('run', args); self.assertNotIn('fixture-secret', ' '.join(args))
            self.assertEqual(env['OPENCODE_PERMISSION'], '{"*":"deny"}')
            self.assertEqual(env['OPENCODE_DISABLE_PROJECT_CONFIG'], 'true')
            self.assertFalse(folder.exists())
        self.assertEqual(imports[0][2]['OPENCODE_DB'], str(imports[0][1] / 'private.sqlite3'))
        saved = (self.folder / 'opencode-cli.json').read_text()
        for private in ('fixture-secret', 'Private account', 'private-workspace', 'private-folder'):
            self.assertNotIn(private, saved); self.assertNotIn(private, json.dumps(first))

    def test_account_change_during_collection_never_caches_quota(self):
        exports = iter([AUTH, AUTH.replace(b'fixture-secret', b'new-secret')])
        def run(*args, **kwargs):
            return next(exports) if args[1][:2] == ['auth', 'export'] else self.run_cli(*args, **kwargs)
        with patch.object(oc, 'run', side_effect=run):
            result = oc.collect()
        self.assertEqual(result['status'], 'Connect'); self.assertNotIn('windows', result)
        self.assertFalse((self.folder / 'opencode-cli.json').exists())

    def test_changed_identity_bypasses_old_cache(self):
        with patch.object(oc, 'run', side_effect=self.run_cli):
            first = oc.collect()
        with patch.object(oc, 'run', side_effect=lambda *a, **k: AUTH.replace(b'fixture-secret', b'new-secret') if a[1][:2] == ['auth', 'export'] else self.run_cli(*a, **k)):
            second = oc.collect()
        self.assertEqual(second['status'], 'Live'); self.assertNotEqual(first['scope'], second['scope'])

    def test_failed_request_does_not_reuse_a_previous_balance(self):
        with patch.object(oc, 'run', side_effect=self.run_cli): oc.collect()
        path = self.folder / 'opencode-cli.json'
        cached = json.loads(path.read_text()); cached['retryAt'] = 0; path.write_text(json.dumps(cached))
        def fail(*args, **kwargs):
            if args[1][0] == 'api': raise oc.UsageError('OpenCode could not read Console usage.')
            return self.run_cli(*args, **kwargs)
        with patch.object(oc, 'run', side_effect=fail):
            result = oc.collect(); again = oc.collect()
        self.assertEqual(result['status'], 'Unavailable'); self.assertNotIn('windows', result)
        self.assertNotIn('windows', again); self.assertNotIn('windows', json.loads(path.read_text())['reading'])

    def test_malformed_and_missing_reading_cache_is_refetched(self):
        identity = hashlib.sha256(json.dumps(json.loads(AUTH), sort_keys=True).encode()).hexdigest()
        for cache in ([], None, {'identity': identity, 'retryAt': time.time()+300}, {'identity': identity, 'retryAt': 'bad'}):
            with self.subTest(cache=cache):
                (self.folder / 'opencode-cli.json').write_text(json.dumps(cache))
                with patch.object(oc, 'run', side_effect=self.run_cli):
                    self.assertEqual(oc.collect()['status'], 'Live')

    def test_expired_cache_never_returns_expired_quota(self):
        with patch.object(oc, 'run', side_effect=self.run_cli): oc.collect()
        path = self.folder / 'opencode-cli.json'; cache = json.loads(path.read_text())
        for window in cache['reading']['windows']: window['reset'] = time.time()-1
        path.write_text(json.dumps(cache))
        with patch.object(oc, 'run', side_effect=self.run_cli):
            result = oc.collect()
        self.assertEqual(result['status'], 'Waiting'); self.assertNotIn('windows', result)

    def test_signed_out_invalid_auth_and_raw_errors_never_expose_old_quota(self):
        for response in (b'[]', b'{}', b'null', b'private-format-change'):
            with patch.object(oc, 'run', side_effect=lambda *a, **k: b'2.2.2' if a[1] == ['--version'] else response):
                result = oc.collect(); self.assertEqual(result['status'], 'Connect'); self.assertNotIn('windows', result)
        with patch.object(oc, 'run', side_effect=OSError('private-exception')):
            self.assertNotIn('private-exception', json.dumps(oc.collect()))

    def test_missing_or_legacy_cli_keeps_legacy_go_key_support(self):
        reading = {'status': 'Live', 'at': time.time(), 'windows': [{'label': '5-hour', 'remaining': 50, 'reset': None}]}
        with patch.object(oc, 'executable', return_value=None), patch.object(oc.auto_sources, 'collect_account', return_value=reading) as legacy:
            self.assertEqual(oc.collect(), reading); legacy.assert_called_once_with('opencode')
        with patch.object(oc, 'run', return_value=b'1.19.5'), patch.object(oc.auto_sources, 'collect_account', return_value={'status': 'Connect'}) as legacy:
            self.assertEqual(oc.collect()['status'], 'Connect'); legacy.assert_called_once_with('opencode')

    def test_missing_runtime_explains_setup_instead_of_rejected_legacy_signin(self):
        for result in ({'status': 'Connect', 'detail': 'Sign-in needs attention.'}, {'status': 'Waiting', 'detail': 'Sign-in needs attention.'}, {'status': 'Unavailable'}):
            with patch.object(oc, 'executable', return_value=None), patch.object(oc.auto_sources, 'collect_account', return_value=result):
                value = oc.collect()
                self.assertEqual(value['status'], 'Connect'); self.assertIn('Setup.cmd', value['detail'])
                self.assertIn('OpenCode v2', value['detail']); self.assertNotIn('Sign-in needs attention', value['detail'])

    def test_registry_dispatch_and_disconnect_clear_both_caches(self):
        adapter = adapters()['opencode']
        with patch.object(oc, 'collect', return_value={'status': 'Connect'}) as collect:
            adapter.collect(); collect.assert_called_once()
        data = self.folder / 'data'; data.mkdir()
        for name in ('opencode-cli.json', 'opencode-automatic.json', 'keep.json'): (data / name).write_text('{}')
        with patch('provider_registry.ROOT', self.folder): adapter.disconnect()
        self.assertEqual([p.name for p in data.iterdir()], ['keep.json'])


class ParserAndProcessTests(unittest.TestCase):
    def test_console_without_go_or_without_signin_never_invents_quota(self):
        for data in (None, {'workspace': {}, 'budgets': [{'limit': 100, 'spent': 10}]}, {'go': None, 'budgets': []}):
            with self.assertRaises(oc.UsageError): oc.parse_limits({'data': data})

    def test_invalid_partial_expired_and_changed_formats_fail_closed(self):
        for payload in (None, [], {}, {'data': []}, {'data': {'go': []}}):
            with self.assertRaises(oc.UsageError): oc.parse_limits(payload)
        for field, values in (('percent', (None, True, -1, 101, float('nan'), '50')), ('limited', (None, 1, 'false')), ('resetsAt', (None, True, -1, float('inf'), 'tomorrow'))):
            for value in values:
                payload = limits(); payload['data']['go']['rolling'][field] = value
                with self.subTest(field=field, value=value), self.assertRaises(oc.UsageError): oc.parse_limits(payload)
        payload = limits(); del payload['data']['go']['weekly']
        with self.assertRaises(oc.UsageError): oc.parse_limits(payload)
        payload = limits()
        for meter in payload['data']['go'].values(): meter['resetsAt'] = 1000
        with self.assertRaises(oc.UsageError): oc.parse_limits(payload)

    def test_milliseconds_and_rolling_semantics(self):
        windows = oc.parse_limits(limits())
        self.assertEqual(windows[0]['reset'], 4099680000)
        self.assertTrue(windows[0]['rolling']); self.assertFalse(windows[1]['rolling'])

    def test_environment_removes_injected_server_and_config_overrides(self):
        with patch.dict(os.environ, {'OPENCODE_SERVER': 'https://untrusted.test', 'OPENCODE_DB': 'private-original', 'OPENCODE_CONFIG_CONTENT': 'unsafe'}):
            env = oc.environment(ROOT / 'build/test-tmp', private=True)
        self.assertNotIn('OPENCODE_SERVER', env); self.assertNotIn('OPENCODE_CONFIG_CONTENT', env)
        self.assertNotEqual(env['OPENCODE_DB'], 'private-original')

    def test_runtime_detection_resolves_v2_npm_shim_without_shell(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder:
            root = pathlib.Path(folder); binary = root / 'node_modules/@opencode/cli/bin/opencode.exe'
            binary.parent.mkdir(parents=True); binary.write_bytes(b'fixture')
            with patch.object(oc, 'DATA', root / 'absent'), patch.object(oc.shutil, 'which', side_effect=lambda name: str(root / 'opencode.cmd') if name == 'opencode' else None):
                self.assertEqual(oc.executable(), str(binary))

    def test_real_process_stdout_limits_stdin_and_error_sanitization(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder:
            root = pathlib.Path(folder); deadline = lambda: time.monotonic()+5
            result = oc.run(sys.executable, ['-c', 'import sys;sys.stdout.buffer.write(sys.stdin.buffer.read())'], root, dict(os.environ), deadline(), payload=b'fixture-input')
            self.assertEqual(result, b'fixture-input')
            for script in ('print("x"*10000)', 'import sys;print("private-error",file=sys.stderr);sys.exit(1)'):
                with self.assertRaises(oc.UsageError) as error:
                    oc.run(sys.executable, ['-c', script], root, dict(os.environ), deadline(), limit=1024)
                self.assertNotIn('private-error', str(error.exception))

    def test_real_process_timeout_terminates_child_tree(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder:
            root = pathlib.Path(folder); marker = root / 'surviving-child.txt'
            child = 'import time,pathlib;time.sleep(1);pathlib.Path("surviving-child.txt").write_text("bad")'
            script = 'import subprocess,sys,time;subprocess.Popen([sys.executable,"-c",'+repr(child)+']);time.sleep(30)'
            with self.assertRaises(oc.UsageError):
                oc.run(sys.executable, ['-c', script], root, dict(os.environ), time.monotonic()+.3)
            time.sleep(1)
            self.assertFalse(marker.exists())


if __name__ == '__main__': unittest.main()
