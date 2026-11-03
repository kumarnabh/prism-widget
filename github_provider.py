"""Opt-in, GET-only GitHub.com monitoring. Raw responses and credentials stay in memory."""
import ctypes
import datetime as dt
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from github_metrics import CATALOG, REPOSITORY_NAME, finite, validate
from history_scope import scope_id

ROOT = pathlib.Path(__file__).resolve().parent
DATA = ROOT / 'data'
NAME = r'[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})'
REPO = REPOSITORY_NAME
MAX_BYTES = 4_000_000


def integer(value):
    return isinstance(value, int) and not isinstance(value, bool) and 0 <= value <= 1e15


class Failure(Exception):
    def __init__(self, code, retry=300):
        self.code, self.retry = code, retry


def strict_json(raw):
    def pairs(rows):
        result = {}
        for key, value in rows:
            if key in result: raise ValueError('Duplicate field')
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=pairs, parse_constant=lambda _: (_ for _ in ()).throw(ValueError('Non-finite')))


def load(path):
    try:
        with path.open('rb') as file: raw = file.read(MAX_BYTES + 1)
        return strict_json(raw) if len(raw) <= MAX_BYTES else {}
    except (OSError, ValueError): return {}


def save(path, value):
    path.parent.mkdir(exist_ok=True)
    temp = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    try:
        temp.write_text(json.dumps(value, allow_nan=False), encoding='utf-8')
        os.replace(temp, path)
    finally: temp.unlink(missing_ok=True)


def configuration():
    value = load(DATA / 'github-connection.json')
    if not isinstance(value, dict) or value.get('Enabled') is not True: return None
    mode = value.get('Mode', 'cli')
    org, repos = value.get('Organization', ''), value.get('Repositories', [])
    if mode not in {'cli', 'credential'} or not isinstance(org, str) or org and not re.fullmatch(NAME, org): raise Failure('format')
    if not isinstance(repos, list) or len(repos) > 20 or any(not isinstance(r, str) or not re.fullmatch(REPO, r) or r.split('/')[1] in {'.', '..'} for r in repos): raise Failure('format')
    return mode, org.lower(), sorted({repo.lower() for repo in repos})


def cli(*args):
    executable = shutil.which('gh')
    if not executable: raise Failure('auth')
    env = {k: v for k, v in os.environ.items() if k.upper() not in {'GH_TOKEN', 'GITHUB_TOKEN', 'GH_ENTERPRISE_TOKEN', 'GITHUB_ENTERPRISE_TOKEN', 'GH_DEBUG'}}
    env.update(GH_HOST='github.com', GH_PROMPT_DISABLED='1', GH_NO_UPDATE_NOTIFIER='1')
    process = subprocess.Popen([executable, 'auth', *args, '--hostname', 'github.com'], stdin=subprocess.DEVNULL,
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=env,
                               creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    output = bytearray()
    def read():
        while True:
            chunk = process.stdout.read(1024)
            if not chunk: break
            if len(output) + len(chunk) > 8192:
                process.kill(); break
            output.extend(chunk)
    worker = threading.Thread(target=read)
    worker.start()
    try:
        process.wait(timeout=4)
        worker.join(timeout=1)
        if process.returncode != 0 or worker.is_alive(): raise Failure('auth')
        return bytes(output).decode('utf-8').strip()
    except subprocess.TimeoutExpired: raise Failure('auth') from None
    finally:
        if process.poll() is None: process.kill()
        process.wait(); worker.join(timeout=1); process.stdout.close()


def credential_target():
    # Matches GitHubCredential.Target in WPF. Scope by installation, not global account.
    return 'Prism/GitHub/' + hashlib.sha256(str(ROOT).upper().encode('utf-8')).hexdigest()[:32]


def stored_token():
    if os.name != 'nt': raise Failure('auth')
    from ctypes import wintypes as w
    class Credential(ctypes.Structure):
        _fields_ = [('Flags', w.DWORD), ('Type', w.DWORD), ('TargetName', w.LPWSTR), ('Comment', w.LPWSTR),
                    ('LastWritten', w.FILETIME), ('CredentialBlobSize', w.DWORD), ('CredentialBlob', ctypes.POINTER(ctypes.c_ubyte)),
                    ('Persist', w.DWORD), ('AttributeCount', w.DWORD), ('Attributes', ctypes.c_void_p),
                    ('TargetAlias', w.LPWSTR), ('UserName', w.LPWSTR)]
    api = ctypes.WinDLL('advapi32', use_last_error=True)
    pointer = ctypes.POINTER(Credential)()
    api.CredReadW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, ctypes.POINTER(ctypes.POINTER(Credential))]
    api.CredReadW.restype = w.BOOL
    api.CredFree.argtypes = [ctypes.c_void_p]
    if not api.CredReadW(credential_target(), 1, 0, ctypes.byref(pointer)): raise Failure('auth')
    try:
        size = pointer.contents.CredentialBlobSize
        if not 1 <= size <= 8192: raise Failure('auth')
        return ctypes.string_at(pointer.contents.CredentialBlob, size).decode('utf-16-le')
    finally: api.CredFree(pointer)


def token_for(mode):
    if mode == 'cli':
        # /user verifies the token through the bounded client. Do not let gh auth
        # status make extra network calls during API backoff or token rechecks.
        token = cli('token')
    else: token = stored_token()
    if not re.fullmatch('[A-Za-z0-9_]{16,4096}', token): raise Failure('auth')
    return token


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs): return None


class Client:
    def __init__(self, token):
        self.token, self.deadline, self.bytes = token, time.monotonic() + 28, 0

    def get(self, path):
        # Never follow API-provided links or accept a host from preferences.
        allowed = (r'/user', r'/users/' + NAME + r'/settings/billing/usage',
                   r'/organizations/' + NAME + r'/settings/billing/usage',
                   r'/repos/' + REPO + r'(?:/actions/(?:runs|artifacts|cache/usage))?')
        parsed = urllib.parse.urlsplit(path)
        if parsed.scheme or parsed.netloc or parsed.fragment or any(part in {'.', '..'} for part in parsed.path.split('/')) or not any(re.fullmatch(p, parsed.path) for p in allowed): raise Failure('format')
        if self.deadline <= time.monotonic(): raise Failure('network')
        request = urllib.request.Request('https://api.github.com' + path, method='GET', headers={
            'Authorization': 'Bearer ' + self.token, 'Accept': 'application/vnd.github+json',
            'X-GitHub-Api-Version': '2026-03-10', 'User-Agent': 'Prism-Widget'})
        try:
            with urllib.request.build_opener(NoRedirect).open(request, timeout=min(6, self.deadline - time.monotonic())) as response:
                raw = bytearray()
                while True:
                    if time.monotonic() >= self.deadline: raise Failure('network')
                    chunk = response.read1(min(65536, MAX_BYTES + 1 - len(raw)))
                    if not chunk: break
                    raw.extend(chunk); self.bytes += len(chunk)
                    if len(raw) > MAX_BYTES or self.bytes > 12_000_000: raise Failure('format')
                value = strict_json(raw)
                if not isinstance(value, dict): raise Failure('format')
                return value, response.headers
        except urllib.error.HTTPError as error:
            try:
                if error.code == 401: raise Failure('auth') from None
                if error.code == 429 or error.code == 403 and (error.headers.get('X-RateLimit-Remaining') == '0' or error.headers.get('Retry-After')):
                    try: retry = float(error.headers.get('Retry-After', '0')) or float(error.headers.get('X-RateLimit-Reset', '0')) - time.time()
                    except ValueError: retry = 300
                    raise Failure('rate', max(60, min(86400, retry))) from None
                if error.code in {403, 404}: raise Failure('permission') from None
                raise Failure('network') from None
            finally: error.close()
        except (OSError, urllib.error.URLError, TimeoutError): raise Failure('network') from None
        except (ValueError, UnicodeError): raise Failure('format') from None

    def pages(self, path, key, cap=10000):
        rows = []
        for page in range(1, 102):
            value, headers = self.get(path + ('&' if '?' in path else '?') + 'per_page=100&page=' + str(page))
            batch, total = value.get(key), value.get('total_count')
            if not isinstance(batch, list) or len(batch) > 100 or not isinstance(total, int) or isinstance(total, bool) or total < 0 or total > cap: raise Failure('format')
            rows.extend(batch)
            if len(rows) > cap: raise Failure('format')
            more = 'rel="next"' in headers.get('Link', '')
            if not more:
                if len(rows) != total: raise Failure('format')
                return rows
        raise Failure('format')


def identity(client):
    value, _ = client.get('/user')
    identifier, login = value.get('id'), value.get('login')
    if not isinstance(identifier, int) or isinstance(identifier, bool) or identifier <= 0 or not isinstance(login, str) or not re.fullmatch(NAME, login): raise Failure('format')
    return str(identifier), login


def billing(payload, period):
    rows = payload.get('usageItems')
    if not isinstance(rows, list) or len(rows) > 20000: raise Failure('format')
    values, repo_values = {}, {}
    total = 0
    for row in rows:
        if not isinstance(row, dict): raise Failure('format')
        product, sku, unit = row.get('product'), row.get('sku'), row.get('unitType')
        if any(not isinstance(v, str) or len(v) > 200 for v in (product, sku, unit)): raise Failure('format')
        quantity, net = row.get('quantity'), row.get('netAmount')
        if not finite(quantity) or not finite(net) or not isinstance(row.get('date'), str): raise Failure('format')
        try: date = dt.date.fromisoformat(row['date'])
        except ValueError: raise Failure('format') from None
        if date.strftime('%Y-%m') != period: raise Failure('format')
        # The documented billing report amounts are USD. Refuse explicit incompatible currencies.
        if row.get('currency', 'USD') != 'USD': raise Failure('format')
        total += net
        repo = row.get('repositoryName')
        if repo:
            if not isinstance(repo, str) or not re.fullmatch(REPO, repo): raise Failure('format')
            repo_values[repo] = repo_values.get(repo, 0) + net
        p, s, u = product.lower(), sku.lower(), unit.lower()
        metric = None
        if p == 'actions' and u == 'minutes' and not any(word in s for word in ('storage', 'cache', 'artifact')):
            values['github-actions-spend'] = values.get('github-actions-spend', 0) + net
            metric = 'github-actions-minutes'
        elif p == 'actions' and u in {'gb-hours', 'gb-hr'} and any(word in s for word in ('storage', 'cache', 'artifact')):
            metric = 'github-actions-storage'
        elif p == 'packages':
            metric = 'github-packages-storage' if u in {'gb-hours', 'gb-hr'} and 'storage' in s else 'github-packages-transfer' if u == 'gb' and any(w in s for w in ('transfer', 'bandwidth')) else None
        elif p == 'codespaces':
            metric = 'github-codespaces-compute' if u == 'core-hours' else 'github-codespaces-storage' if u in {'gb-hours', 'gb-hr'} and 'storage' in s else None
        elif p in {'git lfs', 'lfs'}:
            metric = 'github-lfs-storage' if u in {'gb-hours', 'gb-hr'} and 'storage' in s else 'github-lfs-bandwidth' if u == 'gb' and any(w in s for w in ('bandwidth', 'transfer')) else None
        if metric: values[metric] = values.get(metric, 0) + quantity
    values['github-spend'] = total
    if repo_values: values['github-repository-spend'] = sum(repo_values.values())
    return values, repo_values


def repository(client, repo, month_start):
    # Validate metadata independently; only explicitly selected repos are queried.
    metadata, _ = client.get('/repos/' + repo)
    if not integer(metadata.get('id')) or metadata['id'] <= 0: raise Failure('format')
    values, failures = {}, []
    prefix = '/repos/' + repo + '/actions/'
    try:
        runs = client.pages(prefix + 'runs?created=' + month_start + '..' + dt.datetime.now(dt.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'), 'workflow_runs', 1000)
        if len(runs) >= 1000: raise Failure('format')  # The filtered API ceiling cannot prove completeness.
        ids = set()
        for row in runs:
            if not isinstance(row, dict) or not integer(row.get('id')) or row['id'] <= 0 or row['id'] in ids or not isinstance(row.get('created_at'), str) or row['created_at'] < month_start: raise Failure('format')
            try: created = dt.datetime.fromisoformat(row['created_at'].replace('Z', '+00:00'))
            except ValueError: raise Failure('format') from None
            if created.tzinfo is None or created.strftime('%Y-%m') != month_start[:7] or created > dt.datetime.now(dt.timezone.utc): raise Failure('format')
            ids.add(row['id'])
        values.update({'github-runs': len(runs), 'github-successes': sum(r.get('conclusion') == 'success' for r in runs), 'github-failures': sum(r.get('conclusion') == 'failure' for r in runs)})
    except Failure as error:
        if error.code in {'auth', 'rate', 'network'}: raise
        failures.append(error.code)
    try:
        artifacts = client.pages(prefix + 'artifacts', 'artifacts')
        total, ids = 0, set()
        for row in artifacts:
            if not isinstance(row, dict) or not integer(row.get('id')) or row['id'] <= 0 or row['id'] in ids or not integer(row.get('size_in_bytes')) or not isinstance(row.get('expired'), bool): raise Failure('format')
            ids.add(row['id'])
            if not row['expired']: total += row['size_in_bytes']
        values['github-artifacts'] = total
    except Failure as error:
        if error.code in {'auth', 'rate', 'network'}: raise
        failures.append(error.code)
    try:
        cache, _ = client.get(prefix + 'cache/usage')
        if not integer(cache.get('active_caches_size_in_bytes')): raise Failure('format')
        values['github-cache'] = cache['active_caches_size_in_bytes']
    except Failure as error:
        if error.code in {'auth', 'rate', 'network'}: raise
        failures.append(error.code)
    return values, failures


def collect():
    now = time.time()
    path = DATA / 'github-cache.json'
    try:
        config = configuration()
        if config is None: return validate({'status': 'Connect', 'message': 'consent'})
        mode, org, repos = config
        token = token_for(mode)
        context = scope_id(DATA, 'github-context', json.dumps([token, org.lower(), [r.lower() for r in repos]]))
        period = dt.datetime.fromtimestamp(now, dt.timezone.utc).strftime('%Y-%m')
        cached = load(path)
        if not isinstance(cached, dict): cached = {}
        retry_at = cached.get('retry_at', 0)
        if context and cached.get('context') == context and finite(retry_at) and now < retry_at <= now + 86400:
            try:
                result = validate(cached.get('reading'), now)
                if result.get('period') == period or not result.get('at'): return result
            except ValueError: pass
        client = Client(token)
        user_id, login = identity(client)
        scope = scope_id(DATA, 'github', json.dumps([user_id, token, org.lower(), [r.lower() for r in repos]]))
        if not scope: raise Failure('format')
        period = dt.datetime.fromtimestamp(now, dt.timezone.utc).strftime('%Y-%m')
        try:
            previous = validate(cached.get('reading'), now)
            if previous.get('scope') != scope or previous.get('period') != period: previous = None
        except ValueError: previous = None
        retry_at = cached.get('retry_at', 0)
        if previous and cached.get('context') == context and finite(retry_at) and now < retry_at <= now + 86400:
            # Recheck active credentials even when using cached values.
            if token_for(mode) != token: raise Failure('changed')
            return previous
        values, repo_values, failures = {}, {}, []
        billing_state = 'Unknown'
        try:
            endpoint = '/organizations/' + org if org else '/users/' + login
            payload, headers = client.get(endpoint + '/settings/billing/usage?year=' + period[:4] + '&month=' + str(int(period[5:])))
            # Report is not a paginated endpoint. Never treat a next link as a complete report.
            if 'rel="next"' in headers.get('Link', ''): raise Failure('format')
            values, repo_values = billing(payload, period)
        except Failure as error:
            if error.code in {'auth', 'rate', 'network'}: raise
            billing_state = 'Permission' if error.code == 'permission' else 'Unavailable'
            failures.append(error.code)
        if repos:
            totals, available = {}, {}
            for repo in repos:
                try:
                    current, errors = repository(client, repo, period + '-01T00:00:00Z')
                    failures.extend(errors)
                    for key, value in current.items():
                        totals[key] = totals.get(key, 0) + value
                        available[key] = available.get(key, 0) + 1
                except Failure as error:
                    if error.code in {'auth', 'rate', 'network'}: raise
                    failures.append(error.code)
            # A partial selected-repository total is unknown, never deceptively low.
            values.update({k: v for k, v in totals.items() if available[k] == len(repos)})
        final_id, _ = identity(client)
        if final_id != user_id or token_for(mode) != token or configuration() != config: raise Failure('changed')
        rows = []
        repo_ids = {'github-runs', 'github-successes', 'github-failures', 'github-artifacts', 'github-cache'}
        for definition in CATALOG:
            key = definition['id']
            rows.append({'id': key, 'unit': definition['unit'], 'value': values.get(key),
                         'state': 'Live' if key in values else ('Permission' if 'permission' in failures else 'Unavailable') if key in repo_ids and repos else 'Unknown' if key in repo_ids or definition['category'] in {'forecast', 'allowance'} else billing_state,
                         'source': 'repository' if key in repo_ids else 'billing'})
        result = validate({'status': 'Live' if values else billing_state, 'scope': scope, 'at': now, 'period': period,
                           'message': 'permission' if 'permission' in failures else 'format' if failures else None, 'metrics': rows,
                           'repositories': [{'scope': scope_id(DATA, 'github-repo', scope + '/' + repo.lower()), 'usd': value, 'name': repo} for repo, value in sorted(repo_values.items())]})
        save(path, {'context': context, 'reading': result, 'retry_at': now + 60})
        return result
    except Failure as error:
        if error.code in {'network', 'rate'} and 'previous' in locals() and previous:
            previous['status'] = 'Stale'; previous['message'] = error.code
            for row in previous['metrics']:
                if row['state'] == 'Live': row['state'] = 'Stale'
            result = validate(previous, now)
            save(path, {'context': context, 'reading': result, 'retry_at': now + error.retry})
            return result
        if error.code in {'auth', 'changed'}: path.unlink(missing_ok=True)
        result = validate({'status': 'Connect' if error.code in {'auth', 'changed'} else 'Permission' if error.code == 'permission' else 'Error', 'message': error.code})
        if error.code in {'network', 'rate'} and 'context' in locals() and context:
            save(path, {'context': context, 'reading': result, 'retry_at': now + error.retry})
        return result
    except (OSError, ValueError, TypeError, AttributeError):
        return validate({'status': 'Error', 'message': 'format'})
