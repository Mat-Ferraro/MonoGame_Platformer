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

BFLAGS = ("SHARED_SAMPLES", "HASHED_VISIBILITY", "VISIBLE_LAYERS", "PREPARED_MATERIALS")
BMETA = ("PO B shared samples", "PO B hashed visibility", "PO B visible layers", "PO B prepared materials")
TFLAGS = ('MATERIAL_REGIONS','LIGHT_REGIONS','SHARED_FIELDS','SKIP_CLEARS','CACHE_CAPACITY')
TMETA = ('PO 10 material regions','PO 10 light regions','PO 10 shared fields','PO 10 skip clears','PO 10 cache capacity')
TCOUNTERS = ('light_width','light_height','mask_width','mask_height','light_composite_pixels',
             'mask_raster_bound_pixels','selected_clear_pixels','compatible_field_hits','regional_masks')

def case(name, profile='candidate', vsync=0, cap=0, mask=127, isolation=0, disable=None):
    return dict(name=name, profile=profile, vsync=vsync, cap=cap, mask=mask,
                isolation=isolation, area=0, motion=0, quality="High", wide=0, bflags={f:int(profile == "candidate") for f in BFLAGS}, flags={f: int(profile == 'candidate' and f != disable) for f in FLAGS})

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

def po04b_plan():
    base=case('candidate_uncapped')
    cases=[base]
    old=case('po04b_all_off'); old['bflags']={f:0 for f in BFLAGS}; cases.append(old)
    for flag in BFLAGS:
        c=case('po04b_without_'+flag.lower()); c['bflags'][flag]=0; cases.append(c)
    for n in (1,3,5,7,9):
        c=case(f'area_{n}'); c['area']=n; cases.append(c)
    for quality in ('Low','Balanced'):
        c=case('quality_'+quality.lower()); c['quality']=quality; cases.append(c)
    for name,mask in (('motion_cached',127),('motion_reference',0)):
        c=case(name,mask=mask); c['motion']=1; cases.append(c)
    c=case('resolution_1080p'); c['wide']=1; cases.append(c)
    return cases

def po10a_plan():
    def make(name, light=100, shadow=100, flags=(), motion=0, wide=0, soft=0, isolation=0):
        c=case(name,isolation=isolation)
        c.update(light_scale=light,mask_scale=shadow,tflags={f:int(f in flags) for f in TFLAGS},motion=motion,wide=wide,soft=soft,po10a=True)
        return c
    cases=[make('candidate_uncapped')]
    cases += [make(f'light_scale_{n}',light=n) for n in (75,50,25)]
    cases += [make(f'mask_scale_{n}',shadow=n) for n in (75,50,25)]
    cases += [make('po10_'+f.lower(),flags=(f,)) for f in TFLAGS]
    # Cache capacity alone cannot exceed the full-size byte budget; isolate its value at reduced resolution too.
    cases += [make('mask50_capacity',shadow=50,flags=('CACHE_CAPACITY',)),
              make('combined_full',flags=TFLAGS), make('combined_75',75,75,TFLAGS),
              make('combined_50',50,50,TFLAGS), make('combined_25',25,25,TFLAGS),
              make('motion_reference',motion=1),make('motion_combined_full',flags=TFLAGS,motion=1),
              make('motion_combined_50',50,50,TFLAGS,motion=1),
              make('wide_reference',wide=1),make('wide_combined_50',50,50,TFLAGS,wide=1),
              make('soft_reference',soft=1),make('soft_combined_full',flags=TFLAGS,soft=1),
              make('soft_combined_50',50,50,TFLAGS,soft=1),make('lighting_off_uncapped',isolation=1)]
    return cases

def po10b_plan():
    from copy import deepcopy
    old={c['name']:c for c in po10a_plan()}
    def make(name, source='combined_50', bloom=100, adaptive=0, static=0, stress=0):
        c=deepcopy(old[source]); c.update(name=name,po10b=True,bloom_scale=bloom,adaptive=adaptive,static_fields=static,stress=stress)
        return c
    return [make('po10b_reference'),make('po10b_combined',bloom=50,adaptive=1,static=1),
            make('po10b_bloom50',bloom=50),make('po10b_bloom75',bloom=75),make('po10b_bloom25',bloom=25),
            make('po10b_adaptive',adaptive=1),make('po10b_static',static=1),
            make('po10b_motion_reference','motion_combined_50'),make('po10b_motion_combined','motion_combined_50',50,1,1),
            make('po10b_stress_reference',stress=1),make('po10b_stress_combined',bloom=50,adaptive=1,static=1,stress=1),
            make('po10b_soft_reference','soft_combined_50'),make('po10b_soft_combined','soft_combined_50',50,1,1),
            make('po10b_wide_reference','wide_combined_50'),make('po10b_wide_combined','wide_combined_50',50,1,1),
            make('po10b_full_reference','combined_full'),make('po10b_full_combined','combined_full',50,1,1)]

def po10c_plan():
    from copy import deepcopy
    base=next(c for c in po10b_plan() if c['name']=='po10b_reference')
    def make(name, fixture='normal', lock=0, adaptive=0, static=0, motion=0):
        c=deepcopy(base)
        c.update(name=name,po10c=True,fixture=fixture,lock_camera=lock,adaptive=adaptive,static_fields=static,motion=motion)
        return c
    return [make('cache_follow'),make('cache_locked',lock=1),
            make('adaptive_fixture_reference','adaptive',1),make('adaptive_fixture_enabled','adaptive',1,adaptive=1),
            make('static_fixture_reference','static',1),make('static_fixture_enabled','static',1,static=1),
            make('cache_motion',motion=1),make('cache_motion_locked',lock=1,motion=1)]

def po10d_plan():
    from copy import deepcopy
    old={c['name']:c for c in po10a_plan()}
    base=po10c_plan()[0]
    def make(name, source=None, snap=1, pan=0):
        c=deepcopy(base if source is None else old[source])
        c.update(name=name,po10c=True,po10d=True,fixture='normal',lock_camera=0,camera_snap=snap,camera_pan=pan)
        return c
    return [make('follow_legacy',snap=0),make('follow_snap'),
            make('pan_reference','candidate_uncapped',pan=1),
            make('pan_full','combined_full',pan=1),make('pan_half','combined_50',pan=1)]

def po10f_plan():
    from copy import deepcopy
    base=next(c for c in po10c_plan() if c['name']=='static_fixture_reference')
    def make(name, hard=0, buffers=1, pan=0, soft=0):
        c=deepcopy(base)
        c.update(name=name,po10f=True,po10d=True,fixture='static_shadow',static_fields=hard,
                 static_shadows=hard,caster_buffers=buffers,camera_snap=1,camera_pan=pan,lock_camera=0 if pan else 1,soft=soft)
        return c
    return [make('hard_field_reference'),make('hard_field_cached',hard=1),
            make('caster_buffers_off',buffers=0),
            make('moving_field_reference',pan=1),make('moving_field_cached',hard=1,pan=1),
            make('soft_field_reference',soft=1),make('soft_field_cached',hard=1,soft=1)]

F_QUICK=('hard_field_reference','hard_field_cached','caster_buffers_off','moving_field_reference','moving_field_cached')
C_QUICK = ('cache_follow','cache_locked','adaptive_fixture_reference','adaptive_fixture_enabled','static_fixture_reference','static_fixture_enabled')
B_QUICK = ('po10b_reference','po10b_combined','po10b_bloom50','po10b_adaptive','po10b_static','po10b_stress_combined')
B_STANDARD = B_QUICK + ('po10b_stress_reference','po10b_motion_reference','po10b_motion_combined','po10b_soft_reference','po10b_soft_combined','po10b_wide_combined')
QUICK_CASES = ('candidate_uncapped','combined_full','combined_50','combined_25',
               'motion_combined_50','soft_combined_50')
STANDARD_CASES = QUICK_CASES + ('combined_75','light_scale_50','mask_scale_50',
                               'po10_shared_fields','wide_combined_50','lighting_off_uncapped')
TIER_FRAMES = {'quick': (60,120), 'standard': (90,180), 'full': (120,300)}

def environment(c, dest):
    env = {k:v for k,v in os.environ.items() if not k.upper().startswith('PS_PO_')}
    env.update({'PS_PO_'+k:str(v) for k,v in c['flags'].items()})
    env.update({'PS_PO_B_'+k:str(v) for k,v in c['bflags'].items()})
    env.update({'PS_PO_10_'+k:str(v) for k,v in c.get('tflags',{}).items()})
    env.update(PS_PO_10B_BLOOM_SCALE=str(c.get('bloom_scale',100)),PS_PO_10B_ADAPTIVE=str(c.get('adaptive',0)),
               PS_PO_10B_STATIC_FIELDS=str(c.get('static_fields',0)),PS_PO_10B_STRESS=str(c.get('stress',0)))
    env.update(PS_PO_10_LIGHT_SCALE=str(c.get('light_scale',100)),PS_PO_10_MASK_SCALE=str(c.get('mask_scale',100)),PS_PO_10_SOFT=str(c.get('soft',0)))
    env.update(PS_PO_B_AREA_SAMPLES=str(c['area']),PS_PO_B_MOTION=str(c['motion']),PS_PO_B_QUALITY=c['quality'],PS_PO_B_1080P=str(c['wide']))
    env.update(PS_PO_AUTO_OUTPUT=str(dest), PS_PO_AUTO_CASE=c['name'],
               PS_PO_AUTO_MASK=str(c['mask']), PS_PO_AUTO_ISOLATION=str(c['isolation']),
               PS_PO_VSYNC=str(c['vsync']), PS_PO_RENDER_CAP=str(c['cap']),
               PS_PO_VARIABLE_LOOP='1', PS_PO_HUD='1', PS_PO_LIGHT_MASK='0')
    if 'warmup_frames' in c:
        env['PS_PO_AUTO_WARMUP_FRAMES']=str(c['warmup_frames'])
        env['PS_PO_AUTO_RECORDED_FRAMES']=str(c['recorded_frames'])
    if c.get('po10c') or c.get('adaptive_warmup'):
        env.update(PS_PO_10C_FIXTURE=c.get('fixture','normal'),PS_PO_10C_LOCK_CAMERA=str(c.get('lock_camera',0)),
                   PS_PO_AUTO_ADAPTIVE_WARMUP=str(int(c.get('adaptive_warmup',False))),
                   PS_PO_AUTO_MAX_WARMUP=str(c.get('max_warmup_frames',c.get('warmup_frames',120))))
    env.update(PS_PO_10F_STATIC_SHADOWS=str(c.get('static_shadows',0)),PS_PO_10F_CASTER_BUFFERS=str(c.get('caster_buffers',1)))
    if c.get('po10d'):
        env.update(PS_PO_10D_CAMERA_SNAP=str(c['camera_snap']),PS_PO_10D_PAN_CAMERA=str(c['camera_pan']))
    return env

def perc(v,p): return sorted(v)[max(0,math.ceil(len(v)*p)-1)]

def read_result(c, dest):
    files=list(dest.glob('*.csv')); metadata=list(dest.glob('*-metadata.txt'))
    if len(files)!=1 or len(metadata)!=1:
        raise ValueError(f'expected one complete capture and metadata, found {len(files)} and {len(metadata)}')
    m=dict(line.split(': ',1) for line in metadata[0].read_text(encoding='utf-8-sig').splitlines() if ': ' in line)
    expected={'Build':'Release','Debugger':'False','Scene':'LIGHTING_PERFORMANCE_LAB',
              'Lighting quality':c['quality'],'Lighting preset':'Phase21LabNeutral','Lab mode':'6' if c.get('stress') else '5',
              'Isolation':str(c['isolation']),'Motion frozen':str(not c['motion']),'Overlay':'True',
              'Fixed timestep':'False','Requested VSync':str(bool(c['vsync'])),
              'Render cap':str(c['cap']),'Automatic case':c['name']}
    expected.update({k:str(bool(c['flags'][f])) for f,k in zip(FLAGS,META)})
    expected.update({k:str(bool(c['bflags'][f])) for f,k in zip(BFLAGS,BMETA)})
    expected['PO B area samples']=str(c['area'])
    if c.get('po10a'):
        expected.update({'PO 10 light scale':str(c['light_scale']),'PO 10 mask scale':str(c['mask_scale']),
                         'PO 10 shadow edge':'Soft8Px' if c['soft'] else 'Hard'})
        expected.update({k:str(bool(c['tflags'][f])) for f,k in zip(TFLAGS,TMETA)})
    if c.get('po10b'):
        expected.update({'PO 10B bloom scale':str(c['bloom_scale']), 'PO 10B adaptive':str(bool(c['adaptive'])),
                         'PO 10B static fields':str(bool(c['static_fields']))})
    if 'warmup_frames' in c:
        expected['Recorded frames']=str(c['recorded_frames'])
        if not c.get('adaptive_warmup'): expected['Warmup frames']=str(c['warmup_frames'])
    actual_warmup=c.get('warmup_frames',120)
    if c.get('po10c') or c.get('adaptive_warmup'):
        expected.update({'Warmup minimum':str(actual_warmup),'Warmup maximum':str(c['max_warmup_frames']),
                         'Fixture':c.get('fixture','normal'),'Camera locked':str(bool(c.get('lock_camera',0)))})
        actual_warmup=int(m['Warmup frames'])
        status=m.get('Warmup status')
        if c.get('adaptive_warmup'):
            if not c['warmup_frames']<=actual_warmup<=c['max_warmup_frames'] or status not in ('stable','max_reached'):
                raise ValueError('invalid adaptive warmup metadata')
            if status=='max_reached' and actual_warmup!=c['max_warmup_frames']: raise ValueError('early maximum warmup')
        elif status!='fixed': raise ValueError('expected fixed warmup')
        with (dest/'warmup_trace.tsv').open(newline='',encoding='utf-8-sig') as trace:
            warm_rows=list(csv.DictReader(trace,delimiter='\t'))
        if [int(r['frame']) for r in warm_rows]!=list(range(1,actual_warmup+1)): raise ValueError('invalid warmup trace')
        if status=='stable' and (len(warm_rows)<12 or any(r['ready']!='1' for r in warm_rows[-12:])): raise ValueError('invalid stable warmup streak')
    if c.get('po10d'):
        expected.update({'Camera pan':str(bool(c['camera_pan'])), 'Camera convergence':str(bool(c['camera_snap']))})
    if c.get('po10f'):
        expected.update({'PO 10F static shadows':str(bool(c['static_shadows'])), 'PO 10F caster buffers':str(bool(c['caster_buffers']))})
    if c.get('fixture','normal') != 'normal': expected['Fixture coordinates']='world_fixed_v1'
    for k,v in expected.items():
        if m.get(k)!=v: raise ValueError(f'metadata {k}: expected {v}, got {m.get(k)}')
    with files[0].open(newline='',encoding='utf-8-sig') as f: rows=list(csv.DictReader(f))
    recorded=c.get('recorded_frames',300)
    if len(rows)!=recorded: raise ValueError(f'expected {recorded} recorded frames, got {len(rows)}')
    for k,v in {'width':1920 if c['wide'] else 1280,'height':1080 if c['wide'] else 720,'fixed_step':0,'vsync':c['vsync'],
                'optimization_mask':c['mask'],'isolation':c['isolation'],'freeze_lights':1-c['motion']}.items():
        if {r[k] for r in rows}!={str(v)}: raise ValueError(f'wrong/mixed {k}')
    if [int(r['frame']) for r in rows]!=list(range(1,recorded+1)): raise ValueError('missing/duplicate frame index')
    def col(k):
        a=[float(r[k]) for r in rows]
        if not all(math.isfinite(v) and v>=0 for v in a): raise ValueError('invalid '+k)
        return a
    frame=col('preceding_frame_ms')
    if min(frame)<=0: raise ValueError('nonpositive frame interval')
    lights=col('logical_lights')
    if c['isolation']==0 and not any(lights): raise ValueError('lighting not active')
    if c['isolation'] in (1,7,8) and any(lights): raise ValueError('unexpected lighting work in disabled isolation')
    if c.get('po10a'):
        width,height=(1920,1080) if c['wide'] else (1280,720)
        for key,dim,scale in (('light_width',width,c['light_scale']),('light_height',height,c['light_scale']),
                              ('mask_width',width,c['mask_scale']),('mask_height',height,c['mask_scale'])):
            wanted=0 if c['isolation'] in (1,7,8) else (dim*scale+99)//100
            if set(col(key))!={float(wanted)}: raise ValueError('wrong actual '+key)
    result=dict(name=c['name'], status='ok', profile=c['profile'], vsync=c['vsync'], cap=c['cap'], mask=c['mask'], isolation=c['isolation'],
                frames=len(frame), warmup_frames=actual_warmup, tier=c.get('tier','full'), mean_ms=statistics.mean(frame), p50_ms=perc(frame,.5), p95_ms=perc(frame,.95), p99_ms=perc(frame,.99), max_ms=max(frame), fps=1000/statistics.mean(frame),
                over_60fps_budget=sum(x>1000/60 for x in frame),
                gpu_adapter=m.get('GPU adapter',''), os_version=m.get('OS',''),
                logical_processors=m.get('Logical processors',''), runtime=m.get('Runtime',''))
    for k in ('update_cpu_ms','previous_draw_cpu_ms','present_wall_ms','simulation_cpu_ms','diagnostic_cpu_ms','cpu_submit_ms','material_geometry_cpu_ms','layer_geometry_cpu_ms','debug_hud_cpu_ms','scene_draw_cpu_ms','thread_alloc_bytes','draw_calls','target_changes','logical_lights','area_sample_evaluations','persistent_mask_hits','shared_mask_fallbacks','mask_fallback_capacity','mask_fallback_current_frame','soft_filter_pixels'):
        result[k+'_mean']=statistics.mean(col(k))
    if c.get('po10a'):
        result.update(light_scale=c['light_scale'],mask_scale=c['mask_scale'],soft=c['soft'],motion=c['motion'],wide=c['wide'])
        for k in TCOUNTERS+('estimated_target_mb','soft_filter_passes','shared_mask_targets','layer_builds','layer_reuses'):
            result[k+'_mean']=statistics.mean(col(k))
    if c.get('po10b'):
        result.update(bloom_scale=c['bloom_scale'],adaptive=c['adaptive'],static_fields=c['static_fields'],stress=c['stress'])
        for k in ('static_field_hits','bloom_pixels','bloom_passes'):
            result[k+'_mean']=statistics.mean(col(k))
        if result['bloom_passes_mean']<=0: raise ValueError('bloom not active in PO10B fixture')
    if c.get('po10c') or c.get('adaptive_warmup'):
        result.update(warmup_status=m['Warmup status'],fixture=c.get('fixture','normal'),camera_locked=c.get('lock_camera',0),
                      warmup_min=c['warmup_frames'],warmup_max=c['max_warmup_frames'])
        for k in ('view_changed','lights_changed','mask_invalidated','mask_evictions','player_changed','logical_area_samples'):
            result[k+'_mean']=statistics.mean(col(k))
        if c.get('po10c') and c.get('fixture')=='adaptive':
            expected_counts='1|3|5|7|9' if c['adaptive'] else '9|9|9|9|9'
            if {r['area_sample_counts'] for r in rows}!={expected_counts}: raise ValueError('adaptive fixture did not exercise expected sample counts')
            if set(col('logical_area_samples'))!={25.0 if c['adaptive'] else 45.0}: raise ValueError('wrong adaptive fixture total sample count')
        if c.get('po10c') and c.get('fixture')=='static':
            result['static_fixture_exercised']=bool(result['static_field_hits_mean']>0)
        # Descriptive partition, not a replacement for the complete capture.
        steady=[t for t,r in zip(frame,rows) if r['view_changed']=='0' and r['lights_changed']=='0' and r['mask_invalidated']=='0' and r['shared_mask_misses']=='0']
        result['steady_frames']=len(steady)
        result['steady_mean_ms']=statistics.mean(steady) if steady else None
    if c.get('po10d'):
        result.update(camera_pan=c['camera_pan'],camera_snap=c['camera_snap'],
                      world_geometry_hits_mean=statistics.mean(col('persistent_hits')),
                      geometry_builds_mean=statistics.mean(col('geometry_builds')))
        if c['camera_pan'] and not any(col('view_changed')): raise ValueError('camera pan did not change the view')
    if c.get('po10f'):
        validate_world_fixed_fixture(c,rows)
        hits=col('static_shadow_field_hits')
        result.update(static_shadow_field_hits_mean=statistics.mean(hits),caster_buffers=c['caster_buffers'],static_shadows=c['static_shadows'])
        if not c['static_shadows'] or c['soft']:
            if any(hits): raise ValueError('unexpected hard shadow field cache hits')
        if c['name']=='hard_field_cached' and not any(hits):
            result['static_shadow_fixture_exercised']=False
        elif c['name']=='hard_field_cached': result['static_shadow_fixture_exercised']=True
        if c['name']=='hard_field_reference': result['reference_layer_masks_mean']=statistics.mean(col('layer_masks'))
    for k in ('gc0','gc1','gc2'): result[k+'_total']=sum(col(k))
    return result

def validate_world_fixed_fixture(c, rows):
    """A camera sweep must move only the view, not the fixed light sources."""
    if c.get('fixture') == 'static_shadow' and any(int(r['lights_changed']) for r in rows):
        raise ValueError('static shadow fixture light geometry changed during capture')


def write_summary(out, results):
    fields=list(dict.fromkeys(k for r in results for k in r))
    with (out/'summary.csv').open('w',newline='',encoding='utf-8') as f:
        w=csv.DictWriter(f,fieldnames=fields); w.writeheader(); w.writerows(results)
    ok=[r for r in results if r['status']=='ok']
    groups={}
    for r in ok: groups.setdefault(r['name'],[]).append(r)
    baseline=groups.get('candidate_uncapped',[]) or groups.get('po10b_reference',[])
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

FOCUS_CANCEL = 'PO benchmark incomplete: CANCELLED - WINDOW FOCUS OR SIZE CHANGED'


def retryable_focus_cancel(dest, rc):
    # A clean, explicitly cancelled capture with no output is retryable.
    # Crashes, timeouts, unexpected metadata and partial captures are not.
    if rc != 0 or list(dest.glob('*.csv')) or list(dest.glob('*-metadata.txt')):
        return False
    try:
        lines = (dest/'process.log').read_text(encoding='utf-8-sig', errors='replace').splitlines()
    except OSError:
        return False
    return FOCUS_CANCEL in [line.strip() for line in lines]


def run_attempt(c, dest, root, project, timeout):
    cmd=['dotnet','run','--project',str(project),'-c','Release','--no-build','--no-launch-profile','--','--dev-level=LIGHTING_PERFORMANCE_LAB']
    start=time.monotonic()
    rc=None
    try:
        with (dest/'process.log').open('w',encoding='utf-8') as log:
            proc=subprocess.Popen(cmd,cwd=root,env=environment(c,dest),stdout=log,stderr=subprocess.STDOUT)
            try: rc=proc.wait(timeout=timeout)
            except (subprocess.TimeoutExpired,KeyboardInterrupt):
                if os.name=='nt': subprocess.run(['taskkill','/PID',str(proc.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                else: proc.kill()
                proc.wait(); raise
        if rc: raise ValueError(f'game exit code {rc}')
        r=read_result(c,dest)
    except (ValueError,KeyError,OSError,subprocess.TimeoutExpired) as e:
        retry=retryable_focus_cancel(dest,rc)
        r=dict(name=c['name'],status='failed',error=FOCUS_CANCEL if retry else str(e))
    else:
        retry=False
    r.update(case_folder=dest.name,wall_seconds=round(time.monotonic()-start,2))
    return r,retry


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--no-wide',action='store_true',help='skip 1080p test on small displays')
    suites=ap.add_mutually_exclusive_group()
    suites.add_argument('--po04b',action='store_true',help='targeted PO04B suite')
    suites.add_argument('--po10f',action='store_true',help='hard-shadow field cache and caster allocation A/B suite')
    suites.add_argument('--po10d',action='store_true',help='camera convergence and moving-camera A/B suite')
    suites.add_argument('--po10c',action='store_true',help='cache readiness and dedicated adaptive/static fixture pairs')
    suites.add_argument('--po10b',action='store_true',help='PO10B adaptive/bloom/static/stress experiments')
    suites.add_argument('--po10a',action='store_true',help='targeted renderer resolution/region/cache suite')
    ap.add_argument('--repeat',type=int,default=1,choices=range(1,6))
    ap.add_argument('--tier',choices=tuple(TIER_FRAMES),default='full',help='PO10A quick (6 cases), standard (12), full (26); full preserves original durations')
    ap.add_argument('--warmup-frames',type=int,help='override warmup (30-1200)')
    ap.add_argument('--adaptive-warmup',action='store_true',help='wait for stable frozen-scene counters (default in PO10C)')
    ap.add_argument('--fixed-warmup',action='store_true',help='disable adaptive warmup')
    ap.add_argument('--max-warmup-frames',type=int,help='adaptive warmup limit (default max of 240 and minimum)')
    ap.add_argument('--recorded-frames',type=int,help='override capture length (60-3000)')
    ap.add_argument('--isolations',action='store_true',help='include seven additional quality/workload isolation cases')
    ap.add_argument('--single-on',action='store_true',help='also test each original lighting flag alone')
    ap.add_argument('--smoke',action='store_true',help='quick checks; PO04B checks baseline and the previously interrupted shared-sample case')
    ap.add_argument('--focus-retries',type=int,default=3,choices=range(0,6),help='extra attempts for explicit focus/size cancellations only (default 3)')
    ap.add_argument('--case',action='append',help='run named case(s) from the selected suite; repeat option to select more')
    ap.add_argument('--no-build',action='store_true',help='use existing Release binary (must contain automation patch)')
    ap.add_argument('--timeout',type=int,default=240,help='per-case timeout in seconds')
    ap.add_argument('--list',action='store_true',help='print plan without building or running')
    a=ap.parse_args()
    if a.timeout<30: ap.error('timeout must be >=30 seconds')
    cases=po10f_plan() if a.po10f else po10d_plan() if a.po10d else po10c_plan() if a.po10c else po10b_plan() if a.po10b else po10a_plan() if a.po10a else po04b_plan() if a.po04b else plan(a.isolations,a.single_on)
    if a.tier != 'full' and not (a.po10a or a.po10b or a.po10c or a.po10d or a.po10f): ap.error('quick/standard tiers require --po10a through --po10f (supported suites)')
    if a.tier != 'full' and (a.smoke or a.case): ap.error('use --tier full with --smoke or --case')
    if a.tier != 'full':
        names=(F_QUICK if a.tier=='quick' else tuple(c['name'] for c in cases)) if a.po10f else tuple(c['name'] for c in cases) if a.po10d else (C_QUICK if a.tier=='quick' else tuple(c['name'] for c in cases)) if a.po10c else (B_QUICK if a.tier=='quick' else B_STANDARD) if a.po10b else (QUICK_CASES if a.tier=='quick' else STANDARD_CASES)
        cases=[c for c in cases if c['name'] in names]
    if a.no_wide: cases=[c for c in cases if not c["wide"]]
    if a.smoke and a.case: ap.error('--smoke and --case cannot be combined')
    if a.smoke:
        names=('hard_field_reference','hard_field_cached') if a.po10f else ('follow_snap','pan_half') if a.po10d else ('cache_follow','cache_locked') if a.po10c else ('po10b_reference','po10b_combined') if a.po10b else ('candidate_uncapped','combined_50','soft_combined_50') if a.po10a else ('candidate_uncapped','po04b_without_shared_samples') if a.po04b else ('candidate_uncapped','candidate_vsync')
        cases=[c for c in cases if c['name'] in names]
    if a.case:
        unknown=set(a.case)-{c['name'] for c in cases}
        if unknown: ap.error('unknown case(s): '+', '.join(sorted(unknown)))
        cases=[c for c in cases if c['name'] in a.case]
    warmup,recorded=TIER_FRAMES[a.tier]
    if a.warmup_frames is not None: warmup=a.warmup_frames
    if a.recorded_frames is not None: recorded=a.recorded_frames
    if not 30<=warmup<=1200: ap.error('warmup frames must be 30-1200')
    if not 60<=recorded<=3000: ap.error('recorded frames must be 60-3000')
    if a.adaptive_warmup and a.fixed_warmup: ap.error('choose adaptive or fixed warmup')
    maximum=a.max_warmup_frames if a.max_warmup_frames is not None else max(240,warmup)
    if not warmup<=maximum<=1200: ap.error('max warmup must be between warmup and 1200')
    for c in cases:
        c.update(tier=a.tier,warmup_frames=warmup,recorded_frames=recorded)
        if a.po10c or a.po10d or a.po10f or a.adaptive_warmup:
            adaptive=not a.fixed_warmup and not c['motion'] and not c.get('camera_pan',0)
            c.update(adaptive_warmup=adaptive,max_warmup_frames=maximum if adaptive else warmup)
    if a.list:
        print(json.dumps(cases,indent=2)); return 0
    root=Path(__file__).resolve().parent.parent
    project=root/'Platforms'/'DesktopGL'/'PlatformerSandbox.DesktopGL.csproj'
    if not project.exists(): ap.error('DesktopGL project not found; extract into the solution root')
    out=root/'Benchmarks'/('PO_auto_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    out.mkdir(parents=True)
    (out/'plan.json').write_text(json.dumps(dict(repeat=a.repeat,focus_retries=a.focus_retries,tier=a.tier,warmup_frames=warmup,recorded_frames=recorded,cases=cases),indent=2),encoding='utf-8')
    if not a.no_build:
        print('Building Release...',flush=True)
        with (out/'build.log').open('w',encoding='utf-8') as log:
            rc=subprocess.call(['dotnet','build',str(project),'-c','Release'],cwd=root,stdout=log,stderr=subprocess.STDOUT)
        if rc:
            print('Build failed. See',out/'build.log'); print('Upload:',archive(out)); return 1
    results=[]
    print(f'{len(cases)*a.repeat} cases ({a.tier}; {warmup} warmup + {recorded} measured frames per run). Leave the game window in focus; do not move the player or change settings.',flush=True)
    try:
        for rep in range(1,a.repeat+1):
            ordered=cases if rep%2 else list(reversed(cases))
            for i,c in enumerate(ordered,1):
                stem=f'r{rep:02d}_{i:02d}_{c["name"]}'
                print(f'[{rep}/{a.repeat} {i}/{len(cases)}] {c["name"]}',flush=True)
                for attempt in range(1,a.focus_retries+2):
                    dest=out/(stem if attempt==1 else f'{stem}_attempt{attempt:02d}')
                    dest.mkdir()
                    # Allow the previous window to close and the desktop to settle.
                    time.sleep(2)
                    r,retry=run_attempt(c,dest,root,project,a.timeout)
                    r.update(repeat=rep,attempt=attempt)
                    (dest/'attempt_result.json').write_text(json.dumps(r,indent=2),encoding='utf-8')
                    if r['status']=='ok':
                        suffix=f" | warmup {r['warmup_frames']} ({r['warmup_status']})" if 'warmup_status' in r else ''
                        print(f'  {r["fps"]:.1f} FPS | P95 {r["p95_ms"]:.2f} ms'+suffix,flush=True)
                        if r.get('warmup_status')=='max_reached': print('  WARNING: warmup limit reached; not a validated steady state',flush=True)
                        if r.get('static_shadow_fixture_exercised') is False: print('  WARNING: hard-shadow field cache not exercised; result inconclusive for that optimization',flush=True)
                        if r.get('static_fixture_exercised') is False and c.get('static_fields'): print('  WARNING: static cache not exercised',flush=True)
                        break
                    if retry and attempt<=a.focus_retries:
                        print(f'  Focus/size cancellation; retrying ({attempt}/{a.focus_retries} retries).',flush=True)
                        continue
                    print('  FAILED:',r['error'],flush=True)
                    break
                results.append(r)
                write_summary(out,results)
                if r['status']!='ok':
                    print('Stopping to avoid collecting invalid cases. Upload partial results.',flush=True); return 1
    except KeyboardInterrupt:
        print('Interrupted; retaining completed results.',flush=True); return 130
    finally:
        print('Upload this ZIP:',archive(out),flush=True)
    return 0

if __name__=='__main__': sys.exit(main())
