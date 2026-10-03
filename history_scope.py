"""Installation-local opaque scopes. Never return account identifiers or credentials."""
import hashlib, hmac, os, pathlib

def scope_id(directory, provider, identity):
    if not identity: return ''
    try:
        directory=pathlib.Path(directory);directory.mkdir(exist_ok=True)
        path=directory/'history-scope.keydata'
        try:
            with path.open('xb') as output: output.write(os.urandom(32))
        except FileExistsError: pass
        with path.open('rb') as source: salt=source.read(33)
        if len(salt)!=32: return ''
        raw=identity.encode() if isinstance(identity,str) else identity
        return hmac.new(salt,provider.encode()+b'\0'+raw,hashlib.sha256).hexdigest()
    except (OSError,ValueError,TypeError): return ''

def codex_scope(directory):
    # A credential-file change splits the series conservatively, including token rotation.
    path=pathlib.Path(os.environ.get('CODEX_HOME',str(pathlib.Path.home()/'.codex')))/'auth.json'
    try:
        with path.open('rb') as source: raw=source.read(1_048_577)
        return scope_id(directory,'codex',raw) if 0<len(raw)<=1_048_576 else ''
    except OSError: return ''
