"""Automatic account usage. Secrets are never returned, logged, cached or refreshed."""
import base64, datetime, hashlib, json, os, pathlib, re, sqlite3, time, urllib.error, urllib.request
from quota_cache import valid_cache

ROOT=pathlib.Path(__file__).resolve().parent
DATA=ROOT/'data'
HOME=pathlib.Path.home()

class UsageError(Exception): pass
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs): return None

def numeric(v): return isinstance(v,(int,float)) and not isinstance(v,bool) and __import__('math').isfinite(v)
def timestamp(v):
    if numeric(v): return v/1000 if v>1e11 else v
    if isinstance(v,str):
        try:
            d=datetime.datetime.fromisoformat(v.replace('Z','+00:00'))
            return d.timestamp() if d.tzinfo else None
        except ValueError: pass
    return None

def usage_window(used,reset,label):
    if not numeric(used) or used<0: return None
    end=timestamp(reset)
    if reset is not None and end is None: return None
    if end is not None and end<=time.time(): return None
    return {'remaining':round(max(0,100-used),1),'reset':end,'label':label}

def parse_go(data):
    windows=[]
    for key,label in [('rolling','5-hour'),('weekly','Weekly'),('monthly','Monthly')]:
        w=data.get('usage',{}).get(key)
        if not isinstance(w,dict) or w.get('status') not in ('ok','rate-limited'):
            raise UsageError('Go returned an unknown quota window. Refresh after checking the Go console.')
        used=w.get('percent')
        if not numeric(used) or not 0<=used<=100: raise UsageError('Go returned an invalid percentage.')
        value=usage_window(100 if w['status']=='rate-limited' else used,w.get('resetsAt'),label)
        if value:windows.append(value)
    if not windows:raise UsageError('Go returned no active quota windows.')
    return windows

def parse_cursor(data):
    reset=data.get('billingCycleEnd')
    individual=data.get('individualUsage') or {}
    plan=individual.get('plan') or {}
    windows=[]
    # Explicit API percentage fields, not inferred token counts or API-rate costs.
    for key,label in [('totalPercentUsed','Included'),('autoPercentUsed','Cursor models'),('apiPercentUsed','Other models')]:
        value=usage_window(plan.get(key),reset,label)
        if value: windows.append(value)
    if not windows and numeric(plan.get('used')) and numeric(plan.get('limit')) and plan['limit']>0:
        value=usage_window(plan['used']/plan['limit']*100,reset,'Included')
        if value:windows.append(value)
    if not windows:raise UsageError('Cursor returned no recognized included-usage balance. The dashboard format may have changed.')
    return windows

def enabled():
    try:return json.loads((DATA/'connections.json').read_text()).get('allowStoredCredentials') is True
    except (OSError,ValueError):return False

def get_json(url,headers):
    # Only the two quota endpoints can receive stored credentials. Redirects are refused.
    if url not in ('https://opencode.ai/zen/go/v1/usage','https://cursor.com/api/usage-summary'):
        raise UsageError('Unapproved usage endpoint.')
    request=urllib.request.Request(url,headers={'Accept':'application/json','User-Agent':'Prism-Widget/1.1',**headers})
    try:
        with urllib.request.build_opener(NoRedirect).open(request,timeout=15) as response:
            raw=response.read(262145)
            if len(raw)>262144:raise UsageError('Usage response too large.')
            return json.loads(raw)
    except urllib.error.HTTPError as exc:
        if exc.code==403 and url.startswith('https://opencode.ai/'):
            try:
                payload=json.loads(exc.read(16384))
                if payload.get('error',{}).get('type')=='EntitlementError':
                    raise UsageError('The saved Go key has no Go subscription. Reconnect OpenCode Go to your subscribed workspace.')
            except (ValueError,TypeError,AttributeError):pass
        if exc.code in (401,403):raise UsageError('Sign-in needs attention. Open the provider app, sign in, then refresh.') from None
        if exc.code==429:raise UsageError('Usage service rate limited this check. Automatic checks pause for 15 minutes.') from None
        raise UsageError(f'Usage service returned HTTP {exc.code}.') from None
    except (urllib.error.URLError,TimeoutError):raise UsageError('Usage service could not be reached. Check your connection.') from None
    except (ValueError,TypeError):raise UsageError('Usage service returned an unexpected response.') from None

def cursor_token():
    path=pathlib.Path(os.environ.get('APPDATA',str(HOME/'AppData/Roaming')))/'Cursor/User/globalStorage/state.vscdb'
    if not path.exists():raise UsageError('Sign in to the Cursor desktop app first.')
    uri=path.as_uri()+'?mode=ro'
    # Avoid creating sidecars for a checkpointed, inactive database.
    if not pathlib.Path(str(path)+'-wal').exists() and not pathlib.Path(str(path)+'-shm').exists():uri+='&immutable=1'
    try:
        with sqlite3.connect(uri,uri=True,timeout=1) as conn:
            row=conn.execute("SELECT value FROM ItemTable WHERE key='cursorAuth/accessToken' LIMIT 1").fetchone()
        if not row:raise UsageError('Sign in to the Cursor desktop app first.')
        token=row[0]
        if isinstance(token,bytes):token=token.decode('utf-16-le' if b'\x00' in token else 'utf-8-sig')
        part=token.split('.')[1];payload=json.loads(base64.urlsafe_b64decode(part+'='*(-len(part)%4)))
        subject=payload.get('sub','').split('|')[-1]
        if not re.fullmatch(r'[A-Za-z0-9._-]+',subject):raise ValueError()
        if not numeric(payload.get('exp')) or payload['exp']<=time.time()+60:
            raise UsageError('Cursor sign-in expired. Open Cursor to renew it, then refresh Prism.')
        return subject,token
    except UsageError:raise
    except Exception:raise UsageError('Cursor sign-in could not be read. Open Cursor and sign in again.') from None

def go_token():
    path=pathlib.Path(os.environ.get('XDG_DATA_HOME',str(HOME/'.local/share')))/'opencode/auth.json'
    try:
        token=json.loads(path.read_text(encoding='utf-8-sig')).get('opencode-go',{}).get('key')
        if not isinstance(token,str) or not token or '\n' in token or '\r' in token:raise ValueError()
        return token
    except Exception:raise UsageError('Connect OpenCode Go inside OpenCode first, then refresh Prism.') from None

def collect_account(name):
    if not enabled():return {'status':'Connect','detail':'Enable automatic account connections in Settings.'}
    token=None
    try:
        if name=='cursor':
            subject,token=cursor_token();headers={'Cookie':'WorkosCursorSessionToken='+subject+'%3A%3A'+token};url='https://cursor.com/api/usage-summary';parse=parse_cursor
        else:
            token=go_token();headers={'Authorization':'Bearer '+token};url='https://opencode.ai/zen/go/v1/usage';parse=parse_go
        fingerprint=hashlib.sha256(token.encode()).hexdigest()
        cache_path=DATA/(name+'-automatic.json')
        cache={}
        try:cache=json.loads(cache_path.read_text())
        except (OSError,ValueError):pass
        if not valid_cache(cache,'scope','retryAt') or cache.get('scope')!=fingerprint:cache={}
        if cache.get('retryAt',0)>time.time():
            result=cache.get('reading')
            if result:
                active=[w for w in result['windows'] if w.get('reset') is None or w['reset']>time.time()]
                if active:
                    fresh=0<=time.time()-result['at']<=600 and not cache.get('error')
                    return {**result,'status':'Recent' if fresh else 'Stale','windows':active,'detail':cache.get('error','Last reading')}
            return {'status':'Waiting','detail':cache.get('error','Next automatic check shortly.')}
        try:
            windows=parse(get_json(url,headers))
            reading={'status':'Live','source':name.title()+' account usage','at':time.time(),'windows':windows}
            cache={'scope':fingerprint,'reading':reading,'retryAt':time.time()+300}
        except UsageError as exc:
            message=str(exc)
            # Rejected credentials invalidate the cached account reading.
            if 'Sign-in' in message or 'no Go subscription' in message:cache={}
            cache.update(scope=fingerprint,error=message,retryAt=time.time()+(900 if 'rate limited' in message else 300))
            reading={'status':'Unavailable','detail':message}
            if cache.get('reading'):
                reading={**cache['reading'],'status':'Stale','detail':message}
                reading['windows']=[w for w in reading['windows'] if w.get('reset') is None or w['reset']>time.time()]
                if not reading['windows']:reading={'status':'Unavailable','detail':message}
        DATA.mkdir(exist_ok=True);tmp=cache_path.with_name(cache_path.stem+f'-{os.getpid()}-{time.time_ns()}.tmp');tmp.write_text(json.dumps(cache));tmp.replace(cache_path)
        return reading
    except UsageError as exc:return {'status':'Connect','detail':str(exc)}
    except Exception:return {'status':'Unavailable','detail':'Account usage could not be read. Open the provider app and refresh.'}

def browser_claude():
    path=DATA/'claude-browser.json'
    try:data=json.loads(path.read_text())
    except (OSError,ValueError):return None
    at=data.get('at')
    if not numeric(at) or at>time.time()+300:return None
    if data.get('error'):return {'status':'Connect','detail':data['error']}
    windows=[]
    for w in data.get('windows',[]):
        if not isinstance(w,dict):continue
        value=usage_window(w.get('used'),w.get('reset'),w.get('label','Quota'))
        if value:windows.append(value)
    if not windows:return None
    return {'status':'Browser' if time.time()-at<600 else 'Stale','source':'Claude signed-in browser','at':at,'windows':windows}
