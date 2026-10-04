"""Reviewed built-ins only. The manifest is package data, never downloaded code."""
import json
import pathlib
import shutil
from dataclasses import dataclass
from provider_contract import normalize

ROOT = pathlib.Path(__file__).resolve().parent
CAPABILITIES = {'remaining', 'reset', 'multiple_windows', 'history', 'multi_account', 'plan', 'api_credits', 'local_cli'}

def manifest():
    payload = json.loads((ROOT / 'provider-manifest.json').read_text(encoding='utf-8'))
    if payload.get('version') != 1: raise ValueError('Unsupported provider registry')
    rows = payload['providers']
    ids = set()
    for row in rows:
        if row['id'] in ids or row['adapter'] not in {'codex', 'cursor', 'opencode', 'claude', 'openrouter'} or row['id'] != row['adapter']:
            raise ValueError('Unregistered adapter')
        if not set(row['capabilities']) <= CAPABILITIES or not 60 <= row['interval'] <= 3600: raise ValueError('Invalid provider capabilities')
        ids.add(row['id'])
    return rows

@dataclass(frozen=True)
class ProviderAdapter:
    metadata: dict

    def availability(self):
        # Discovery never reads credentials. Install-location fallbacks remain adapter-owned.
        key = self.metadata['id']
        return 'Available' if key not in {'claude', 'codex'} or shutil.which(key) else 'Check connection'

    def collect(self):
        import providers
        key = self.metadata['adapter']
        if key == 'codex': return providers.codex()
        if key == 'claude': return providers.claude()
        return providers.auto_sources.collect_account(key)

    def normalize(self, raw, now=None): return normalize(self.metadata, raw, now)

    def diagnostics(self):
        return {'provider_id': self.metadata['id'], 'availability': self.availability(),
                'authentication': self.metadata['authentication'], 'source': self.metadata['source']}

    def disconnect(self):
        # Clear only this adapter's normalized quota cache. Provider credentials remain provider-owned.
        if self.metadata['id'] in {'cursor', 'opencode', 'openrouter'}:
            (ROOT / 'data' / (self.metadata['id'] + '-automatic.json')).unlink(missing_ok=True)

def adapters(): return {row['id']: ProviderAdapter(row) for row in manifest()}
