"""Opt-in DesktopGL backbuffer A/B capture. No timed benchmark contamination.
Uses only the Python standard library. Exact-repeat control is mandatory.
"""
import argparse, copy, json, math, os, struct, subprocess, sys, time, zlib
from datetime import datetime
from pathlib import Path
import po_run_benchmarks as bench


def png(path, width, height, rgb):
    if len(rgb)!=width*height*3: raise ValueError('invalid RGB dimensions')
    def chunk(t,d): return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
    scan=b''.join(b'\x00'+rgb[y*width*3:(y+1)*width*3] for y in range(height))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(scan))+chunk(b'IEND',b''))


def compare(a,b):
    if len(a)!=len(b) or not a or len(a)%3: raise ValueError('incompatible RGB images')
    diff=bytearray(len(a)); total=squares=maximum=changed=over2=0
    for i in range(0,len(a),3):
        ds=[abs(a[j]-b[j]) for j in range(i,i+3)]
        peak=max(ds); maximum=max(maximum,peak); changed+=peak>0; over2+=peak>2
        total+=sum(ds); squares+=sum(d*d for d in ds)
        for j,d in enumerate(ds): diff[i+j]=min(255,d*8)
    pixels=len(a)//3
    return dict(mean_abs=total/len(a),rmse=math.sqrt(squares/len(a)),max_channel=maximum,
                changed_fraction=changed/pixels,over_2_fraction=over2/pixels),bytes(diff)


def capture(c,dest,root,project,timeout):
    dest.mkdir(); env=bench.environment(c,dest); env.pop('PS_PO_AUTO_OUTPUT',None)
    env['PS_PO_VISUAL_OUTPUT']=str(dest); env['PS_PO_HUD']='0'
    if c.get('refinement') or c.get('gpu_expanded'): env['PS_PO_GPU_VISUAL_WARMUP']='1'
    env['PS_PO_VISUAL_TICKS']=str(c.get('visual_ticks',60))
    if c.get('pb_dynamic_visual'): env['PS_PB01_VISUAL_BODY']=str(c['pb_visual_body'])
    with (dest/'process.log').open('w',encoding='utf-8') as log:
        proc=subprocess.Popen(bench.gpuenv.command(project,c.get('scene','LIGHTING_PERFORMANCE_LAB')),cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
        try: rc=proc.wait(timeout=timeout)
        except (subprocess.TimeoutExpired,KeyboardInterrupt):
            if os.name=='nt': subprocess.run(['taskkill','/PID',str(proc.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            else: proc.kill()
            proc.wait(); raise
    if rc: raise ValueError(f'game exit {rc}: {dest.name}')
    m=dict(line.split(': ',1) for line in (dest/'visual.txt').read_text(encoding='utf-8-sig').splitlines() if ': ' in line)
    bench.validate_adapter(c.get('expect_adapter'),m.get('GPU',''),m.get('GPU GL renderer',''),strict=(bool(c.get('expect_adapter')) or (os.name=='nt' and c.get('refinement'))) and m.get('GPU backend','DesktopGL')=='DesktopGL')
    if c.get('pb_dynamic_visual'):
        if int(m.get('PB visible bodies','0'))<1 or not m.get('PB body signature'): raise ValueError('no visible dynamic body workload')
        if m.get('PB visual target')!=str(c['pb_visual_body']) or m.get('PB target visible')!='True': raise ValueError('requested body not visible')
        if c['visual_ticks']>=60 and int(m.get('PB contacts','0'))<1: raise ValueError('dynamic contacts not exercised')
        if int(m.get('PB pair impacts','0'))<1: raise ValueError('prop pair impacts not exercised')
    if c.get('spatial_mode'):
        if m.get('Spatial mode')!=c['spatial_mode']: raise ValueError('wrong spatial mode')
        if c['spatial_mode']!='Reference' and int(m.get('Spatial queries','0'))<=0:
            raise ValueError('indexed spatial path not exercised')
        if c.get('scene') in ('ENGINE_SCALABILITY_LAB','PHYSICS_INTERACTION_LAB'):
            for k,v in {'Width':'1280','Height':'720','Ticks':str(c.get('visual_ticks',60)),'Scene':c['scene']}.items():
                if m.get(k)!=v: raise ValueError(f'wrong spatial capture metadata: {k}')
            raw=(dest/'frame.rgba').read_bytes()
            if len(raw)!=1280*720*4: raise ValueError('wrong backbuffer byte count')
            rgb=bytearray(1280*720*3); rgb[0::3]=raw[0::4]; rgb[1::3]=raw[1::4]; rgb[2::3]=raw[2::4]
            if len(set(rgb))<4: raise ValueError('blank spatial capture')
            png(dest/'frame.png',1280,720,rgb); (dest/'frame.rgba').unlink()
            return m,rgb
    want={'Width':'1280','Height':'720','Ticks':str(c.get('visual_ticks',60)),'Scene':c.get('scene','LIGHTING_PERFORMANCE_LAB'),
          'Light scale':str(c['light_scale']),'Mask scale':str(c['mask_scale']),
          'Bloom scale':str(c.get('bloom_scale',100)), 'Adaptive':str(bool(c.get('adaptive',0))),
          'Static fields':str(bool(c.get('static_fields',0))), 'Motion frozen':str(not c['motion']),
          'Shadow edge':'Soft8Px' if c.get('soft',0) else 'Hard',
          'Light width':str((1280*c['light_scale']+99)//100),'Mask width':str((1280*c['mask_scale']+99)//100),
          'Original mask':str(c['mask']),'Quality':c['quality']}
    if c.get('po10c'):
        want.update({'Fixture':c['fixture'],'Camera locked':str(bool(c['lock_camera']))})
    if c.get('po10d'):
        want.update({'Camera pan':str(bool(c['camera_pan'])), 'Camera convergence':str(bool(c['camera_snap']))})
    if c.get('po10f'): want['Static shadow fields']=str(bool(c['static_shadows']))
    if c.get('fixture','normal') != 'normal': want['Fixture coordinates']='world_fixed_v1'
    want.update({name:str(bool(c['tflags'][flag])) for flag,name in zip(bench.TFLAGS,('Material regions','Light regions','Shared fields','Skip clears','Cache capacity'))})
    for k,v in want.items():
        if m.get(k)!=v: raise ValueError(f'{dest.name}: metadata {k}: expected {v}, got {m.get(k)}')
    if c.get('gpu01'):
        for key,flag in [('GPU shadow buffers requested','gpu_buffers'),('GPU resident requested','gpu_resident'),('GPU indexed requested','gpu_indexed')]:
            if m.get(key)!=str(bool(c[flag])): raise ValueError('GPU request mismatch: '+key)
        actual={k:int(m[v]) for k,v in [('gpu_buffered_draws','GPU buffered draws'),('gpu_reference_draws','GPU reference draws'),('gpu_fallback_draws','GPU fallback draws'),('gpu_resident_hits','GPU resident hits'),('gpu_resident_misses','GPU resident misses'),('gpu_indexed_draws','GPU indexed draws'),('gpu_index_bypasses','GPU index bypasses')]}
        bench.validate_gpu_activity(c,actual,visual=True)
    if c.get('gpu_compare'):
        for key,flag in [('GPU shadow buffers requested','gpu_buffers'),('GPU resident requested','gpu_resident'),('GPU indexed requested','gpu_indexed')]:
            if m.get(key)!=str(bool(c[flag])): raise ValueError('GPU request mismatch: '+key)
        if int(m['GPU fallback draws']): raise ValueError('GPU submission fell back')
    if c.get('gpu_expanded'):
        for k,v in bench.gpu_extra_metadata(c).items():
            if m.get(k)!=v: raise ValueError('GPU request mismatch: '+k)
        bench.validate_gpu_expanded(c,{k:int(m[v]) for k,v in zip(bench.GPU_EXTRA_COUNTERS,bench.GPU_EXTRA_VISUAL)})
    if c.get('refinement') and m.get('Visual draws')!='14': raise ValueError('wrong refinement warmup draw count')
    if c.get('refinement') and c.get('scene')=='LIGHTING_QUALITY_LAB':
        if m.get('Light coverage')!=('Preserve' if c.get('fixture','normal')=='normal' else 'Budget'): raise ValueError('wrong light coverage')
    for f in ('scoped_binds','material_clear'):
        if c.get('gpu_'+f) and m.get('GPU '+f)!='True': raise ValueError('GPU request mismatch: '+f)
    if int(m['Lights'])<=0: raise ValueError('lighting inactive')
    if c.get('fixture','normal') != 'normal' and len(m.get('Light positions','').split(';'))!=5:
        raise ValueError('missing five fixture light positions')
    raw=(dest/'frame.rgba').read_bytes()
    if len(raw)!=1280*720*4: raise ValueError('wrong backbuffer byte count')
    rgb=bytearray(1280*720*3); rgb[0::3]=raw[0::4]; rgb[1::3]=raw[1::4]; rgb[2::3]=raw[2::4]
    if len(set(rgb))<4: raise ValueError('blank or degenerate capture')
    png(dest/'frame.png',1280,720,rgb)
    (dest/'frame.rgba').unlink()
    return m,rgb


def plan(extended=False, fixtures=False, temporal=False, hard_cache=False, gpu01=False, gpu_quick=False,gpu_expanded=False,gpu_compare=False):
    if gpu_compare:
        cs=[dict(c,visual_ticks=60) for c in bench.gpu_compare_plan()]
        return [("gpu_compare",cs[0],[(c["gpu_variant"],c,"quality") for c in cs[1:]])]
    if gpu_expanded:
        groups={}
        for c in bench.gpu_expanded_plan('quick' if gpu_quick else 'standard'):
            groups.setdefault(c['gpu_group'],[]).append(dict(c,visual_ticks=60))
        return [(tag,cs[0],[(c['gpu_variant'],c,'strict' if c['gpu_variant']=='specialized' else 'quality') for c in cs[1:]]) for tag,cs in groups.items()]
    if gpu01:
        c={v['name']:v for v in bench.gpu01_plan()}
        return [(tag,dict(c['gpu_'+tag+'_reference'],visual_ticks=60),[(variant,dict(c['gpu_'+tag+'_'+variant],visual_ticks=60),'strict') for variant,*_ in bench.GPU_VARIANTS[1:]]) for tag in (bench.GPU_QUICK_GROUPS if gpu_quick else ('static_half','moving_full','moving_half','soft_half','pan_half'))]

    if hard_cache:
        c={v['name']:v for v in bench.po10f_plan()}
        return [('hard_cache',dict(c['hard_field_reference'],visual_ticks=120),[
            ('cached',dict(c['hard_field_cached'],visual_ticks=120),'strict'),
            ('buffers_off',dict(c['caster_buffers_off'],visual_ticks=120),'strict')]),
            ('moving_hard_cache',dict(c['moving_field_reference'],visual_ticks=30),[
            ('cached',dict(c['moving_field_cached'],visual_ticks=30),'strict')]),
            ('soft_cache_guard',dict(c['soft_field_reference'],visual_ticks=120),[
            ('cached',dict(c['soft_field_cached'],visual_ticks=120),'strict')])]
    if temporal:
        c={x['name']:x for x in bench.po10d_plan()}
        pairs=[]
        for tick in (30,60,150):
            def at(name): return dict(c[name],visual_ticks=tick)
            pairs.append((f'pan_tick_{tick}',at('pan_reference'),[
                ('full',at('pan_full'),'strict'),('half',at('pan_half'),'quality')]))
        return pairs
    if fixtures:
        c={x['name']:x for x in bench.po10c_plan()}
        return [('static_fixture',c['static_fixture_reference'],[('static_enabled',c['static_fixture_enabled'],'strict')]),
                ('adaptive_fixture',c['adaptive_fixture_reference'],[('adaptive_enabled',c['adaptive_fixture_enabled'],'quality')])]
    old={c['name']:c for c in bench.po10a_plan()}; new={c['name']:c for c in bench.po10b_plan()}
    # Reference repeat shares all actual renderer settings; its own name only identifies output.
    pairs=[('hard',old['candidate_uncapped'],[('full',old['combined_full'],'strict'),('half',old['combined_50'],'quality'),('adaptive_bloom',new['po10b_combined'],'quality')])]
    if extended:
        pairs += [('soft',old['soft_reference'],[('full',old['soft_combined_full'],'strict'),('half',old['soft_combined_50'],'quality')]),
                  ('motion',old['motion_reference'],[('full',old['motion_combined_full'],'strict'),('half',old['motion_combined_50'],'quality')])]
    return pairs


def refinement_plan():
    from copy import deepcopy
    pairs=[]
    base=bench.gpu_compare_plan()
    for tick in (30,60,150):
        cs=[dict(deepcopy(c),visual_ticks=tick,scene='LIGHTING_QUALITY_LAB',po10d=True,camera_pan=1,camera_snap=1) for c in base]
        pairs.append((f'quality_pan_{tick}',cs[0],[(c['gpu_variant'],c,'tolerance' if c['gpu_variant']=='shaders' else 'quality') for c in cs[1:]]))
    # Stable cache coverage independent of dynamic scene; compare frozen scene with camera pan.
    local=[c for c in bench.gpu_expanded_plan('quick') if c['gpu_group']=='local_pan']
    for tick in (30,60,150):
        cs=[dict(deepcopy(c),visual_ticks=tick,scene='LIGHTING_QUALITY_LAB') for c in local]
        pairs.append((f'static_pan_{tick}',cs[0],[('local',cs[1],'quality')]))
    shader=deepcopy(base[3]); shader['visual_ticks']=60
    variants=[]
    for flag in ('scoped_binds','material_clear','both'):
        c=deepcopy(shader); c.update(gpu_scoped_binds=int(flag!='material_clear'),gpu_material_clear=int(flag!='scoped_binds'))
        variants.append((flag,c,'strict'))
    pairs.append(('target_clear',shader,variants))
    for _,ref,variants in pairs:
        ref['refinement']=True
        for _,c,_ in variants: c['refinement']=True
    return pairs

def quality_verdict(kind,metrics):
    if metrics['max_channel']==0: return 'EXACT_MATCH'
    if kind=='tolerance' and metrics['max_channel']<=1: return 'EQUIVALENT_WITHIN_TOLERANCE'
    if kind in ('quality','tolerance'): return 'REVIEW_REQUIRED'
    return 'DIFFERENCES_FOUND'


def validate_fixed_light_camera_pair(stationary,moving):
    if stationary['Camera']==moving['Camera']: raise ValueError('moving hard-shadow capture did not change camera')
    if stationary['Light positions']!=moving['Light positions']: raise ValueError('static shadow fixture lights moved with camera')


def spatial_plan(tier='quick', scenes=('ENGINE_SCALABILITY_LAB','LIGHTING_QUALITY_LAB')):
    pairs=[]
    for scene in scenes:
        for ticks in ((60,) if tier=='quick' else (30,60,150)):
            c=bench.case('po05_reference'); c.update(scene=scene,visual_ticks=ticks,spatial_mode='Reference',motion=1,
                light_scale=100,mask_scale=100,soft=0,tflags={f:0 for f in bench.TFLAGS},po10d=True,camera_pan=1,camera_snap=1)
            other=dict(c,name='po05_indexed',spatial_mode='Indexed')
            pairs.append((scene.lower()+'_'+str(ticks),c,[('indexed',other,'strict')]))
    return pairs

def pb_dynamic_plan(tier='quick'):
    pairs=spatial_plan(tier,('PHYSICS_INTERACTION_LAB','ENGINE_INTEGRATION_LAB'))
    for _,ref,variants in pairs:
        for c in [ref]+[c for _,c,_ in variants]:
            stack=6 if c["scene"]=="PHYSICS_INTERACTION_LAB" else 9
            c.update(pb_dynamic_visual=True,camera_pan=0,pb_visual_body=stack if c["visual_ticks"]==60 else (1 if c["visual_ticks"]==30 else 2))
    from copy import deepcopy
    carriers=[]
    for name,ref,variants in pairs:
        if ref['visual_ticks']!=60: continue
        a,b=deepcopy((ref,variants))
        target=9 if a['scene']=='PHYSICS_INTERACTION_LAB' else 12
        for c in [a]+[c for _,c,_ in b]:c['pb_visual_body']=target
        carriers.append((name+'_carrier',a,b))
    return pairs+carriers


def main():
    p=argparse.ArgumentParser(description=__doc__); p.add_argument('--pb01',action='store_true'); p.add_argument('--po05',action='store_true'); p.add_argument('--po05-tier',choices=('quick','standard','full'),default='quick'); p.add_argument('--extended',action='store_true'); p.add_argument('--fixtures',action='store_true'); p.add_argument('--temporal',action='store_true'); p.add_argument('--hard-cache',action='store_true'); p.add_argument('--gpu01',action='store_true'); p.add_argument('--gpu01-expanded',action='store_true'); p.add_argument('--gpu01-compare',action='store_true'); p.add_argument('--gpu01-refine',action='store_true'); p.add_argument('--expect-adapter'); p.add_argument('--project',type=Path); p.add_argument('--gpu-quick',action='store_true',help='only stationary and moving half-resolution GPU cases'); p.add_argument('--no-build',action='store_true'); p.add_argument('--timeout',type=int,default=90); p.add_argument('--list',action='store_true'); a=p.parse_args()
    if a.gpu_quick and not (a.gpu01 or a.gpu01_expanded): p.error("--gpu-quick requires --gpu01")
    if a.gpu01_compare and (a.extended or a.fixtures or a.temporal or a.hard_cache or a.gpu01 or a.gpu01_expanded or a.gpu_quick): p.error("choose one visual suite")
    pairs=pb_dynamic_plan(a.po05_tier) if a.pb01 else spatial_plan(a.po05_tier) if a.po05 else refinement_plan() if a.gpu01_refine else plan(a.extended,a.fixtures,a.temporal,a.hard_cache,a.gpu01,a.gpu_quick,a.gpu01_expanded,a.gpu01_compare)
    if a.expect_adapter:
        for _,ref,variants in pairs:
            ref["expect_adapter"]=a.expect_adapter
            for _,c,_ in variants: c["expect_adapter"]=a.expect_adapter
    if a.list:
        print(json.dumps(pairs,indent=2)); return 0
    root=Path(__file__).resolve().parent.parent; project=a.project.resolve() if a.project else root/'Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj'
    if not project.exists(): p.error('extract into solution root')
    out=root/'Benchmarks'/('PO_visual_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f')); out.mkdir(parents=True)
    results=[]; status=0
    try:
        if not a.no_build:
            with (out/'build.log').open('w',encoding='utf-8') as log:
                if subprocess.call(['dotnet','build',str(project),'-c','Release'],cwd=root,stdout=log,stderr=subprocess.STDOUT): raise ValueError('build failed')
        hard_reference=None
        static_pan_reference=None
        for group,reference,variants in pairs:
            captures={}
            for name,c,kind in [('reference',reference,'control'),('repeat',reference,'control'),*variants]:
                tag=group+'_'+name; print('Capturing '+tag+'; leave window focused and inputs neutral.',flush=True)
                time.sleep(.5); m,rgb=capture(c,out/tag,root,project,a.timeout)
                captures[name]=(m,rgb)
                if name=='reference':
                    if group=='hard_cache': hard_reference=m
                    if group.startswith('static_pan_'):
                        if static_pan_reference is not None:
                            validate_fixed_light_camera_pair(static_pan_reference,m)
                        static_pan_reference=m
                    if group=='moving_hard_cache' and hard_reference is not None:
                        validate_fixed_light_camera_pair(hard_reference,m)
                    continue
                base,ref=captures['reference']; same_state=all(m[k]==base[k] for k in ('World tick','Ticks','Scene','Lights','GPU','Player bounds','Camera')) and m.get('GPU GL renderer')==base.get('GPU GL renderer')
                if c.get('spatial_mode') or c.get('fixture','normal')!='normal' or c.get('gpu01') or c.get('gpu_expanded'): same_state=same_state and m['Light positions']==base['Light positions']
                if c.get('pb_dynamic_visual'): same_state=same_state and m['PB body signature']==base['PB body signature'] and m['PB visible bodies']==base['PB visible bodies'] and m['PB contacts']==base['PB contacts'] and m['PB pair impacts']==base['PB pair impacts']
                metrics,diff=compare(ref,rgb); png(out/(tag+'_diff_x8.png'),1280,720,diff)
                if name=='repeat': control_ok=same_state and metrics['max_channel']==0
                if not same_state or not control_ok: verdict='INCONCLUSIVE_STATE_OR_CONTROL'
                elif name not in ('reference','repeat') and any(c.get('gpu_'+f) and int(m.get(k,'0'))<=0 for f,k in [('scoped_binds','GPU skipped binds'),('material_clear','GPU skipped clear pixels')]): verdict='INCONCLUSIVE_OPTIMIZATION_NOT_EXERCISED'
                elif group=='hard_cache' and name=='cached' and int(m.get('Static shadow hits','0'))<=0: verdict='INCONCLUSIVE_CACHE_NOT_EXERCISED'
                elif group=='soft_cache_guard' and int(m.get('Static shadow hits','0'))>0: verdict='DIFFERENCES_FOUND'
                elif c.get('fixture')=='static' and c.get('static_fields') and int(m.get('Static hits','0'))<=0: verdict='INCONCLUSIVE_CACHE_NOT_EXERCISED'
                else: verdict=quality_verdict(kind,metrics)
                results.append(dict(group=group,name=name,comparison=kind,state_matches=same_state,reference_repeat_exact=control_ok,verdict=verdict,**metrics))
                print('  '+verdict,flush=True)
                if verdict.startswith('INCONCLUSIVE') or verdict=='DIFFERENCES_FOUND' or ((c.get('gpu_expanded') or a.gpu01_refine) and verdict=='REVIEW_REQUIRED'): status=2
    except (OSError,ValueError,KeyError,subprocess.TimeoutExpired) as e:
        results.append(dict(error=str(e))); print('FAILED:',e); status=1
    except KeyboardInterrupt: status=130
    finally:
        (out/'visual_summary.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
        print('Upload this ZIP:',bench.archive(out),flush=True)
    return status
if __name__=='__main__': sys.exit(main())
