"""Run PO01–PO04 A/B captures in fresh DesktopGL processes; no UI automation."""
import argparse
import csv
import json
import math
import os
from pathlib import Path
import statistics
import shutil
import subprocess
import sys
import time
from datetime import datetime
import zipfile
import gpu01_environment as gpuenv

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

GPU_VARIANTS = (('reference',0,0,0),('buffered',1,0,0),('resident',1,1,0),('indexed',1,0,1),('combined',1,1,1))
GPU_QUICK_GROUPS = ('static_half','moving_half')

def gpu01_plan():
    from copy import deepcopy
    old={c['name']:c for c in po10a_plan()}
    cases=[]
    for source,tag in [('combined_50','static_half'),('motion_combined_50','moving_half'),('motion_combined_full','moving_full'),('soft_combined_50','soft_half'),('wide_combined_50','wide_half'),('combined_50','pan_half')]:
        for variant,buffered,resident,indexed in GPU_VARIANTS:
            c=deepcopy(old[source]); c.update(name='gpu_'+tag+'_'+variant,gpu01=True,gpu_group=tag,gpu_variant=variant,gpu_buffers=buffered,gpu_resident=resident,gpu_indexed=indexed)
            # Disable across-frame mask reuse to isolate submission and upload behavior.
            c['mask']=127 & ~64
            if tag=='static_half': c.update(po10c=True,fixture='normal',lock_camera=1)
            if tag=='pan_half': c.update(po10d=True,camera_snap=1,camera_pan=1)
            cases.append(c)
    return cases

GPU_EXTRA_FLAGS=('local_masks','specialized_materials','fused_layer','batch_unshadowed','fast_bloom','instanced_particles')
GPU_EXTRA_COUNTERS=('gpu_local_builds','gpu_local_hits','gpu_local_prior_hits','gpu_local_bypasses','gpu_local_bytes','gpu_specialized_passes','gpu_fused_layers','gpu_direct_lights','gpu_fast_bloom','gpu_particle_instances','gpu_particle_draws','gpu_particle_analytic','gpu_particle_fallbacks','gpu_particle_upload_bytes')
GPU_EXTRA_VISUAL=('GPU Local mask builds','GPU Local mask hits','GPU Local mask prior hits','GPU Local mask bypasses','GPU Local mask bytes','GPU Specialized passes','GPU Fused layers','GPU Direct lights','GPU Fast bloom','GPU Particle instances','GPU Particle draws','GPU Particle analytic','GPU Particle fallbacks','GPU Particle upload bytes')

def gpu_expanded_plan(tier='full'):
    from copy import deepcopy
    old={c['name']:c for c in po10a_plan()}
    cases=[]
    def group(tag,source,variants,**settings):
        for name,flags,required in [('reference',(),()),*variants]:
            c=deepcopy(old[source]);c.update(name='gx_'+tag+'_'+name,gpu_expanded=True,gpu_group=tag,gpu_variant=name,**settings)
            c.update({'gpu_'+f:int(f in flags) for f in GPU_EXTRA_FLAGS})
            c['gpu_motion']=int(name=='analytic');c['gpu_required']=list(required)
            if c.get('po10c'): c.update(po10b=True,bloom_scale=100)
            if name=='combined': c.update(gpu_buffers=1,gpu_resident=1,gpu_indexed=1)
            cases.append(c)
    material=[('specialized',('specialized_materials',),('gpu_specialized_passes',)),('fused',('fused_layer',),('gpu_fused_layers',)),('combined',('local_masks','specialized_materials','fused_layer','batch_unshadowed','instanced_particles'),('gpu_specialized_passes','gpu_fused_layers')),('fast_bloom',('fast_bloom',),('gpu_fast_bloom',))]
    group('material','motion_combined_full',material)
    group('local_pan','combined_full',[('local',('local_masks',),('gpu_local_hits','gpu_local_prior_hits'))],po10c=True,po10d=True,fixture='static_shadow',lock_camera=0,camera_snap=1,camera_pan=1,adaptive=0,static_fields=0,stress=0)
    group('direct','combined_full',[('batch',('batch_unshadowed',),('gpu_direct_lights',))],po10c=True,fixture='static',lock_camera=1,adaptive=0,static_fields=0,stress=0)
    particles=[('instanced',('instanced_particles',),('gpu_particle_instances','gpu_particle_draws')),('analytic',('instanced_particles',),('gpu_particle_analytic',))]
    group('particles_1000','combined_50',particles,gpu_stress=1000)
    if tier!='quick':
        group('particles_5000','combined_50',particles,gpu_stress=5000)
        group('soft','soft_combined_50',material)
    if tier=='full':
        group('particles_10000','combined_50',particles,gpu_stress=10000)
        group('wide','wide_reference',material)
    return cases

def gpu_compare_plan():
    """Five manual GPU settings, with production-quality controls held constant.
    Keep established optimizations ON, including cross-frame mask reuse.
    This is a representative Performance Lab comparison, not feature isolation.
    """
    from copy import deepcopy
    base=next(c for c in po10a_plan() if c['name']=='motion_combined_full')
    shaders=('specialized_materials','fused_layer','batch_unshadowed')
    full=shaders+('local_masks','instanced_particles')
    variants=(('reference',0,0,0,()),('all',1,1,1,full),
              ('no_indexed',1,1,0,full),('shaders',0,0,0,shaders),
              ('shaders_local',0,0,0,shaders+('local_masks',)))
    cases=[]
    for name,buffered,resident,indexed,flags in variants:
        c=deepcopy(base)
        c.update(name='gx_compare_'+name,gpu_group='compare',gpu_variant=name,
                 gpu_compare=True,gpu_expanded=True,po10b=True,po10f=True,
                 bloom_scale=100,adaptive=0,static_fields=1,static_shadows=1,
                 caster_buffers=1,stress=0,gpu_stress=0,gpu_motion=0,
                 gpu_buffers=buffered,gpu_resident=resident,gpu_indexed=indexed,
                 gpu_required=['gpu_specialized_passes','gpu_fused_layers'] if flags else [])
        c.update({'gpu_'+f:int(f in flags) for f in GPU_EXTRA_FLAGS})
        cases.append(c)
    return cases


def validate_pb_tick_window(c, metadata):
    start=int(metadata['PB measurement start tick']); end=int(metadata['PB measurement end tick'])
    if metadata.get('PB tick benchmark')!='True' or end-start!=c['pb_recorded_ticks']:
        raise ValueError('wrong PB measurement tick span')
    if start!=120+c['pb_warmup_ticks']:
        raise ValueError('wrong PB absolute start tick')
    if start-int(metadata['PB warmup start tick'])<c['pb_warmup_ticks']:
        raise ValueError('short PB warmup')
    if c.get('pb_platform_script'):
        if metadata.get('PB workload version')!='7': raise ValueError('wrong PB workload version')
        if min(int(metadata[k]) for k in ('PB measured platform commands','PB measured deaths','PB measured respawns'))<=0:
            raise ValueError('platform or lifecycle workload not exercised')
        if int(metadata.get('PB prop pair impacts','0'))<=0: raise ValueError('prop-to-prop impact workload not exercised')
        if int(metadata.get('PB platform prop contacts','0'))<=0: raise ValueError('platform-to-prop contact workload not exercised')
        if int(metadata.get('PB committed coupled event iterations','0'))<=0: raise ValueError('coupled event workload not exercised')
        if int(metadata['PB platform recovery attempts'])<0: raise ValueError('invalid platform recovery counter')
    return end-start


def pb01_baseline_plan(tier='quick'):
    from copy import deepcopy
    source=next(c for c in po10a_plan() if c['name']=='motion_combined_50')
    cases=[]
    for mode in ('Reference','Indexed'):
        for vsync in ((0,) if tier=='quick' else (0,1)):
            c=deepcopy(source); c.update(name=f'pb01_{mode.lower()}_'+('vsync' if vsync else 'uncapped'),scene='ENGINE_INTEGRATION_LAB',spatial_mode=mode,vsync=vsync,pb01_baseline=True,pb_tick_benchmark=True,pb_platform_script=True,pb_warmup_ticks=60 if tier=="quick" else 300,pb_recorded_ticks=300 if tier=="quick" else 1800 if tier=="full" else 900)
            cases.append(c)
    return cases


def gpu_refine_plan(tier='quick'):
    from copy import deepcopy
    base=gpu_compare_plan()
    cases=[]
    for scene in ('LIGHTING_PERFORMANCE_LAB','LIGHTING_QUALITY_LAB'):
        for original in base:
            c=deepcopy(original); c['scene']=scene
            if scene=='LIGHTING_QUALITY_LAB': c['name']='gxr_quality_'+c['gpu_variant']
            cases.append(c)
    for flag in ('scoped_binds','material_clear','both'):
        c=deepcopy(base[3]); c.update(name='gxr_'+flag,gpu_compare=False,gpu_scoped_binds=int(flag!='material_clear'),gpu_material_clear=int(flag!='scoped_binds')); cases.append(c)
    if tier!='quick':
        for old in gpu_expanded_plan('quick'):
            if old['gpu_group'] in ('local_pan','direct'):
                c=deepcopy(old); c['name']='gxr_'+old['name']; cases.append(c)
        for old in gpu01_plan():
            if old['gpu_group'] in ('static_half','moving_half') and old['gpu_variant'] in ('reference','buffered','indexed'):
                c=deepcopy(old); c['name']='gxr_'+old['name']; cases.append(c)
        # Separate workloads isolate draw/clear pressure; keep old references intact.
        for flag in ('LIGHT_REGIONS','MATERIAL_REGIONS','SKIP_CLEARS','SHARED_FIELDS'):
            c=deepcopy(base[3]); c.update(name='gxr_without_'+flag.lower(),gpu_compare=False)
            c['tflags'][flag]=0; cases.append(c)
        for count in (5000,10000,25000,50000) if tier=='full' else (5000,10000):
            for mode in ('reference','instanced','analytic'):
                c=deepcopy(base[0]); c.update(name=f'gxr_particles_{count}_{mode}',gpu_compare=False,po10b=False,gpu_group=f'isolated_particles_{count}',gpu_variant=mode,isolation=8,gpu_stress=count,gpu_instanced_particles=int(mode!='reference'),gpu_motion=int(mode=='analytic'),gpu_required=['gpu_particle_analytic' if mode=='analytic' else 'gpu_particle_instances'] if mode!='reference' else [])
                cases.append(c)
    return cases


def validate_adapter(expected, reported, native='', strict=False):
    if strict and (not native or native.upper().startswith(('NOT PROBED','UNKNOWN','UNAVAILABLE'))):
        raise ValueError('Native OpenGL renderer not verified')
    if not expected: return
    if expected.casefold() not in reported.casefold():
        raise ValueError(f'GPU adapter mismatch: wanted {expected!r}, got {reported!r}')
    if native and not native.upper().startswith(('NOT PROBED','UNKNOWN','UNAVAILABLE')) and expected.casefold() not in native.casefold():
        raise ValueError(f'Native GPU renderer mismatch: wanted {expected!r}, got {native!r}')


def compare_submission(c, counts):
    """Require actual buffer/index work, reject fallbacks; cache hits are workload dependent."""
    n=lambda key: counts.get(key,0)
    if n('gpu_fallback_draws'): raise ValueError('GPU submission fell back')
    if c['gpu_buffers']:
        if not n('gpu_buffered_draws') or n('gpu_reference_draws'): raise ValueError('buffered submission not exercised or mixed')
    elif n('gpu_buffered_draws') or not n('gpu_reference_draws'):
        raise ValueError('compatibility submission not exercised or mixed')
    if c['gpu_indexed'] and not n('gpu_indexed_draws'): raise ValueError('indexed submission not exercised')
    if not c['gpu_indexed'] and (n('gpu_indexed_draws') or n('gpu_index_bypasses')): raise ValueError('unexpected indexed submission')
    if c['gpu_resident'] and not (n('gpu_resident_hits') or n('gpu_resident_misses')): raise ValueError('resident cache not exercised')
    if not c['gpu_resident'] and any(n(k) for k in ('gpu_resident_hits','gpu_resident_misses','gpu_resident_bypasses')): raise ValueError('unexpected resident work')


def gpu_extra_metadata(c):
    return {'GPU '+f:str(bool(c.get('gpu_'+f,0))) for f in GPU_EXTRA_FLAGS} | {'GPU particle stress':str(c.get('gpu_stress',0)),'GPU particle motion':str(bool(c.get('gpu_motion',0)))}

def validate_gpu_expanded(c,counts):
    for key in c['gpu_required']:
        if counts.get(key,0)<=0: raise ValueError('GPU feature not exercised: '+key)
    for flag,keys in [('local_masks',('gpu_local_builds','gpu_local_hits','gpu_local_prior_hits')),('specialized_materials',('gpu_specialized_passes',)),('fused_layer',('gpu_fused_layers',)),('batch_unshadowed',('gpu_direct_lights',)),('fast_bloom',('gpu_fast_bloom',)),('instanced_particles',('gpu_particle_draws','gpu_particle_instances','gpu_particle_analytic'))]:
        if not c.get('gpu_'+flag) and any(counts.get(k,0) for k in keys):
            # Local masks select the specialized pair shader by design.
            if flag=='specialized_materials' and c.get('gpu_local_masks'): continue
            raise ValueError('unexpected GPU feature: '+flag)
    if c.get('gpu_instanced_particles') and counts.get('gpu_particle_fallbacks',0): raise ValueError('particle path fell back')
    if not c.get('gpu_motion') and counts.get('gpu_particle_analytic',0): raise ValueError('unexpected analytic motion')

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
    env = {k:v for k,v in os.environ.items() if not k.upper().startswith(('PS_PO_','PS_PB01_'))}
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
    env["PS_PO_GPU_SHADOW_BUFFERS"]=str(c.get("gpu_buffers",0))
    env["PS_PO_GPU_SHADOW_RESIDENT"]=str(c.get("gpu_resident",0))
    env["PS_PO_GPU_SHADOW_INDEXED"]=str(c.get("gpu_indexed",0))
    for f in GPU_EXTRA_FLAGS: env["PS_PO_GPU_"+f.upper()]=str(c.get("gpu_"+f,0))
    env["PS_PO_GPU_PARTICLE_STRESS"]=str(c.get("gpu_stress",0))
    env["PS_PO_GPU_PARTICLE_MOTION"]=str(c.get("gpu_motion",0))
    for f in ('scoped_binds','material_clear'): env['PS_PO_GPU_'+f.upper()]=str(c.get('gpu_'+f,0))
    env["PS_PO_GPU_TIMING"]=str(int(c.get("gpu_timing",False)))
    env["PS_PO05_MODE"]=c.get("spatial_mode","Reference")
    if c.get("pb_platform_script"): env["PS_PB01_PLATFORM_SCRIPT"]="1"
    if c.get("pb_tick_benchmark"):
        env.update(PS_PB01_TICK_BENCHMARK="1",PS_PB01_WARMUP_TICKS=str(c["pb_warmup_ticks"]),PS_PB01_RECORDED_TICKS=str(c["pb_recorded_ticks"]))
    return env

def perc(v,p): return sorted(v)[max(0,math.ceil(len(v)*p)-1)]

def read_result(c, dest):
    files=list(dest.glob('*.csv')); metadata=list(dest.glob('*-metadata.txt'))
    if len(files)!=1 or len(metadata)!=1:
        raise ValueError(f'expected one complete capture and metadata, found {len(files)} and {len(metadata)}')
    m=dict(line.split(': ',1) for line in metadata[0].read_text(encoding='utf-8-sig').splitlines() if ': ' in line)
    validate_adapter(c.get('expect_adapter'), m.get('GPU adapter',''), m.get('GPU GL renderer',''),strict=(bool(c.get('expect_adapter')) or c.get('require_native',False)) and m.get('GPU backend','DesktopGL')=='DesktopGL')
    expected={'Build':'Release','Debugger':'False','Scene':c.get('scene','LIGHTING_PERFORMANCE_LAB'),
              'Lighting quality':c['quality'],'Lighting preset':'Phase21LabNeutral','Lab mode':'6' if c.get('stress') else '5',
              'Isolation':str(c['isolation']),'Motion frozen':str(not c['motion']),'Overlay':'True',
              'Fixed timestep':'False','Requested VSync':str(bool(c['vsync'])),
              'Render cap':str(c['cap']),'Automatic case':c['name']}
    if c.get('scene') in ('LIGHTING_QUALITY_LAB','ENGINE_INTEGRATION_LAB'): expected['Light coverage']='Preserve'
    if c.get('pb01_baseline'): expected['Spatial mode']=c['spatial_mode']
    for f in ('scoped_binds','material_clear'):
        if c.get('gpu_'+f): expected['GPU '+f]='True'
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
    if 'warmup_frames' in c and not c.get('pb_tick_benchmark'):
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
    if c.get('gpu01'): expected.update({'GPU shadow buffers requested':str(bool(c['gpu_buffers'])), 'GPU resident requested':str(bool(c['gpu_resident'])), 'GPU indexed requested':str(bool(c['gpu_indexed']))})
    if c.get('gpu_expanded'):
        expected.update(gpu_extra_metadata(c))
        expected.update({'GPU shadow buffers requested':str(bool(c.get('gpu_buffers',0))), 'GPU resident requested':str(bool(c.get('gpu_resident',0))), 'GPU indexed requested':str(bool(c.get('gpu_indexed',0)))})
    if c.get('fixture','normal') != 'normal': expected['Fixture coordinates']='world_fixed_v1'
    for k,v in expected.items():
        if m.get(k)!=v: raise ValueError(f'metadata {k}: expected {v}, got {m.get(k)}')
    with files[0].open(newline='',encoding='utf-8-sig') as f: rows=list(csv.DictReader(f))
    if c.get('pb_tick_benchmark'): actual_warmup=int(m['Warmup frames'])
    recorded=int(m['Recorded frames']) if c.get('pb_tick_benchmark') else c.get('recorded_frames',300)
    if recorded<1: raise ValueError('empty capture')
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
    if c.get('gpu01'):
        counts={k:sum(col(k)) for k in ('gpu_buffered_draws','gpu_reference_draws','gpu_fallback_draws','gpu_upload_bytes','gpu_resident_hits','gpu_resident_misses','gpu_resident_bypasses','gpu_indexed_draws','gpu_index_bypasses','gpu_index_upload_bytes')}
        validate_gpu_activity(c, counts)
        if c['gpu_resident'] and 'UNAVAILABLE' in m.get('GPU resident status',''):
            raise ValueError('resident initialization/upload failed')
        if c['gpu_indexed'] and 'UNAVAILABLE' in m.get('GPU index status',''):
            raise ValueError('index initialization/upload failed')
        result.update(counts, gpu_buffer_bytes_peak=max(col('gpu_buffer_bytes')), gpu_buffers=c['gpu_buffers'], gpu_resident=c['gpu_resident'], gpu_indexed=c['gpu_indexed'], gpu_group=c['gpu_group'], gpu_variant=c['gpu_variant'], graphics_profile=m.get('Graphics profile',''), gpu_buffer_status=m.get('GPU shadow buffer status',''))
    if c.get('gpu_stress') and c['isolation']==8:
        if set(col('drawn_particles'))!={float(c['gpu_stress'])}: raise ValueError('isolated particle count mismatch')
        if c.get('gpu_instanced_particles') and set(col('gpu_particle_instances'))!={float(c['gpu_stress'])}: raise ValueError('isolated GPU instance count mismatch')
    if c.get('gpu_expanded'):
        counts={k:sum(col(k)) for k in GPU_EXTRA_COUNTERS}
        validate_gpu_expanded(c,counts)
        if c.get('fixture')=='static_shadow': validate_world_fixed_fixture(c,rows)
        if c.get('gpu_buffers') and (not any(col('gpu_buffered_draws')) or any(col('gpu_fallback_draws'))): raise ValueError('combined buffer path not exercised or fell back')
        if max(col('gpu_local_bytes'))>32*1024*1024: raise ValueError('local mask budget exceeded')
        result.update(counts,gpu_group=c['gpu_group'],gpu_variant=c['gpu_variant'],gpu_local_bytes_peak=max(col('gpu_local_bytes')))
    if c.get('gpu_compare'):
        counts={k:sum(col(k)) for k in ('gpu_buffered_draws','gpu_reference_draws','gpu_fallback_draws','gpu_upload_bytes','gpu_resident_hits','gpu_resident_misses','gpu_resident_bypasses','gpu_indexed_draws','gpu_index_bypasses','gpu_index_upload_bytes')}
        compare_submission(c, counts)
        for enabled,status_key in [('gpu_resident','GPU resident status'),('gpu_indexed','GPU index status')]:
            if c[enabled] and 'UNAVAILABLE' in m.get(status_key,''): raise ValueError(status_key+': '+m[status_key])
        result.update(counts,gpu_buffer_bytes_peak=max(col('gpu_buffer_bytes')),gpu_compare=True)
        result['gpu_unexercised_optional']=';'.join(f for f,k in [('local_masks','gpu_local_builds'),('batch_unshadowed','gpu_direct_lights'),('instanced_particles','gpu_particle_instances')] if c.get('gpu_'+f) and not result.get(k,0) and not (f=='local_masks' and result.get('gpu_local_hits',0)))
    for flag,counter in [('scoped_binds','gpu_skipped_binds'),('material_clear','gpu_skipped_clear_pixels')]:
        if counter in rows[0]:
            result[counter+'_total']=sum(col(counter))
            if c.get('gpu_'+flag) and result[counter+'_total']<=0:
                result.setdefault('unexercised_refinement',[]).append(flag)
        elif c.get('gpu_'+flag): raise ValueError('Missing execution counter: '+counter)
    result.update(gpu_gl_renderer=m.get('GPU GL renderer',''),gpu_gl_driver=m.get('GPU GL driver',''),scene=m.get('Scene',''),executable=m.get('Executable',''),process_id=m.get('Process ID',''))
    if c.get('gpu_timing'):
        for key in ('gpu_shadow_prepare_wall_ms','gpu_shadow_draw_wall_ms','layer_cpu_ms','material_cpu_ms','post_cpu_ms','lighting_target_binds'):
            if key in rows[0]: result[key+'_mean']=statistics.mean(col(key))
        ts=[float(r['gpu_timestamp_ms']) for r in rows if float(r['gpu_timestamp_ms'])>=0]
        result.update(gpu_timestamp_span_ms_mean=statistics.mean(ts) if ts else None,gpu_timestamp_status=m.get('GPU timestamp status',''),gpu_gl_renderer=m.get('GPU GL renderer',''),gpu_timestamp_samples=max(col('gpu_timestamp_samples')),gpu_timestamp_skipped=max(col('gpu_timestamp_skipped')))
    if c.get('pb01_baseline'):
        result['spatial_mode']=m['Spatial mode']; result['spatial_queries']=int(m['Spatial queries'])
        result['simulation_ticks_between_recorded_frames']=int(m['PB measurement end tick'])-int(m['PB measurement start tick'])
        result['drawn_particles_mean']=statistics.mean(col('drawn_particles'))
        result['particle_update_cpu_ms_mean']=statistics.mean(col('particle_update_cpu_ms'))
        result['audio_cpu_ms_mean']=statistics.mean(col('audio_cpu_ms'))
        if c.get('pb_tick_benchmark'):
            ticks=validate_pb_tick_window(c,m)
            result.update(pb_start_tick=int(m['PB measurement start tick']),pb_end_tick=int(m['PB measurement end tick']),
                pb_platform_commands=int(m.get('PB measured platform commands','0')),pb_deaths=int(m.get('PB measured deaths','0')),pb_respawns=int(m.get('PB measured respawns','0')))
            result.update(dynamic_bodies=int(m['PB dynamic bodies']),dynamic_sweeps=int(m['PB dynamic sweeps']),dynamic_contacts=int(m['PB dynamic contacts']),dynamic_cpu_ms=float(m['PB dynamic CPU ms']),pb_prop_pair_impacts=int(m.get('PB prop pair impacts','0')))
            result.update(pb_character_pushes=int(m['PB character pushes']),pb_character_boundary_impacts=int(m['PB character boundary impacts']),pb_platform_prop_contacts=int(m['PB platform prop contacts']),pb_platform_prop_stops=int(m['PB platform prop stops']),pb_platform_destroyed_props=int(m['PB platform destroyed props']))
            if min(result['dynamic_bodies'],result['dynamic_sweeps'],result['dynamic_contacts'])<=0: raise ValueError('dynamic workload not exercised')
            if not math.isfinite(result['dynamic_cpu_ms']) or result['dynamic_cpu_ms']<0: raise ValueError('invalid dynamic CPU measurement')
            result['dynamic_cpu_ms_per_tick']=result['dynamic_cpu_ms']/ticks
            result.update(pb_workload_version=int(m['PB workload version']),pb_platform_recovery_attempts=int(m['PB platform recovery attempts']),pb_coupled_event_iterations=int(m['PB committed coupled event iterations']))
        if result['simulation_ticks_between_recorded_frames']<0: raise ValueError('world clock moved backwards')
        if c['spatial_mode']=='Indexed' and result['spatial_queries']<=0: raise ValueError('indexed collision path not exercised')
    for k in ('gc0','gc1','gc2'): result[k+'_total']=sum(col(k))
    return result

def validate_gpu_activity(c, counts, visual=False):
    n=lambda key: counts.get(key,0)
    if c['gpu_buffers']:
        if n('gpu_buffered_draws')<=0 or (not visual and n('gpu_upload_bytes')<=0 and n('gpu_resident_hits')<=0):
            raise ValueError('GPU buffer path not exercised')
        if n('gpu_fallback_draws') or n('gpu_reference_draws'):
            raise ValueError('GPU buffer path fell back or mixed submission paths')
    elif n('gpu_buffered_draws') or n('gpu_upload_bytes') or n('gpu_fallback_draws') or n('gpu_reference_draws')<=0:
        raise ValueError('reference GPU submission not exercised or unexpected buffered work')
    if c.get('gpu_resident'):
        if n('gpu_resident_hits')+n('gpu_resident_misses')<=0:
            raise ValueError('resident cache not exercised')
        if c.get('gpu_group')=='static_half' and n('gpu_resident_hits')<=0:
            raise ValueError('stationary resident cache did not reuse geometry')
        if n('gpu_resident_bypasses') and not visual and n('gpu_resident_bypasses')>=n('gpu_buffered_draws'):
            raise ValueError('resident cache entirely bypassed')
    elif n('gpu_resident_hits') or n('gpu_resident_misses') or n('gpu_resident_bypasses'):
        raise ValueError('unexpected resident work')
    if c.get('gpu_indexed'):
        if n('gpu_indexed_draws')<=0 or n('gpu_index_bypasses'):
            raise ValueError('indexed fan path not exercised or bypassed')
    elif n('gpu_indexed_draws') or n('gpu_index_bypasses') or n('gpu_index_upload_bytes'):
        raise ValueError('unexpected indexed work')


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
    if any(r.get('gpu_buffers') is not None for r in ok):
        with (out/'gpu_pairs.csv').open('w',newline='',encoding='utf-8') as f:
            w=csv.writer(f); w.writerow(['group','variant','reference_runs','variant_runs','reference_ms','variant_ms','delta_ms','reference_p95_ms','variant_p95_ms','reference_tracked_explicit_upload_bytes_per_frame','variant_tracked_explicit_upload_bytes_per_frame','resident_hits','indexed_draws'])
            for name,rs in groups.items():
                if not name.startswith('gpu_') or not name.endswith('_reference'): continue
                prefix=name[:-len('_reference')]
                for variant,*_ in GPU_VARIANTS[1:]:
                    bs=groups.get(prefix+'_'+variant,[])
                    if bs:
                        x=statistics.median(r['mean_ms'] for r in rs); y=statistics.median(r['mean_ms'] for r in bs)
                        uploads=lambda records: statistics.median((r['gpu_upload_bytes']+r['gpu_index_upload_bytes'])/r['frames'] for r in records)
                        w.writerow([prefix,variant,len(rs),len(bs),x,y,y-x,statistics.median(r['p95_ms'] for r in rs),statistics.median(r['p95_ms'] for r in bs),uploads(rs),uploads(bs),sum(r['gpu_resident_hits'] for r in bs),sum(r['gpu_indexed_draws'] for r in bs)])
    if any(n.startswith('gx_') for n in groups):
        with (out/'gpu_expanded_pairs.csv').open('w',newline='',encoding='utf-8') as f:
            w=csv.writer(f);w.writerow(['group','variant','reference_ms','variant_ms','delta_ms','reference_p95_ms','variant_p95_ms','reference_runs','variant_runs'])
            for name,rs in groups.items():
                if not name.startswith('gx_') or not name.endswith('_reference'): continue
                prefix=name[:-len('_reference')];x=statistics.median(r['mean_ms'] for r in rs)
                for n,bs in groups.items():
                    if n==name or not n.startswith(prefix+'_'):continue
                    y=statistics.median(r['mean_ms'] for r in bs)
                    w.writerow([prefix,n[len(prefix)+1:],x,y,y-x,statistics.median(r['p95_ms'] for r in rs),statistics.median(r['p95_ms'] for r in bs),len(rs),len(bs)])
    if 'gx_compare_reference' in groups:
        base=statistics.median(r['fps'] for r in groups['gx_compare_reference'])
        with (out/'gpu_comparison.csv').open('w',newline='',encoding='utf-8') as f:
            w=csv.writer(f); w.writerow(['configuration','runs','median_fps','fps_change_percent','median_p95_ms','median_p99_ms','median_cpu_submit_ms','median_draw_calls','median_target_changes','adapter','optional_features_not_exercised'])
            for name,rs in groups.items():
                if not name.startswith('gx_compare_'): continue
                med=lambda k: statistics.median(r[k] for r in rs)
                w.writerow([name.removeprefix('gx_compare_'),len(rs),round(med('fps'),3),round((med('fps')/base-1)*100,2),round(med('p95_ms'),3),round(med('p99_ms'),3),round(med('cpu_submit_ms_mean'),3),round(med('draw_calls_mean'),3),round(med('target_changes_mean'),3),' | '.join(sorted({r['gpu_adapter'] for r in rs})),' | '.join(sorted({r['gpu_unexercised_optional'] for r in rs if r['gpu_unexercised_optional']}))])
    if 'gxr_quality_reference' in groups:
        with (out/'gpu_refinement.csv').open('w',newline='',encoding='utf-8') as f:
            w=csv.writer(f); w.writerow(['case','runs','median_fps','median_p95_ms','median_p99_ms','median_cpu_submit_ms','median_target_changes','median_clear_pixels','median_skipped_binds','median_skipped_clear_pixels','unexercised','native_renderer'])
            for name,rs in groups.items():
                if not name.startswith(('gx_compare_','gxr_')): continue
                med=lambda k:statistics.median(r[k] for r in rs)
                w.writerow([name,len(rs),med('fps'),med('p95_ms'),med('p99_ms'),med('cpu_submit_ms_mean'),med('target_changes_mean'),med('selected_clear_pixels_mean') if 'selected_clear_pixels_mean' in rs[0] else '',statistics.median(r.get('gpu_skipped_binds_total',0)/r['frames'] for r in rs),statistics.median(r.get('gpu_skipped_clear_pixels_total',0)/r['frames'] for r in rs),';'.join(sorted({flag for r in rs for flag in r.get('unexercised_refinement',[])})), ' | '.join(sorted({r.get('gpu_gl_renderer','') for r in rs}))])
    if any(n.startswith('pb01_') for n in groups):
        with (out/'pb01_baseline.csv').open('w',newline='',encoding='utf-8') as f:
            w=csv.writer(f);w.writerow(['case','runs','median_fps','median_p95_ms','median_simulation_cpu_ms','median_draw_cpu_ms','median_thread_alloc_bytes','median_simulation_ticks','median_drawn_particles','median_logical_lights','native_renderer','median_dynamic_bodies','median_dynamic_sweeps','median_dynamic_contacts','median_dynamic_cpu_ms_per_tick','median_platform_commands','median_deaths','median_respawns','median_start_tick','median_end_tick','median_prop_pair_impacts','median_character_pushes','median_character_boundary_impacts','median_platform_prop_contacts','median_platform_prop_stops','median_platform_destroyed_props','workload_version','median_platform_recovery_attempts','median_committed_coupled_event_iterations'])
            for n,rs in groups.items():
                if not n.startswith('pb01_'): continue
                med=lambda k:statistics.median(r[k] for r in rs)
                w.writerow([n,len(rs),med('fps'),med('p95_ms'),med('simulation_cpu_ms_mean'),med('previous_draw_cpu_ms_mean'),med('thread_alloc_bytes_mean'),med('simulation_ticks_between_recorded_frames'),med('drawn_particles_mean'),med('logical_lights_mean'),' | '.join(sorted({r['gpu_gl_renderer'] for r in rs})),*[statistics.median(r.get(k,0) for r in rs) for k in ('dynamic_bodies','dynamic_sweeps','dynamic_contacts','dynamic_cpu_ms_per_tick','pb_platform_commands','pb_deaths','pb_respawns','pb_start_tick','pb_end_tick','pb_prop_pair_impacts','pb_character_pushes','pb_character_boundary_impacts','pb_platform_prop_contacts','pb_platform_prop_stops','pb_platform_destroyed_props','pb_workload_version','pb_platform_recovery_attempts','pb_coupled_event_iterations')]])
    baseline=groups.get('candidate_uncapped' ,[]) or groups.get('po10b_reference',[])
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


def start_gpu_telemetry(dest):
    """Optional diagnostic sampling, not process GPU timing or a benchmark requirement."""
    exe=shutil.which('nvidia-smi')
    note=dest/'gpu_telemetry_status.txt'
    if not exe:
        note.write_text('UNAVAILABLE: nvidia-smi not on PATH.\n',encoding='utf-8')
        return None
    fields='timestamp,index,name,driver_version,utilization.gpu,utilization.memory,memory.used,memory.total,temperature.gpu,clocks.gr,clocks.mem,power.draw'
    cmd=[exe,'--query-gpu='+fields,'--format=csv','-lms','500']
    try:
        with (dest/'gpu_telemetry.csv.txt').open('w',encoding='utf-8') as f, (dest/'gpu_telemetry_errors.txt').open('w',encoding='utf-8') as err:
            proc=subprocess.Popen(cmd,stdout=f,stderr=err)
        note.write_text('System-wide NVIDIA samples; not application-specific or GPU pass timing. Diagnostic run only.\n',encoding='utf-8')
        return proc
    except OSError as e:
        note.write_text('UNAVAILABLE: '+str(e)+'\n',encoding='utf-8')
        return None


def stop_gpu_telemetry(proc):
    if proc is None: return
    if proc.poll() is None:
        proc.terminate()
        try: proc.wait(timeout=5)
        except subprocess.TimeoutExpired: proc.kill(); proc.wait()


def run_attempt(c, dest, root, project, timeout):
    start=time.monotonic()
    rc=None
    telemetry=start_gpu_telemetry(dest) if c.get("gpu_telemetry") else None
    try:
        cmd=gpuenv.command(project,c.get('scene','LIGHTING_PERFORMANCE_LAB'))
        power=gpuenv.power_source(); active_plan=gpuenv.power_plan()
        if c.get('require_ac') and power!='AC': raise ValueError('AC power required')
        (dest/'launch.json').write_text(json.dumps({'command':cmd,'power':power,'active_plan':active_plan},indent=2),encoding='utf-8')
        with (dest/'process.log').open('w',encoding='utf-8') as log:
            proc=subprocess.Popen(cmd,cwd=root,env=environment(c,dest),stdout=log,stderr=subprocess.STDOUT)
            try: rc=gpuenv.wait_checked(proc,timeout,dest,c.get("require_ac",False),power,active_plan)
            except (subprocess.TimeoutExpired,KeyboardInterrupt,ValueError):
                if os.name=='nt': subprocess.run(['taskkill','/PID',str(proc.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                else: proc.kill()
                proc.wait(); raise
        if rc: raise ValueError(f'game exit code {rc}')
        r=read_result(c,dest)
        if os.name=='nt' and c.get('require_native'):
            if int(r.get('process_id') or 0)!=proc.pid or os.path.normcase(os.path.abspath(r.get('executable') or ''))!=os.path.normcase(os.path.abspath(cmd[0])):
                raise ValueError('Capture process or executable differs from launched Release apphost')
    except (ValueError,KeyError,OSError,subprocess.TimeoutExpired) as e:
        retry=retryable_focus_cancel(dest,rc)
        r=dict(name=c['name'],status='failed',error=FOCUS_CANCEL if retry else str(e))
    else:
        retry=False
    finally:
        stop_gpu_telemetry(telemetry)
    r.update(case_folder=dest.name,wall_seconds=round(time.monotonic()-start,2))
    return r,retry


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--allow-battery',action='store_true',help='permit separate battery-powered tests; still reject power-source changes')
    ap.add_argument('--gpu-telemetry',action='store_true',help='optional diagnostic NVIDIA system-wide sampling; do not compare to clean timing runs')
    ap.add_argument('--no-wide',action='store_true',help='skip 1080p test on small displays')
    suites=ap.add_mutually_exclusive_group()
    ap.add_argument('--project',type=Path,help='experimental backend project path')
    ap.add_argument('--gpu-timing',action='store_true',help='diagnostic timestamp queries; separate from clean timing')
    suites.add_argument('--pb01-baseline',action='store_true',help='integrated engine baseline with reference/indexed spatial queries')
    suites.add_argument('--gpu01-refine',action='store_true',help='matched performance and quality labs, expanded isolated stress tests')
    suites.add_argument('--gpu01-compare',action='store_true',help='five matched GPU configuration comparisons')
    ap.add_argument('--expect-adapter',help='reject captures on an adapter not containing this name')
    suites.add_argument('--gpu01-expanded',action='store_true',help='GPU shader, mask and particle A/B suite')
    suites.add_argument('--gpu01',action='store_true',help='GPU shadow submission A/B suite')
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
    cases=pb01_baseline_plan(a.tier) if a.pb01_baseline else gpu_refine_plan(a.tier) if a.gpu01_refine else gpu_compare_plan() if a.gpu01_compare else gpu_expanded_plan(a.tier) if a.gpu01_expanded else gpu01_plan() if a.gpu01 else po10f_plan() if a.po10f else po10d_plan() if a.po10d else po10c_plan() if a.po10c else po10b_plan() if a.po10b else po10a_plan() if a.po10a else po04b_plan() if a.po04b else plan(a.isolations,a.single_on)
    if a.tier != 'full' and not (a.po10a or a.po10b or a.po10c or a.po10d or a.po10f or a.gpu01 or a.gpu01_expanded or a.gpu01_compare or a.gpu01_refine or a.pb01_baseline): ap.error('quick/standard tiers require --po10a through --po10f, --gpu01, or --pb01-baseline')
    if a.tier != 'full' and (a.smoke or a.case): ap.error('use --tier full with --smoke or --case')
    if a.tier != 'full' and not (a.gpu01_expanded or a.gpu01_compare or a.gpu01_refine or a.pb01_baseline):
        names=tuple(c['name'] for c in cases if a.tier=='full' or (a.tier=='standard' and c['gpu_group']!='wide_half') or (a.tier=='quick' and c['gpu_group'] in GPU_QUICK_GROUPS)) if a.gpu01 else (F_QUICK if a.tier=='quick' else tuple(c['name'] for c in cases)) if a.po10f else tuple(c['name'] for c in cases) if a.po10d else (C_QUICK if a.tier=='quick' else tuple(c['name'] for c in cases)) if a.po10c else (B_QUICK if a.tier=='quick' else B_STANDARD) if a.po10b else (QUICK_CASES if a.tier=='quick' else STANDARD_CASES)
        cases=[c for c in cases if c['name'] in names]
    if a.no_wide: cases=[c for c in cases if not c["wide"]]
    if a.smoke and a.case: ap.error('--smoke and --case cannot be combined')
    if a.smoke and a.gpu01_compare: ap.error('--gpu01-compare already is a targeted suite')
    if a.smoke:
        names=tuple(c['name'] for c in cases if c['gpu_group']=='static_half') if a.gpu01 else ('hard_field_reference','hard_field_cached') if a.po10f else ('follow_snap','pan_half') if a.po10d else ('cache_follow','cache_locked') if a.po10c else ('po10b_reference','po10b_combined') if a.po10b else ('candidate_uncapped','combined_50','soft_combined_50') if a.po10a else ('candidate_uncapped','po04b_without_shared_samples') if a.po04b else ('candidate_uncapped','candidate_vsync')
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
        if a.expect_adapter: c["expect_adapter"]=a.expect_adapter
        if os.name=="nt":
            c["require_ac"]=not a.allow_battery
            c["require_native"]=bool(a.gpu01_refine or a.pb01_baseline)
        if a.gpu_telemetry: c["gpu_telemetry"]=True
        if a.gpu_timing: c["gpu_timing"]=True
        if (a.gpu01 or a.gpu01_expanded or a.gpu01_refine) and c.get('po10c'):
            c.update(adaptive_warmup=False,max_warmup_frames=warmup)
        elif a.po10c or a.po10d or a.po10f or a.adaptive_warmup:
            adaptive=not a.fixed_warmup and not c['motion'] and not c.get('camera_pan',0)
            c.update(adaptive_warmup=adaptive,max_warmup_frames=maximum if adaptive else warmup)
    if a.list:
        print(json.dumps(cases,indent=2)); return 0
    root=Path(__file__).resolve().parent.parent
    project=a.project.resolve() if a.project else root/'Platforms'/'DesktopGL'/'PlatformerSandbox.DesktopGL.csproj'
    if os.name=="nt" and not a.allow_battery and gpuenv.power_source()!="AC": ap.error("Plug in AC power, or explicitly use --allow-battery for a battery baseline")
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
