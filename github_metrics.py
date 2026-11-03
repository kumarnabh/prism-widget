"""Reviewed typed GitHub boundary. No arbitrary API labels cross this module."""
import json
import math
import pathlib
import re
import time

ROOT = pathlib.Path(__file__).resolve().parent
CATALOG = json.loads((ROOT / 'github-metrics.json').read_text(encoding='utf-8'))
DEFINITIONS = {row['id']: row for row in CATALOG}
STATES = {'Live', 'Stale', 'Unknown', 'Unavailable', 'Error', 'Permission', 'Connect', 'Disabled'}
REPOSITORY_NAME = r'[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9_.-]{1,100}'


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and 0 <= value <= 1e15


def validate(raw, now=None):
    """Fail closed on corrupt cache/worker payloads; preserve legitimate zero."""
    now = time.time() if now is None else now
    if not isinstance(raw, dict) or raw.get('status') not in STATES:
        raise ValueError('Invalid GitHub state')
    result = {'schema_version': 1, 'provider_id': 'github', 'display_name': 'GitHub',
              'account_label': 'Default', 'windows': [], 'capabilities': ['typed_metrics', 'history', 'local_cli'],
              'source': 'GitHub REST API', 'status': raw['status'], 'metrics': [],
              'detail': 'Read-only GitHub usage. Billing reports may be delayed.'}
    # Messages are reviewed codes, never remote bodies or exception strings.
    messages = {
        'consent': 'Enable GitHub and choose a sign-in in Connections.',
        'auth': 'Sign in with gh auth login --hostname github.com, or replace the saved credential.',
        'permission': 'Billing needs Plan (read) for personal accounts or organization Administration (read). Repository Actions needs Actions (read).',
        'network': 'GitHub could not be reached. Last successful values, if any, retain their capture time.',
        'rate': 'GitHub rate limited this source. Collection will resume after backoff.',
        'changed': 'GitHub sign-in changed during collection. Refresh to read the current account.',
        'format': 'GitHub returned an unsupported or incomplete response.',
    }
    if raw.get('message') in messages:
        result['message'] = raw['message']
        result['detail'] = messages[raw['message']]
    scope = raw.get('scope')
    if isinstance(scope, str) and re.fullmatch('[a-f0-9]{64}', scope):
        result['scope'] = result['account_id'] = scope
    at = raw.get('at')
    period = raw.get('period')
    if not finite(at) or not 0 < at <= now or not isinstance(period, str) or not re.fullmatch(r'\d{4}-(0[1-9]|1[0-2])', period):
        return result
    if 'scope' not in result: raise ValueError('Invalid GitHub scope')
    result.update(at=at, period=period)
    if now - at > 3600 and result['status'] == 'Live':
        result['status'] = 'Stale'
    rows = raw.get('metrics', [])
    if not isinstance(rows, list) or len(rows) > len(CATALOG):
        raise ValueError('Invalid GitHub metrics')
    seen = set()
    for row in rows:
        if not isinstance(row, dict) or row.get('id') not in DEFINITIONS or row['id'] in seen:
            raise ValueError('Invalid GitHub metric')
        seen.add(row['id'])
        definition = DEFINITIONS[row['id']]
        state = row.get('state', 'Unknown')
        value = row.get('value')
        if state not in STATES or (value is not None and not finite(value)) or row.get('unit') != definition['unit']:
            raise ValueError('Invalid GitHub value')
        if definition['unit'] == '%' and value is not None and value > 100: raise ValueError('Invalid GitHub percentage')
        if state not in {'Live', 'Stale'} or result['status'] not in {'Live', 'Stale'}:
            value = None
        result['metrics'].append({'id': row['id'], 'unit': definition['unit'], 'category': definition['category'],
                                  'value': value, 'state': 'Stale' if state == 'Live' and result['status'] == 'Stale' else state,
                                  'source': 'repository' if row.get('source') == 'repository' else 'billing'})
    repos = raw.get('repositories', [])
    if not isinstance(repos, list) or len(repos) > 1000:
        raise ValueError('Invalid repository summary')
    clean = []
    for row in repos:
        if not isinstance(row, dict) or not re.fullmatch('[a-f0-9]{64}', str(row.get('scope', ''))) or not finite(row.get('usd')):
            raise ValueError('Invalid repository summary')
        name = row.get('name', '')
        if not isinstance(name, str) or name and (not re.fullmatch(REPOSITORY_NAME, name) or name.split('/')[1] in {'.', '..'}): raise ValueError('Invalid repository label')
        if re.search(r'(?i)(?:gh[pousr]_[a-z0-9]{20,}|github_pat_[a-z0-9_]{20,})', name): name = ''
        clean.append({'scope': row['scope'], 'usd': row['usd'], 'name': name})
    result['repositories'] = clean if result['status'] in {'Live', 'Stale'} else []
    return result
