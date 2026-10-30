"""Opted-in OpenCode Console quota via a private v2 CLI; never submit a prompt."""
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import tempfile
import threading
import time

import auto_sources
from history_scope import scope_id
from quota_cache import valid_cache

ROOT = pathlib.Path(__file__).resolve().parent
DATA = ROOT / 'data'
VERSION = '2.2.2'
FLAGS = ['--standalone', '--log-level', 'none']
USAGE_PATH = '/api/experimental/usage/limits'
UsageError = auto_sources.UsageError


def executable():
    managed = DATA / 'tools' / 'opencode-v2' / VERSION / 'opencode.exe'
    if managed.is_file():
        return str(managed)
    # Resolve npm shims to the native v2 binary; never invoke a shell or the legacy v1 package.
    for name in ('opencode2.exe', 'opencode2', 'opencode.exe', 'opencode'):
        found = shutil.which(name)
        if not found:
            continue
        path = pathlib.Path(found)
        if path.suffix.lower() in ('.cmd', '.bat', '.ps1'):
            path = path.parent / 'node_modules' / '@opencode' / 'cli' / 'bin' / 'opencode.exe'
        if path.is_file():
            return str(path)
    return None


def environment(folder, private=False):
    env = os.environ.copy()
    # Do not inherit a server override, injected config, DB override, or model/API-key override.
    for key in list(env):
        if key.startswith('OPENCODE_'):
            env.pop(key)
    env.update(OPENCODE_DISABLE_PROJECT_CONFIG='true', OPENCODE_DISABLE_DEFAULT_PLUGINS='true',
               OPENCODE_DISABLE_EXTERNAL_SKILLS='true', OPENCODE_DISABLE_AUTOUPDATE='true',
               OPENCODE_DISABLE_CLAUDE_CODE='true', OPENCODE_PERMISSION='{"*":"deny"}',
               OPENCODE_CONFIG_DIR=str(folder / 'config'), XDG_CONFIG_HOME=str(folder / 'config'),
               XDG_CACHE_HOME=str(folder / 'cache'), XDG_STATE_HOME=str(folder / 'state'))
    if private:
        env.update(XDG_DATA_HOME=str(folder / 'data'), OPENCODE_DB=str(folder / 'private.sqlite3'))
    # For export only, retain the provider's data location to find its saved Console sign-in.
    return env


def _kill(process):
    try:
        if os.name == 'nt':
            subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                           timeout=3, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    finally:
        if process.poll() is None:
            process.kill()


def run(exe, args, folder, env, deadline, payload=None, limit=262144):
    """Bound both elapsed time and captured bytes, including credential exports in memory."""
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise UsageError('OpenCode usage check timed out. Refresh after checking the CLI connection.')
    flags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
    with subprocess.Popen([exe, *args, *FLAGS], cwd=folder, env=env, shell=False,
                          stdin=subprocess.PIPE if payload is not None else subprocess.DEVNULL,
                          stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, creationflags=flags) as process:
        chunks = bytearray()
        overflow = threading.Event()
        def read():
            while True:
                part = process.stdout.read(4096)
                if not part:
                    return
                if len(chunks) + len(part) > limit:
                    overflow.set()
                    return
                chunks.extend(part)
        def write():
            try:
                process.stdin.write(payload)
                process.stdin.flush()
            except (BrokenPipeError, OSError):
                pass
            finally:
                process.stdin.close()
        reader = threading.Thread(target=read, daemon=True)
        reader.start()
        writer = None
        if payload is not None:
            writer = threading.Thread(target=write, daemon=True)
            writer.start()
        try:
            while process.poll() is None:
                if overflow.is_set():
                    raise UsageError('OpenCode returned an unexpectedly large response.')
                if time.monotonic() >= deadline:
                    raise UsageError('OpenCode usage check timed out. Refresh after checking the CLI connection.')
                time.sleep(.02)
            reader.join(max(0, deadline - time.monotonic()))
            if reader.is_alive() or overflow.is_set():
                raise UsageError('OpenCode returned an incomplete or oversized response.')
            if process.returncode:
                raise UsageError('OpenCode could not read Console usage. Check the CLI sign-in and connection.')
            return bytes(chunks)
        finally:
            if process.poll() is None or reader.is_alive():
                _kill(process)
            process.wait(timeout=3)
            reader.join(3)
            if writer:
                writer.join(3)


def export_auth(exe, folder, deadline):
    payload = run(exe, ['auth', 'export', 'opencode'], folder, environment(folder), deadline, limit=65536)
    try:
        credentials = json.loads(payload)
        if not isinstance(credentials, list) or not credentials:
            raise ValueError()
    except (ValueError, TypeError):
        raise UsageError('Sign in to OpenCode Console using OpenCode v2, then refresh Prism.') from None
    # Canonical JSON ignores export formatting; account selection and credential changes split history.
    identity = hashlib.sha256(json.dumps(credentials, sort_keys=True).encode()).hexdigest()
    return payload, identity


def parse_limits(payload):
    if not isinstance(payload, dict) or 'data' not in payload:
        raise UsageError('OpenCode returned an unknown usage format. Check the installed v2 CLI.')
    data = payload['data']
    if data is None:
        raise UsageError('Sign in to OpenCode Console using OpenCode v2, then refresh Prism.')
    if not isinstance(data, dict):
        raise UsageError('OpenCode returned an unknown usage format. Check the installed v2 CLI.')
    if data.get('go') is None:
        raise UsageError('Console is connected, but no Go allowance was reported. Free-model limits and Console budgets are not a Go quota.')
    if not isinstance(data['go'], dict):
        raise UsageError('OpenCode returned an invalid Go allowance.')
    usage = {}
    for key in ('rolling', 'weekly', 'monthly'):
        meter = data['go'].get(key)
        if (not isinstance(meter, dict) or not isinstance(meter.get('limited'), bool)
                or not auto_sources.numeric(meter.get('resetsAt')) or meter['resetsAt'] <= 0):
            raise UsageError('OpenCode returned an invalid Go quota window.')
        usage[key] = {'status': 'rate-limited' if meter['limited'] else 'ok',
                      'percent': meter.get('percent'), 'resetsAt': meter['resetsAt'] / 1000}
    return auto_sources.parse_go({'usage': usage})


def legacy_or_setup():
    reading = auto_sources.collect_account('opencode')
    if reading.get('windows'):
        return reading
    return {'status': 'Connect', 'detail': 'OpenCode v2 is missing or incompatible. For Console sign-in, run Setup.cmd and choose OpenCode support. Legacy Go-key quota is unavailable.'}


def collect():
    if not auto_sources.enabled():
        return {'status': 'Connect', 'detail': 'Enable this account connection in Settings.'}
    exe = executable()
    if not exe:
        # Existing v1 Go API-key installations remain supported without a v2 runtime.
        return legacy_or_setup()
    DATA.mkdir(parents=True, exist_ok=True)
    path = DATA / 'opencode-cli.json'
    deadline = time.monotonic() + 37  # Native worker has an independent 45-second process-tree deadline.
    try:
        with tempfile.TemporaryDirectory(prefix='opencode-private-', dir=DATA) as temporary:
            folder = pathlib.Path(temporary)
            version = run(exe, ['--version'], folder, environment(folder, private=True), deadline, limit=1024)
            if not re.fullmatch(rb'(?:opencode\s+)?v?2\.\d+\.\d+(?:[-+][\w.-]+)?', version.strip()):
                return legacy_or_setup()
            auth, identity = export_auth(exe, folder, deadline)
            cached = {}
            try:
                cached = json.loads(path.read_text(encoding='utf-8'))
                if (not valid_cache(cached, 'identity', 'retryAt') or cached['identity'] != identity
                        or not isinstance(cached.get('reading'), dict)):
                    cached = {}
            except (OSError, ValueError):
                pass
            if cached.get('retryAt', 0) > time.time():
                reading = cached['reading']
                if reading.get('windows'):
                    active = [w for w in reading['windows'] if w.get('reset') is None or w['reset'] > time.time()]
                    if not active:
                        return {'status': 'Waiting', 'detail': 'Go quota windows expired. Waiting for the next usage check.'}
                    reading = {**reading, 'windows': active, 'status': 'Recent' if time.time()-reading['at'] <= 600 else 'Stale'}
                return {**reading, 'scope': scope_id(DATA, 'opencode', identity)}
            env = environment(folder, private=True)
            try:
                run(exe, ['auth', 'import'], folder, env, deadline, payload=auth, limit=65536)
                result = run(exe, ['api', 'GET', USAGE_PATH], folder, env, deadline)
                windows = parse_limits(json.loads(result))
                reading = {'status': 'Live', 'source': 'OpenCode Console CLI usage', 'at': time.time(), 'windows': windows}
            except UsageError as exc:
                reading = {'status': 'Unavailable', 'detail': str(exc)}
            # Verify the active source sign-in again before attaching quota or writing its cache.
            _, after = export_auth(exe, folder, deadline)
            if after != identity:
                return {'status': 'Connect', 'detail': 'OpenCode sign-in changed. Refresh after sign-in completes.'}
            # Never carry a previous balance through a failed check or changed account.
            cache = {'identity': identity, 'retryAt': time.time()+300, 'reading': reading}
            tmp = path.with_name(f'opencode-cli-{os.getpid()}-{time.time_ns()}.tmp')
            tmp.write_text(json.dumps(cache), encoding='utf-8')
            tmp.replace(path)
            return {**reading, 'scope': scope_id(DATA, 'opencode', identity)}
    except UsageError as exc:
        return {'status': 'Connect' if 'Sign in' in str(exc) else 'Unavailable', 'detail': str(exc)}
    except (OSError, ValueError, subprocess.SubprocessError):
        return {'status': 'Unavailable', 'detail': 'OpenCode usage could not be read. Check the v2 CLI sign-in and connection.'}
