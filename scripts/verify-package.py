"""Check release boundaries without extracting or printing matched contents."""
import pathlib
import sys
import zipfile

FORBIDDEN = {'data', 'vendor', '.venv', '.git', '__pycache__', 'build', 'work', 'obj'}
REQUIRED = {'Prism.exe', 'Prism.dll', 'coreclr.dll', 'Setup.cmd', 'Setup.ps1', 'doctor.py',
            'providers.py', 'requirements.txt', 'LICENSE', 'PRIVACY.md',
            'docs/legal/dotnet-LICENSE.txt', 'docs/legal/wpf-LICENSE.txt'}

def check(archive):
    issues = []
    with zipfile.ZipFile(archive) as package:
        names = set()
        for entry in package.infolist():
            name = entry.filename.replace('\\', '/')
            names.add(name)
            path = pathlib.PurePosixPath(name)
            if path.is_absolute() or '..' in path.parts or any(p.lower() in FORBIDDEN for p in path.parts):
                issues.append(name)
            if path.name.lower().startswith(('.env', 'preview')) or path.name.lower() in {'auth.json', '.credentials.json', 'native-host.json', 'selftest.json', 'cookies'} or path.suffix.lower() in {'.pdb', '.log', '.key', '.pem', '.db', '.vscdb', '.sqlite'}:
                issues.append(name)
        issues.extend('Missing: ' + name for name in sorted(REQUIRED - names))
    for issue in issues:
        print(issue)
    print(f'Package boundary: {len(names)} entries; {len(issues)} findings.')
    return bool(issues)

if __name__ == '__main__':
    sys.exit(check(sys.argv[1]))
