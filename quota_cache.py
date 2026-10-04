"""Validate untrusted local cache data before reusing it; invalid data is a cache miss."""
import math
import time


def number(value):
    return isinstance(value,(int,float)) and not isinstance(value,bool) and math.isfinite(value)


def valid_reading(value):
    if not isinstance(value,dict) or not isinstance(value.get('status'),str): return False
    from provider_contract import STATUSES, text
    if value['status'] not in STATUSES or set(value)-{'status','source','detail','at','windows','scope'}:return False
    if any(key in value and not text(value[key],400) for key in ('source','detail')):return False
    if 'windows' not in value: return isinstance(value.get('detail'),str)
    if not number(value.get('at')) or not 0<value['at']<=time.time()+300: return False
    windows=value['windows']
    if not isinstance(windows,list) or not 1<=len(windows)<=32: return False
    identifiers=set()
    for window in windows:
        if not isinstance(window,dict): return False
        if set(window)-{'id','label','remaining','reset','rolling'}:return False
        if not number(window.get('remaining')) or not 0<=window['remaining']<=100: return False
        if not text(window.get('label')): return False
        identifier=window.get('id',window['label'])
        if not text(identifier,160) or identifier in identifiers:return False
        identifiers.add(identifier)
        if 'rolling' in window and not isinstance(window['rolling'],bool):return False
        if window.get('reset') is not None and (not number(window['reset']) or not 0<window['reset']<253402300799): return False
    return True


def valid_cache(value,identity_key,clock_key):
    if not isinstance(value,dict) or not isinstance(value.get(identity_key),str): return False
    if set(value)-{identity_key,clock_key,'reading','error'}:return False
    clock=value.get(clock_key)
    if not number(clock) or not 0<=clock<=time.time()+1200: return False
    if 'error' in value and not isinstance(value['error'],str): return False
    return valid_reading(value['reading']) if 'reading' in value else clock_key=='retryAt'
