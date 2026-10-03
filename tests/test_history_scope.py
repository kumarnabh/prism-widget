import pathlib, tempfile, unittest
from history_scope import scope_id

class ScopeTests(unittest.TestCase):
    def test_scopes_are_stable_isolated_and_not_identity(self):
        root=pathlib.Path(__file__).resolve().parents[1]/'build/test-tmp';root.mkdir(parents=True,exist_ok=True)
        with tempfile.TemporaryDirectory(dir=root) as first, tempfile.TemporaryDirectory(dir=root) as second:
            a=scope_id(first,'test','synthetic-account')
            self.assertEqual(a,scope_id(first,'test','synthetic-account'))
            self.assertEqual(len(a),64)
            self.assertNotIn('synthetic',a)
            self.assertNotEqual(a,scope_id(first,'other','synthetic-account'))
            self.assertNotEqual(a,scope_id(first,'test','another-account'))
            self.assertNotEqual(a,scope_id(second,'test','synthetic-account'))
    def test_missing_or_corrupt_scope_fails_closed(self):
        root=pathlib.Path(__file__).resolve().parents[1]/'build/test-tmp';root.mkdir(parents=True,exist_ok=True)
        with tempfile.TemporaryDirectory(dir=root) as folder:
            self.assertEqual(scope_id(folder,'test',''),'')
            (pathlib.Path(folder)/'history-scope.keydata').write_bytes(b'invalid')
            self.assertEqual(scope_id(folder,'test','synthetic-account'),'')
