"""Windows benchmark environment guard; no machine settings are changed."""
import ctypes, os, subprocess, time, json
from pathlib import Path

class PowerStatus(ctypes.Structure):
    _fields_=[("ACLineStatus",ctypes.c_ubyte),("BatteryFlag",ctypes.c_ubyte),("BatteryLifePercent",ctypes.c_ubyte),("SystemStatusFlag",ctypes.c_ubyte),("BatteryLifeTime",ctypes.c_uint32),("BatteryFullLifeTime",ctypes.c_uint32)]

def power_source():
    if os.name!='nt': return 'unknown'
    status=PowerStatus()
    if not ctypes.windll.kernel32.GetSystemPowerStatus(ctypes.byref(status)): return 'unknown'
    return {0:'battery',1:'AC'}.get(status.ACLineStatus,'unknown')

def power_plan():
    if os.name!='nt': return 'unknown'
    try: return subprocess.check_output(['powercfg','/getactivescheme'],text=True,errors='replace',timeout=5).strip()
    except (OSError,subprocess.SubprocessError): return 'unknown'

def wait_checked(proc, timeout, dest, require_ac=False, initial=None, plan=None):
    start=time.monotonic(); initial=power_source() if initial is None else initial; plan="unknown" if plan is None else plan; samples=[]
    try:
        if require_ac and initial!='AC': raise ValueError('Benchmark requires AC power (use --allow-battery for separate battery tests).')
        while True:
            source=power_source(); samples.append({'elapsed_seconds':round(time.monotonic()-start,3),'source':source})
            if source!=initial: raise ValueError('Power source changed during capture')
            if require_ac and source!='AC': raise ValueError('AC power not verified')
            left=timeout-(time.monotonic()-start)
            if left<=0: raise subprocess.TimeoutExpired(proc.args,timeout)
            try: rc=proc.wait(timeout=min(.5,left))
            except subprocess.TimeoutExpired: continue
            source=power_source(); samples.append({"elapsed_seconds":round(time.monotonic()-start,3),"source":source})
            if source!=initial: raise ValueError("Power source changed during capture")
            return rc
    finally:
        (dest/'power.json').write_text(json.dumps({'initial':initial,'active_plan':plan,'samples':samples},indent=2),encoding='utf-8')

def command(project, scene):
    # Match the actual configured apphost on Windows; fail if it cannot be found.
    if os.name=='nt':
        base=Path(project).parent/'bin'/'Release'
        candidates=list(base.glob('*/PlatformerSandbox.exe'))
        if len(candidates)!=1: raise ValueError(f'Expected exactly one Release apphost in {base}; found {candidates}')
        return [str(candidates[0].resolve()),'--dev-level='+scene]
    return ['dotnet','run','--project',str(project),'-c','Release','--no-build','--no-launch-profile','--','--dev-level='+scene]
