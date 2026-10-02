import pathlib
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import providers
import doctor


class PublicReleaseTests(unittest.TestCase):
    def test_active_codex_account_wins_over_recent_old_account(self):
        current = {'status': 'Live', 'windows': [{'remaining': 5}]}
        old = {'status': 'Recent', 'windows': [{'remaining': 90}]}
        with patch.object(providers, 'codex_api', return_value=current), patch.object(providers, 'codex_telemetry', return_value=old) as telemetry:
            self.assertEqual(providers.codex(), current)
            telemetry.assert_not_called()

    def test_signed_out_codex_does_not_show_previous_account(self):
        with patch.object(providers, 'codex_api', return_value={'status': 'Connect'}), patch.object(providers, 'codex_telemetry') as telemetry:
            self.assertEqual(providers.codex(), {'status': 'Connect'})
            telemetry.assert_not_called()

    def test_unavailable_codex_marks_history_unverified_without_restamping(self):
        old = {'status': 'Recent', 'at': 1000, 'windows': [{'remaining': 90}]}
        with patch.object(providers, 'codex_api', side_effect=TimeoutError), patch.object(providers, 'codex_telemetry', return_value=old):
            result = providers.codex()
            self.assertEqual(result['status'], 'Stale')
            self.assertEqual(result['at'], 1000)
            self.assertIn('Unverified', result['source'])

    def test_codex_unavailable_and_corrupt_history_preserve_failure(self):
        with patch.object(providers, 'codex_api', side_effect=TimeoutError), patch.object(providers, 'codex_telemetry', side_effect=OSError):
            with self.assertRaises(TimeoutError):
                providers.codex()

    def test_diagnostics_never_include_detected_paths(self):
        with patch.object(doctor.shutil, 'which', return_value='/private/example/cli'), patch.object(doctor.importlib.util, 'find_spec', return_value=object()):
            report = doctor.report()
            self.assertNotIn('/private', str(report))
            self.assertEqual(report['cli_on_path'], {'codex': True, 'claude': True})
            self.assertEqual(set(report), {'python', 'architecture', 'platform', 'isolated_environment', 'terminal_dependencies', 'cli_on_path'})

if __name__ == '__main__':
    unittest.main()
