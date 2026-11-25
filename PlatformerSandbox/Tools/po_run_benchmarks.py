"""Run PO01–PO04 A/B captures in fresh DesktopGL processes; no UI automation."""
import argparse
import csv
import json
import math
import os
from pathlib import Path
import statistics
import subprocess
import sys
import time
from datetime import datetime
import zipfile

FLAGS = ('SNAPSHOTS', 'HUD_ATLAS', 'ARTWORK', 'DIAGNOSTICS', 'LIGHT_VALIDATION',
         'LIGHT_BUFFERS', 'LIGHT_PARAMETERS', 'LIGHT_SAMPLES')
BITS = ('frame_geo', 'layer_fields', 'sample_culling', 'persistent_geo',
        'hard_masks', 'shared_masks', 'persistent_masks')
META = ('PO snapshots', 'PO HUD atlas', 'PO artwork', 'PO diagnostics',
        'PO light validation', 'PO light buffers', 'PO light parameters', 'PO light samples')

def case(name, profile='candidate', vsync=0, cap=0, mask=127, isolation=0, disable=None):
    return dict(name=name, profile=profile, vsync=vsync, cap=cap, mask=mask,
                isolation=isolation, flags={f: int(profile == 'candidate' and f != disable) for f in FLAGS})

def plan(extra=False, single_on=False):
    cases = [case(f'{p}_{pace}', p, v, cap) for p in ('reference','candidate')
             for pace,v,cap in (('uncapped',0,0),('vsync',1,0),('cap60',0,60))]
    cases += [case('without_'+f.lower(), disable=f) for f in FLAGS]
    cases += [case('without_'+name, mask=127 ^ (1<<i)) for i,name in enumerate(BITS)]
    cases += [case('legacy_all_off', mask=0), case('lighting_off_uncapped', isolation=1),
              case('lighting_off_vsync', vsync=1, isolation=1)]
    if extra:
        cases += [case('isolation_'+name, isolation=i) for i,name in enumerate(
            ('full','lighting_off','shadows_off','bloom_off','particles_off','single_sample','dev_hud_off','no_light_vfx','no_world_draw')) if i>=2]
    if single_on:
        cases += [case('legacy_only_'+name, mask=1<<i) for i,name in enumerate(BITS)]
    return cases

def environment(c, dest):
    env = {k:v for k,v in os.environ.items() if not k.upper().startswith('PS_PO_')}
    env.update({'PS_PO_'+k:str(v) for k,v in c['flags'].items()})
    env.update(PS_PO_AUTO_OUTPUT=str(dest), PS_PO_AUTO_CASE=c['name'],
               PS_PO_AUTO_MASK=str(c['mask']), PS_PO_AUTO_ISOLATION=str(c['isolation']),
               PS_PO_VSYNC=str(c['vsync']), PS_PO_RENDER_CAP=str(c['cap']),
               PS_PO_VARIABLE_LOOP='1', PS_PO_HUD='1', PS_PO_LIGHT_MASK='0')
    return env

def perc(v,p): return sorted(v)[max(0,math.ceil(len(v)*p)-1)]

def read_result(c, dest):
    files=list(dest.glob('*.csv')); metadata=list(dest.glob('*-metadata.txt'))
    if len(files)!=1 or len(metadata)!=1:
        raise ValueError(f'expected one complete capture and metadata, found {len(files)} and {len(metadata)}')
    m=dict(line.split(': ',1) for line in metadata[0].read_text(encoding='utf-8-sig').splitlines() if ': ' in line)
    expected={'Build':'Release','Debugger':'False','Scene':'LIGHTING_PERFORMANCE_LAB',
              'Lighting quality':'High','Lighting preset':'Phase21LabNeutral','Lab mode':'5',
              'Isolation':str(c['isolation']),'Motion frozen':'True','Overlay':'True',
              'Fixed timestep':'False','Requested VSync':str(bool(c['vsync'])),
              'Render cap':str(c['cap']),'Automatic case':c['name']}
    expected.update({k:str(bool(c['flags'][f])) for f,k in zip(FLAGS,META)})
    for k,v in expected.items():
        if m.get(k)!=v: raise ValueError(f'metadata {k}: expected {v}, got {m.get(k)}')
    with files[0].open(newline='',encoding='utf-8-sig') as f: rows=list(csv.DictReader(f))
    if len(rows)!=300: raise ValueError(f'expected 300 recorded frames, got {len(rows)}')
    for k,v in {'width':1280,'height':720,'fixed_step':0,'vsync':c['vsync'],
                'optimization_mask':c['mask'],'isolation':c['isolation'],'freeze_lights':1}.items():
        if {r[k] for r in rows}!={str(v)}: raise ValueError(f'wrong/mixed {k}')
    if [int(r['frame']) for r in rows]!=list(range(1,301)): raise ValueError('missing/duplicate frame index')
    def col(k):
        a=[float(r[k]) for r in rows]
        if not all(math.isfinite(v) and v>=0 for v in a): raise ValueError('invalid '+k)
        return a
    frame=col('preceding_frame_ms')
    if min(frame)<=0: raise ValueError('nonpositive frame interval')
    lights=col('logical_lights')
    if c['isolation']==0 and not any(lights): raise ValueError('lighting not active')
    if c['isolation'] in (1,7,8) and any(lights): raise ValueError('unexpected lighting work in disabled isolation')
    result=dict(name=c['name'], status='ok', profile=c['profile'], vsync=c['vsync'], cap=c['cap'], mask=c['mask'], isolation=c['isolation'],
                frames=len(frame), mean_ms=statistics.mean(frame), p50_ms=perc(frame,.5), p95_ms=perc(frame,.95), p99_ms=perc(frame,.99), max_ms=max(frame), fps=1000/statistics.mean(frame),
                over_60fps_budget=sum(x>1000/60 for x in frame))
    for k in ('update_cpu_ms','previous_draw_cpu_ms','present_wall_ms','simulation_cpu_ms','diagnostic_cpu_ms','cpu_submit_ms','material_geometry_cpu_ms','layer_geometry_cpu_ms','debug_hud_cpu_ms','scene_draw_cpu_ms','thread_alloc_bytes','draw_calls','target_changes','logical_lights','area_sample_evaluations','persistent_mask_hits','shared_mask_fallbacks','soft_filter_pixels'):
        result[k+'_mean']=statistics.mean(col(k))
    for k in ('gc0','gc1','gc2'): result[k+'_total']=sum(col(k))
    return result

def write_summary(out, results):
    fields=list(dict.fromkeys(k for r in results for k in r))
    with (out/'summary.csv').open('w',newline='',encoding='utf-8') as f:
        w=csv.DictWriter(f,fieldnames=fields); w.writeheader(); w.writerows(results)
    ok=[r for r in results if r['status']=='ok']
    groups={}
    for r in ok: groups.setdefault(r['name'],[]).append(r)
    baseline=groups.get('candidate_uncapped',[])
    if not baseline: return
    b=statistics.median(r['mean_ms'] for r in baseline)
    with (out/'ablation_summary.csv').open('w',newline='',encoding='utf-8') as f:
        w=csv.writer(f); w.writerow(['case','successful_runs','median_mean_ms','median_p95_ms','median_fps','delta_ms_vs_candidate_uncapped'])
        for name,rs in groups.items():
            t=statistics.median(r['mean_ms'] for r in rs)
            w.writerow([name,len(rs),round(t,4),round(statistics.median(r['p95_ms'] for r in rs),4),round(statistics.median(r['fps'] for r in rs),3),round(t-b,4)])

def archive(out):
    dest=out.with_suffix('.zip')
    with zipfile.ZipFile(dest,'w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(out.rglob('*')):
            if p.is_file(): z.write(p,p.relative_to(out.parent))
    return dest

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--repeat',type=int,default=1,choices=range(1,6))
    ap.add_argument('--isolations',action='store_true',help='include seven additional quality/workload isolation cases')
    ap.add_argument('--single-on',action='store_true',help='also test each original lighting flag alone')
    ap.add_argument('--smoke',action='store_true',help='only capture candidate uncapped and candidate vsync')
    ap.add_argument('--no-build',action='store_true',help='use existing Release binary (must contain automation patch)')
    ap.add_argument('--timeout',type=int,default=240,help='per-case timeout in seconds')
    ap.add_argument('--list',action='store_true',help='print plan without building or running')
    a=ap.parse_args()
    if a.timeout<30: ap.error('timeout must be >=30 seconds')
    cases=plan(a.isolations,a.single_on)
    if a.smoke: cases=[c for c in cases if c['name'] in ('candidate_uncapped','candidate_vsync')]
    if a.list:
        print(json.dumps(cases,indent=2)); return 0
    root=Path(__file__).resolve().parent.parent
    project=root/'Platforms'/'DesktopGL'/'PlatformerSandbox.DesktopGL.csproj'
    if not project.exists(): ap.error('DesktopGL project not found; extract into the solution root')
    out=root/'Benchmarks'/('PO_auto_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    out.mkdir(parents=True)
    (out/'plan.json').write_text(json.dumps(dict(repeat=a.repeat,cases=cases),indent=2),encoding='utf-8')
    if not a.no_build:
        print('Building Release...',flush=True)
        with (out/'build.log').open('w',encoding='utf-8') as log:
            rc=subprocess.call(['dotnet','build',str(project),'-c','Release'],cwd=root,stdout=log,stderr=subprocess.STDOUT)
        if rc:
            print('Build failed. See',out/'build.log'); print('Upload:',archive(out)); return 1
    results=[]
    print(f'{len(cases)*a.repeat} cases. Leave the game window in focus; do not move the player or change settings.',flush=True)
    try:
        for rep in range(1,a.repeat+1):
            ordered=cases if rep%2 else list(reversed(cases))
            for i,c in enumerate(ordered,1):
                dest=out/f'r{rep:02d}_{i:02d}_{c["name"]}'; dest.mkdir()
                print(f'[{rep}/{a.repeat} {i}/{len(cases)}] {c["name"]}',flush=True)
                cmd=['dotnet','run','--project',str(project),'-c','Release','--no-build','--no-launch-profile','--','--dev-level=LIGHTING_PERFORMANCE_LAB']
                start=time.monotonic()
                try:
                    with (dest/'process.log').open('w',encoding='utf-8') as log:
                        proc=subprocess.Popen(cmd,cwd=root,env=environment(c,dest),stdout=log,stderr=subprocess.STDOUT)
                        try: rc=proc.wait(timeout=a.timeout)
                        except (subprocess.TimeoutExpired,KeyboardInterrupt):
                            # dotnet run launches a child; terminate its entire tree on Windows.
                            if os.name=='nt': subprocess.run(['taskkill','/PID',str(proc.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                            else: proc.kill()
                            proc.wait(); raise
                    if rc: raise ValueError(f'game exit code {rc}')
                    r=read_result(c,dest); print(f'  {r["fps"]:.1f} FPS | P95 {r["p95_ms"]:.2f} ms',flush=True)
                except (ValueError,KeyError,OSError,subprocess.TimeoutExpired) as e:
                    r=dict(name=c['name'],status='failed',error=str(e)); print('  FAILED:',e,flush=True)
                r.update(repeat=rep,case_folder=dest.name,wall_seconds=round(time.monotonic()-start,2)); results.append(r)
                write_summary(out,results)
                if r['status']!='ok':
                    print('Stopping to avoid collecting invalid cases. Upload partial results.',flush=True); return 1
    except KeyboardInterrupt:
        print('Interrupted; retaining completed results.',flush=True); return 130
    finally:
        print('Upload this ZIP:',archive(out),flush=True)
    return 0

if __name__=='__main__': sys.exit(main())
