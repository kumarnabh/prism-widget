"""Versioned, allowlisted display boundary; never forward arbitrary provider payloads."""
import math
import re
import time

STATUSES = {'Live', 'Recent', 'CLI', 'Browser', 'Feed', 'Stale', 'Connect', 'Waiting', 'Unavailable', 'Disabled'}
FRESH = {'Live', 'Recent', 'CLI', 'Browser', 'Feed'}
PRIVATE = re.compile(r'(?i)(?:\bsk-[a-z0-9_-]{12,}|\beyJ[a-z0-9_-]{15,}\.|\bgh[pousr]_[a-z0-9]{20,}|[\w.+-]+@[\w.-]+\.[a-z]{2,}|[a-z]:[\\/]Users[\\/])')

def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)

def text(value, maximum=120):
    return isinstance(value, str) and 0 < len(value) <= maximum and not any(ord(c) < 32 or ord(c) == 127 for c in value) and not PRIVATE.search(value)

def normalize(provider, raw, now=None):
    now = time.time() if now is None else now
    result = {'schema_version': 1, 'provider_id': provider['id'], 'display_name': provider['name'],
              'account_label': 'Default', 'capabilities': provider['capabilities'],
              'status': 'Unavailable', 'source': provider['source'], 'windows': [],
              'detail': 'Usage unavailable. Check this provider in Connections.'}
    if not isinstance(raw, dict): return result
    status = raw.get('status')
    if status not in STATUSES: return result
    result['status'] = status
    if text(raw.get('detail'), 400): result['detail'] = raw['detail']
    at = raw.get('at')
    windows = raw.get('windows', [])
    if not isinstance(windows, list) or len(windows) > 32: return {**result, 'status': 'Unavailable'}
    if not windows:
        if status in FRESH: result['status'] = 'Unavailable'
        return result
    if not number(at) or not 0 < at <= now: return {**result, 'status': 'Unavailable'}
    if status not in FRESH | {'Stale'}: return result
    clean, ids = [], set()
    for item in windows:
        if not isinstance(item, dict): return {**result, 'status': 'Unavailable'}
        remaining, reset, label = item.get('remaining'), item.get('reset'), item.get('label')
        identifier = item.get('id', label)
        if not number(remaining) or not 0 <= remaining <= 100 or not text(label) or not text(identifier, 160) or identifier in ids:
            return {**result, 'status': 'Unavailable'}
        if reset is not None and (not number(reset) or not 0 < reset < 253402300799): return {**result, 'status': 'Unavailable'}
        if 'rolling' in item and not isinstance(item['rolling'], bool): return {**result, 'status': 'Unavailable'}
        ids.add(identifier)
        if reset is not None and reset <= now: continue
        clean.append({'id': identifier, 'label': label, 'remaining': remaining, 'reset': reset, 'rolling': item.get('rolling', False)})
    result.update(at=at, windows=clean)
    if clean and not text(raw.get('detail'),400): result['detail']='Provider-reported allowance; estimates are calculated locally.'
    if status in FRESH and now - at > 600: result['status'] = 'Stale'
    if not clean: result['status'] = 'Unavailable'
    scope = raw.get('scope', '')
    if isinstance(scope, str) and re.fullmatch('[a-fA-F0-9]{64}', scope):
        result['scope'] = scope.lower()
        result['account_id'] = scope.lower()  # Opaque local scope; never an email or provider identity.
    return result
