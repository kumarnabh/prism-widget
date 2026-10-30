import pathlib
import shutil
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
TMP = ROOT / 'build/test-tmp'
TMP.mkdir(parents=True, exist_ok=True)


class InstallerSafetyTests(unittest.TestCase):
    def run_installer(self, script, *arguments):
        return subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(script), *arguments],
                              capture_output=True, text=True, timeout=20,
                              creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))

    def fixture(self, root):
        script = root / 'scripts/install-opencode.ps1'; script.parent.mkdir()
        shutil.copyfile(ROOT / 'scripts/install-opencode.ps1', script)
        return script

    def test_bad_archive_is_rejected_before_extraction(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder:
            root = pathlib.Path(folder); script = self.fixture(root)
            archive = root / 'invalid.zip'; archive.write_bytes(b'synthetic invalid archive')
            result = self.run_installer(script, '-ArchivePath', str(archive))
            self.assertNotEqual(result.returncode, 0); self.assertIn('checksum mismatch', result.stderr)
            self.assertFalse((root / 'data/tools/opencode-v2/2.2.2/opencode.exe').exists())
            self.assertEqual(archive.read_bytes(), b'synthetic invalid archive')

    def test_existing_unknown_runtime_is_preserved(self):
        with tempfile.TemporaryDirectory(dir=TMP) as folder:
            root = pathlib.Path(folder); script = self.fixture(root)
            binary = root / 'data/tools/opencode-v2/2.2.2/opencode.exe'; binary.parent.mkdir(parents=True)
            binary.write_bytes(b'owner runtime')
            result = self.run_installer(script)
            self.assertNotEqual(result.returncode, 0); self.assertIn('preserved', result.stderr)
            self.assertEqual(binary.read_bytes(), b'owner runtime')


if __name__ == '__main__': unittest.main()
