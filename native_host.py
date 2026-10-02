"""Chrome/Edge native messaging host. Accepts quota-only messages, never credentials."""
import json,math,pathlib,struct,sys,time
ROOT=pathlib.Path(__file__).resolve().parent
LABELS={'5-hour','Weekly','Sonnet weekly','Opus weekly'}
def normalize(message):
    if not isinstance(message,dict) or message.get('provider')!='claude':raise ValueError()
    at=message.get('at')
    if not isinstance(at,(int,float)) or isinstance(at,bool) or not math.isfinite(at) or abs(time.time()-at)>300:raise ValueError()
    if 'error' in message:return {'at':at,'error':'Claude browser sync needs attention. Open the Prism extension.'}
    if not isinstance(message.get('windows'),list) or not 1<=len(message['windows'])<=4:raise ValueError()
    result=[];labels=set()
    for w in message['windows']:
        used=w.get('used');reset=w.get('reset');label=w.get('label')
        if label not in LABELS or label in labels:raise ValueError()
        labels.add(label)
        if not isinstance(used,(float,int)) or isinstance(used,bool) or not math.isfinite(used) or used<0:raise ValueError()
        if reset is not None and (not isinstance(reset,(float,int)) or isinstance(reset,bool) or not math.isfinite(reset)):raise ValueError()
        result.append({'used':used,'reset':reset,'label':label})
    return {'at':at,'windows':result}
def main():
    reply={'ok':False}
    try:
        manifest=json.loads((ROOT/'browser-extension/manifest.json').read_text())
        import base64,hashlib
        digest=hashlib.sha256(base64.b64decode(manifest['key'])).hexdigest()[:32]
        id=''.join(chr(97+int(c,16)) for c in digest)
        if len(sys.argv)<2 or sys.argv[1]!='chrome-extension://'+id+'/':raise ValueError()
        head=sys.stdin.buffer.read(4)
        if len(head)!=4:raise ValueError()
        size=struct.unpack('<I',head)[0]
        if not 0<size<=8192:raise ValueError()
        data=normalize(json.loads(sys.stdin.buffer.read(size)))
        folder=ROOT/'data';folder.mkdir(exist_ok=True)
        target=folder/'claude-browser.json';tmp=folder/f'claude-browser-{time.time_ns()}.tmp'
        tmp.write_text(json.dumps(data),encoding='utf-8');tmp.replace(target);reply={'ok':True}
    except Exception:pass
    encoded=json.dumps(reply).encode();sys.stdout.buffer.write(struct.pack('<I',len(encoded))+encoded);sys.stdout.buffer.flush()
if __name__=='__main__':main()
