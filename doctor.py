"""Shareable dependency diagnostics. Never reads credentials, accounts or environment values."""
import importlib.util
import json
import platform
import shutil
import struct
import sys


def report():
    return {
        'python':platform.python_version(),
        'architecture':str(struct.calcsize('P')*8)+'-bit',
        'platform':platform.system(),
        'isolated_environment':sys.prefix!=sys.base_prefix,
        'terminal_dependencies':{name:importlib.util.find_spec(name) is not None for name in ('winpty','pyte','wcwidth')},
        'cli_on_path':{name:bool(shutil.which(name+'.exe') or shutil.which(name)) for name in ('codex','claude')},
    }


if __name__=='__main__':
    print(json.dumps(report(),indent=2))
