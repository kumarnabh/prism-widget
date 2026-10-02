"""Publication boundary check. Reports paths and rules, never matched secret values."""
import pathlib,re,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[1]
RULES={
    'private-key':rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
    'github-token':rb'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})\b',
    'provider-key':rb'\bsk-(?:ant-|proj-)?[A-Za-z0-9_-]{24,}\b',
    'jwt':rb'\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{15,}\b',
    'personal-windows-path':rb'[A-Za-z]:[\\/]+Users[\\/]+(?!Public\b|Default\b)[A-Za-z0-9]',
}
FORBIDDEN={'data','vendor','build','work','bin','obj','__pycache__','.venv'}
def main():
    files=subprocess.check_output(['git','ls-files','-z'],cwd=ROOT).decode().split('\0')
    issues=[]
    for name in filter(None,files):
        p=pathlib.PurePosixPath(name)
        if any(part.lower() in FORBIDDEN for part in p.parts) or p.name.lower().startswith('.env') or p.suffix.lower() in {'.exe','.dll','.pdb','.log','.pem','.key','.zip','.db','.sqlite','.sqlite3','.vscdb'} or p.name in {'native-host.json','selftest.json','auth.json','credentials.json','.credentials.json','Cookies'} or p.name.startswith('preview'):
            issues.append((name,'private/runtime file'))
        raw=subprocess.check_output(['git','show',':'+name],cwd=ROOT)
        for rule,pattern in RULES.items():
            if re.search(pattern,raw): issues.append((name,rule))
    for path,rule in issues: print(f'{path}: {rule}')
    print(f'Checked {len(list(filter(None,files)))} staged files; {len(issues)} publication findings.')
    return bool(issues)
if __name__=='__main__':sys.exit(main())
