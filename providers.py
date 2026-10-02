"""Read-only quota collectors. Credential reuse requires an explicit saved opt-in."""
import concurrent.futures, datetime, json, math, os, pathlib, queue, shutil, sqlite3, subprocess, sys, threading, time
import auto_sources
import claude_cli

ROOT = pathlib.Path(__file__).resolve().parent
DATA = ROOT / 'data'
HOME = pathlib.Path.home()

def number(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)

def window(used, reset, label):
    if not number(used) or not 0 <= used <= 100:
        return None
    if number(reset) and reset <= time.time():
        return None
    return {'remaining': round(100-used, 1), 'reset': reset if number(reset) else None, 'label': label}

def normalize_codex(result):
    buckets = result.get('rateLimitsByLimitId') or {'codex': result.get('rateLimits')}
    windows = []
    for key, bucket in buckets.items():
        if not isinstance(bucket, dict): continue
        for name in ('primary', 'secondary'):
            w = bucket.get(name)
            if not isinstance(w, dict): continue
            mins = w.get('windowDurationMins')
            label = ('7-day' if mins == 10080 else '5-hour' if mins == 300 else f'{mins:g} min' if number(mins) else name)
            if len(buckets) > 1: label = f'{bucket.get("limitName") or key} / {label}'
            item = window(w.get('usedPercent'), w.get('resetsAt'), label)
            if item: windows.append(item)
    return windows

def codex_telemetry():
    root=pathlib.Path(os.environ.get('CODEX_HOME',str(HOME/'.codex')))/'sessions'
    files=[]
    for ago in range(8):
        day=datetime.datetime.now()-datetime.timedelta(days=ago)
        folder=root/day.strftime('%Y/%m/%d')
        files.extend(folder.glob('*.jsonl'))
    newest=None
    for path in sorted(files,key=lambda p:p.stat().st_mtime,reverse=True)[:12]:
        with path.open('rb') as f:
            f.seek(max(0,path.stat().st_size-512000))
            if f.tell(): f.readline()
            lines=f.readlines()
        for line in reversed(lines):
            try: data=json.loads(line)
            except ValueError: continue
            payload=data.get('payload',{})
            if payload.get('type')!='token_count' or not payload.get('rate_limits'): continue
            raw=payload['rate_limits']; bucket={}
            for key in ('primary','secondary'):
                w=raw.get(key)
                if w: bucket[key]={'usedPercent':w.get('used_percent'),'resetsAt':w.get('resets_at'),'windowDurationMins':w.get('window_minutes')}
            windows=normalize_codex({'rateLimits':bucket})
            at=datetime.datetime.fromisoformat(data['timestamp'].replace('Z','+00:00')).timestamp()
            if windows and (newest is None or at>newest['at']):
                newest={'status':'Recent' if time.time()-at<600 else 'Stale','source':'Codex local quota telemetry','at':at,'windows':windows}
            break
    return newest

def codex():
    try:recent=codex_telemetry()
    except Exception:recent=None
    if recent and recent['status']=='Recent': return recent
    try: return codex_api()
    except Exception:
        if recent: return recent
        raise

def codex_api():
    exe = shutil.which('codex.exe') or shutil.which('codex')
    if not exe:
        candidates = list((HOME/'AppData/Local/OpenAI/Codex/bin').glob('*/codex.exe'))
        if candidates: exe = str(max(candidates, key=lambda x:x.stat().st_mtime))
    if not exe: return {'status':'Connect', 'detail':'Sign in to the Codex CLI to read your quota.'}
    p = subprocess.Popen([exe,'app-server'], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                         stderr=subprocess.DEVNULL, text=True, encoding='utf-8', creationflags=0x08000000 if os.name=='nt' else 0)
    messages=queue.Queue()
    def read():
        for line in p.stdout:
            try: messages.put(json.loads(line))
            except ValueError: pass
    threading.Thread(target=read,daemon=True).start()
    def send(msg): p.stdin.write(json.dumps(msg)+'\n'); p.stdin.flush()
    def response(id):
        end=time.monotonic()+18
        while time.monotonic()<end:
            try: msg=messages.get(timeout=max(.1,end-time.monotonic()))
            except queue.Empty: break
            if msg.get('id')==id:
                if 'error' in msg: raise RuntimeError('Account usage unavailable; check Codex sign-in.')
                return msg.get('result',{})
        raise TimeoutError('Codex connection timed out.')
    try:
        send({'id':1,'method':'initialize','params':{'clientInfo':{'name':'prism_widget','title':'Prism Widget','version':'1.0.0'}}})
        response(1)
        send({'method':'initialized'})
        send({'id':2,'method':'account/rateLimits/read'})
        windows=normalize_codex(response(2))
        if not windows: return {'status':'Unavailable','detail':'This account returned no active quota windows.'}
        return {'status':'Live','source':'Codex account API','at':time.time(),'windows':windows}
    finally:
        p.terminate()
        try: p.wait(timeout=3)
        except subprocess.TimeoutExpired: p.kill(); p.wait()

def claude():
    candidates=[]
    for source in (claude_cli.collect,auto_sources.browser_claude,claude_statusline):
        try:
            reading=source()
            if not isinstance(reading,dict):continue
            if reading.get('windows') and reading.get('status') in ('CLI','Browser','Feed'):return reading
            candidates.append(reading)
        except Exception:continue
    stale=[reading for reading in candidates if reading.get('windows')]
    if stale:return max(stale,key=lambda reading:reading.get('at',0))
    return candidates[0] if candidates else {'status':'Connect','detail':'Connect Claude Code from Settings.'}

def claude_statusline():
    path=DATA/'claude-feed.json'
    if not path.exists(): return None
    data=json.loads(path.read_text(encoding='utf-8-sig'))
    at=data.get('at')
    if not number(at) or at>time.time()+300: return {'status':'Unavailable','detail':'Claude feed has no valid timestamp.'}
    windows=[]
    for key,label in [('five_hour','5-hour'),('seven_day','7-day'),('spend_limit','Spend limit')]:
        w=(data.get('rate_limits') or {}).get(key) or {}
        item=window(w.get('used_percentage'),w.get('resets_at'),label)
        if item: windows.append(item)
    if not windows: return {'status':'Waiting','detail':'Open Claude Code and complete a turn to refresh limits.'}
    return {'status':'Feed' if time.time()-at<600 else 'Stale','source':'Claude Code status line','at':at,'windows':windows}

def opencode():
    path=pathlib.Path(os.environ.get('XDG_DATA_HOME',str(HOME/'.local/share')))/'opencode/opencode.db'
    if not path.exists(): return {'status':'Connect','detail':'Open the Go console to check your allowance.'}
    # Only aggregate assistant usage. Never read message text, keys, or auth files.
    with sqlite3.connect(path.as_uri()+'?mode=ro',uri=True,timeout=2) as conn:
        conn.execute('PRAGMA temp_store=MEMORY')
        deadline=time.monotonic()+3
        conn.set_progress_handler(lambda: int(time.monotonic()>deadline),10000)
        row=conn.execute('''SELECT COUNT(*),
          COALESCE(SUM(COALESCE(json_extract(data,'$.tokens.input'),0) + COALESCE(json_extract(data,'$.tokens.output'),0)
            + COALESCE(json_extract(data,'$.tokens.reasoning'),0) + COALESCE(json_extract(data,'$.tokens.cache.read'),0)
            + COALESCE(json_extract(data,'$.tokens.cache.write'),0)),0),
          COALESCE(SUM(json_extract(data,'$.cost')),0)
          FROM message WHERE time_created >= ? AND json_extract(data,'$.role')='assistant'
          AND json_extract(data,'$.providerID')='opencode-go' ''',
          (int((time.time()-7*86400)*1000),)).fetchone()
    return {'status':'Local usage','source':'OpenCode local database','at':time.time(),
            'tokensUsed':row[1],'cost':row[2], 'detail':'Go tokens used over 7 days, including cache. Check the Go console for remaining quota.'}

def manual(name):
    path=DATA/f'{name}-manual.json'
    if not path.exists(): return None
    data=json.loads(path.read_text(encoding='utf-8-sig'))
    remaining=data.get('remaining'); total=data.get('total'); at=data.get('at')
    if not all(number(v) for v in (remaining,total,at)) or total<=0 or not 0<=remaining<=total: return None
    return {'status':'Manual' if time.time()-at<86400 else 'Stale manual','source':'Your saved reading',
            'at':at,'windows':[{'remaining':round(100*remaining/total,1),'label':data.get('label') or 'Saved allowance','reset':None}],
            'detail':f'{remaining:g} / {total:g} {data.get("unit","units")} remaining'}

def collect():
    result={}
    tasks={'codex':codex,'claude':claude,'opencode':lambda:auto_sources.collect_account('opencode'),
           'cursor':lambda:auto_sources.collect_account('cursor')}
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        jobs={pool.submit(fn):name for name,fn in tasks.items()}
        for future in concurrent.futures.as_completed(jobs):
            name=jobs[future]
            try: result[name]=future.result()
            except Exception: result[name]={'status':'Unavailable','detail':'Usage source unavailable. Check its connection.'}
    return result

if __name__=='__main__':
    print(json.dumps(collect(),ensure_ascii=True))
