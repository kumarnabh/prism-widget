"""Claude Code status-line bridge: persist only validated numeric quota fields."""
import json, math, pathlib, sys, time

def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)

def normalize(data):
    rates=data.get('rate_limits', {}) if isinstance(data,dict) else {}
    if not isinstance(rates,dict): return {}
    clean={}
    for key in ('five_hour','seven_day','spend_limit'):
        value=rates.get(key)
        if not isinstance(value,dict): continue
        used=value.get('used_percentage');reset=value.get('resets_at')
        if not number(used) or not 0<=used<=100: continue
        if reset is not None and not number(reset): continue
        clean[key]={'used_percentage':used,'resets_at':reset}
    return clean

def main():
    root=pathlib.Path(__file__).resolve().parent/'data'
    try:
        rates=normalize(json.load(sys.stdin))
        root.mkdir(exist_ok=True)
        target=root/'claude-feed.json';tmp=root/f'claude-feed-{time.time_ns()}.tmp'
        tmp.write_text(json.dumps({'at':time.time(),'rate_limits':rates}),encoding='utf-8');tmp.replace(target)
        parts=[f'{label}: {100-rates[key]["used_percentage"]:.0f}% left' for key,label in [('five_hour','5h'),('seven_day','7d')] if key in rates]
        print('Prism | '+(' · '.join(parts) or 'Waiting for quota'))
    except Exception:
        print('Prism | quota unavailable')

if __name__=='__main__': main()
