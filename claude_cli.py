"""Read Claude Code's local /usage panel. Never send a model prompt or copy credentials."""
import datetime
import hashlib
import json
import os
import pathlib
import queue
import re
import shutil
import subprocess
import sys
import threading
import time
from quota_cache import valid_cache
from history_scope import scope_id

ROOT = pathlib.Path(__file__).resolve().parent
DATA = ROOT / 'data'
WORKSPACE = DATA / 'claude-cli-workspace'
if sys.prefix == sys.base_prefix:
    sys.path.insert(0, str(ROOT / 'vendor'))  # Compatibility with earlier portable builds.


def executable():
    native = pathlib.Path.home() / 'AppData/Roaming/npm/node_modules/@anthropic-ai/claude-code/bin/claude.exe'
    return shutil.which('claude.exe') or (str(native) if native.exists() else None)


def environment():
    env = os.environ.copy()
    # This switch also blocks the usage request, so /usage fails with "Failed to load usage data".
    env.pop('CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC', None)
    env['CLAUDE_CODE_DISABLE_AUTO_UPDATE'] = '1'
    return env


def arguments(exe):
    # This is a builtin slash command, never an LLM prompt. Hooks and MCP are disabled.
    return [exe, '/usage', '--tools', '', '--strict-mcp-config', '--mcp-config',
            '{"mcpServers":{}}', '--setting-sources', 'user', '--settings',
            '{"disableAllHooks":true}', '--no-chrome']


def subscription_auth(auth):
    return isinstance(auth, dict) and auth.get('loggedIn') is True and auth.get('authMethod') in ('claude.ai', 'oauth')


def parse_reset(section, now=None):
    """Next occurrence of a panel line such as 'Resets 10:49pm (Asia/Calcutta)' or 'Resets Oct 18, 4:29am'."""
    match = re.search(r'Resets?\s+(?:([A-Za-z]{3,9})\s+(\d{1,2}),?\s+)?(\d{1,2})(?::(\d{2}))?\s*([ap])m\b(?:\s*\(([^)\n]+)\))?', section, re.I)
    if not match:
        return None
    now = time.time() if now is None else now
    try:
        try:
            from zoneinfo import ZoneInfo
            zone = ZoneInfo(match[6].strip()) if match[6] else None
        except Exception:
            zone = None  # The panel prints local time; fall back when tz data is unavailable.
        zone = zone or datetime.datetime.now().astimezone().tzinfo
        hour, minute = int(match[3]), int(match[4] or 0)
        if not 1 <= hour <= 12 or minute > 59:
            return None
        hour = hour % 12 + (12 if match[5].lower() == 'p' else 0)
        current = datetime.datetime.fromtimestamp(now, zone)
        if match[1]:
            month = datetime.datetime.strptime(match[1][:3], '%b').month
            for year in (current.year, current.year + 1):
                stamp = datetime.datetime(year, month, int(match[2]), hour, minute, tzinfo=zone).timestamp()
                if stamp > now:
                    return stamp
            return None
        stamp = current.replace(hour=hour, minute=minute, second=0, microsecond=0)
        if stamp.timestamp() <= now:
            stamp += datetime.timedelta(days=1)
        return stamp.timestamp()
    except (ValueError, OverflowError, OSError):
        return None


def parse_screen(text):
    """Parse bounded quota sections, not cost/token counters or contribution percentages."""
    if re.search(r'failed to (?:load|fetch)|last known|cached usage|unable to (?:load|fetch)', text, re.I):
        return []
    heading = re.compile(r'^\s*(Current session|Current week(?:\s*\([^\n]+\))?)\s*$', re.I | re.M)
    headings = list(heading.finditer(text))
    windows = []
    for i, item in enumerate(headings):
        section = text[item.end():headings[i+1].start() if i+1 < len(headings) else len(text)]
        section = '\n'.join(section.splitlines()[:6])
        match = re.search(r'(?<![\d.\-])(\d+(?:\.\d+)?)\s*%\s*(used|left|remaining)\b', section, re.I)
        if not match:
            continue
        percentage = float(match[1])
        if not 0 <= percentage <= 100:
            continue
        label = '5-hour' if item[1].lower() == 'current session' else re.sub('Current week', '7-day', item[1], flags=re.I)
        windows.append({'label': label, 'remaining': round(100-percentage if match[2].lower() == 'used' else percentage, 1), 'reset': parse_reset(section)})
    return windows


def read_panel(exe):
    from winpty import PtyProcess, Backend
    import pyte
    WORKSPACE.mkdir(parents=True, exist_ok=True)
    terminal = PtyProcess.spawn(arguments(exe), cwd=str(WORKSPACE), env=environment(), dimensions=(55, 150), backend=Backend.ConPTY)
    messages = queue.Queue()
    def read():
        try:
            while True:
                messages.put(terminal.read(16384))
        except (EOFError, OSError):
            pass
    threading.Thread(target=read, daemon=True).start()
    screen = pyte.Screen(150, 55)
    stream = pyte.Stream(screen)
    end = time.monotonic() + 26
    result = None
    stable = 0
    failed = None
    retried = False
    try:
        while time.monotonic() < end:
            try:
                stream.feed(messages.get(timeout=.4))
            except queue.Empty:
                pass
            text = '\n'.join(screen.display)
            if re.search(r'failed to (?:load|fetch)|unable to (?:load|fetch)', text, re.I):
                # Transient load failures happen; retry once, then report the real cause.
                failed = failed or time.monotonic()
                if not retried and time.monotonic() - failed > 2:
                    retried = True
                    terminal.write('r')
                elif time.monotonic() - failed > 8:
                    return {'status': 'Unavailable', 'detail': 'Claude /usage could not load usage data. Check your connection and Claude sign-in.'}
            elif re.search(r'^\s*Error:', text, re.M):
                return {'status': 'Unavailable', 'detail': 'Claude CLI rejected its startup options. Check the installed CLI version.'}
            else:
                failed = None
            if re.search(r'trust this folder|trust the files|choose the text style|select.*theme|to change this later, run /theme|log in to Claude|sign in to Claude', text, re.I):
                return {'status': 'Connect', 'detail': 'Finish one-time Claude CLI setup using Settings → Sign in to Claude.'}
            windows = parse_screen(text)
            if windows:
                if windows != result:
                    result, stable = windows, time.monotonic()
                elif time.monotonic() - stable > 2:
                    return {'status': 'CLI', 'source': 'Claude Code /usage', 'at': time.time(), 'windows': windows}
        return {'status': 'Unavailable', 'detail': 'Claude CLI did not return a readable usage panel. Open Claude /usage to check sign-in or setup.'}
    finally:
        terminal.close(force=True)


def collect():
    exe = executable()
    if not exe:
        return {'status': 'Connect', 'detail': 'Install Claude Code, then sign in using Settings.'}
    try:
        proc = subprocess.run([exe, 'auth', 'status'], capture_output=True, text=True,
                              timeout=8, env=environment(), creationflags=0x08000000)
        auth = json.loads(proc.stdout)
    except Exception:
        return {'status': 'Unavailable', 'detail': 'Claude CLI authentication status could not be read.'}
    if not isinstance(auth,dict) or not auth.get('loggedIn'):
        return {'status': 'Connect', 'detail': 'Sign in to the Claude CLI from Settings to read subscription limits.'}
    if not subscription_auth(auth):
        return {'status': 'Connect', 'detail': 'Claude CLI needs a Claude subscription sign-in, rather than an API key.'}
    identity = hashlib.sha256(json.dumps({k: auth.get(k) for k in ('email','orgId','authMethod','subscriptionType')}, sort_keys=True).encode()).hexdigest()
    DATA.mkdir(parents=True, exist_ok=True)
    path = DATA / 'claude-cli.json'
    try:
        cached = json.loads(path.read_text(encoding='utf-8'))
        if not valid_cache(cached,'identity','checked') or cached.get('identity') != identity:
            cached = {}
    except (OSError, ValueError):
        cached = {}
    now = time.time()
    if 0 <= now - cached.get('checked', 0) < 300:
        previous = cached['reading']
        if previous.get('windows') and now-previous.get('at', 0) >= 600:
            return {'status': 'Unavailable', 'detail': 'Claude CLI reading expired. Waiting for the next automatic check.'}
        return {**previous,'scope':scope_id(DATA,'claude',identity)}
    try:
        reading = read_panel(exe)
    except ImportError:
        # Missing terminal libraries are a setup problem, not a Claude outage: say so, and do not
        # cache it, so the next refresh recovers as soon as Setup has installed them.
        return {'status': 'Unavailable', 'detail': 'Python terminal libraries are missing. Run Setup.cmd in the Prism folder, then refresh.'}
    except Exception:
        reading = {'status': 'Unavailable', 'detail': 'Claude CLI terminal could not be read. Run Setup and check the Claude CLI connection.'}
    # Authentication may change while the interactive panel is being collected.
    # Never attach another account's quota to the scope captured before launch.
    try:
        after_proc = subprocess.run([exe, 'auth', 'status'], capture_output=True, text=True,
                                    timeout=8, env=environment(), creationflags=0x08000000)
        after = json.loads(after_proc.stdout)
        after_identity = hashlib.sha256(json.dumps({k: after.get(k) for k in ('email','orgId','authMethod','subscriptionType')}, sort_keys=True).encode()).hexdigest() if isinstance(after,dict) and subscription_auth(after) else None
    except Exception:
        after_identity = None
    if after_identity != identity:
        return {'status': 'Unavailable', 'detail': 'Claude sign-in changed or could not be verified. Refresh after sign-in completes.'}
    old = cached.get('reading', {})
    # With no machine-readable reset timestamps, never carry old windows past ten minutes.
    if not reading.get('windows') and old.get('windows') and 0 <= now-old.get('at', 0) < 600:
        reading = {**old, 'status': 'Stale', 'detail': reading['detail']}
    tmp=path.with_name(f'claude-cli-{os.getpid()}-{time.time_ns()}.tmp')
    tmp.write_text(json.dumps({'identity': identity, 'checked': now, 'reading': reading}), encoding='utf-8')
    tmp.replace(path)
    return {**reading,'scope':scope_id(DATA,'claude',identity)}


def login():
    exe = executable()
    if not exe:
        print('Claude Code was not found. Install it from https://code.claude.com/docs/en/setup')
        return
    WORKSPACE.mkdir(parents=True, exist_ok=True)
    print('Sign in with your Claude subscription. Credentials stay managed by Claude Code.\n')
    try:
        status = subprocess.run([exe, 'auth', 'status'], capture_output=True, text=True, env=environment(), timeout=8)
        signed_in = subscription_auth(json.loads(status.stdout))
    except Exception:
        signed_in = False
    if not signed_in:
        signed_in = subprocess.run([exe, 'auth', 'login'], env=environment(), cwd=WORKSPACE).returncode == 0
        if signed_in:
            try:
                status = subprocess.run([exe, 'auth', 'status'], capture_output=True, text=True, env=environment(), timeout=8)
                signed_in = subscription_auth(json.loads(status.stdout))
            except Exception:
                signed_in = False
            if not signed_in:
                print('A Claude subscription login is still required. Check whether an API-key environment variable overrides your Claude account, then reopen this setup.')
    if signed_in:
        print('\nOpening /usage. Complete any first-run setup, then close this terminal.\n')
        subprocess.run(arguments(exe), env=environment(), cwd=WORKSPACE)
    try:
        (DATA / 'claude-cli.json').unlink(missing_ok=True)
    except OSError:
        pass


if __name__ == '__main__':
    if '--login' in sys.argv:
        login()
    else:
        print(json.dumps(collect()))
