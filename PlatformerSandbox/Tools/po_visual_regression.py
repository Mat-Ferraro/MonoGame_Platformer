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
    env['PS_PO_VISUAL_TICKS']=str(c.get('visual_ticks',60))
    with (dest/'process.log').open('w',encoding='utf-8') as log:
        proc=subprocess.Popen(['dotnet','run','--project',str(project),'-c','Release','--no-build','--no-launch-profile','--','--dev-level=LIGHTING_PERFORMANCE_LAB'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
        try: rc=proc.wait(timeout=timeout)
        except (subprocess.TimeoutExpired,KeyboardInterrupt):
            if os.name=='nt': subprocess.run(['taskkill','/PID',str(proc.pid),'/T','/F'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            else: proc.kill()
            proc.wait(); raise
    if rc: raise ValueError(f'game exit {rc}: {dest.name}')
    m=dict(line.split(': ',1) for line in (dest/'visual.txt').read_text(encoding='utf-8-sig').splitlines() if ': ' in line)
    want={'Width':'1280','Height':'720','Ticks':str(c.get('visual_ticks',60)),'Scene':'LIGHTING_PERFORMANCE_LAB',
          'Light scale':str(c['light_scale']),'Mask scale':str(c['mask_scale']),
          'Bloom scale':str(c.get('bloom_scale',100)), 'Adaptive':str(bool(c.get('adaptive',0))),
          'Static fields':str(bool(c.get('static_fields',0))), 'Motion frozen':str(not c['motion']),
          'Shadow edge':'Soft8Px' if c['soft'] else 'Hard',
          'Light width':str((1280*c['light_scale']+99)//100),'Mask width':str((1280*c['mask_scale']+99)//100),
          'Original mask':'127','Quality':'High'}
    if c.get('po10c'):
        want.update({'Fixture':c['fixture'],'Camera locked':str(bool(c['lock_camera']))})
    if c.get('po10d'):
        want.update({'Camera pan':str(bool(c['camera_pan'])), 'Camera convergence':str(bool(c['camera_snap']))})
    if c.get('po10f'): want['Static shadow fields']=str(bool(c['static_shadows']))
    if c.get('fixture','normal') != 'normal': want['Fixture coordinates']='world_fixed_v1'
    want.update({name:str(bool(c['tflags'][flag])) for flag,name in zip(bench.TFLAGS,('Material regions','Light regions','Shared fields','Skip clears','Cache capacity'))})
    for k,v in want.items():
        if m.get(k)!=v: raise ValueError(f'{dest.name}: metadata {k}: expected {v}, got {m.get(k)}')
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


def plan(extended=False, fixtures=False, temporal=False, hard_cache=False):
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


def validate_fixed_light_camera_pair(stationary,moving):
    if stationary['Camera']==moving['Camera']: raise ValueError('moving hard-shadow capture did not change camera')
    if stationary['Light positions']!=moving['Light positions']: raise ValueError('static shadow fixture lights moved with camera')


def main():
    p=argparse.ArgumentParser(description=__doc__); p.add_argument('--extended',action='store_true'); p.add_argument('--fixtures',action='store_true'); p.add_argument('--temporal',action='store_true'); p.add_argument('--hard-cache',action='store_true'); p.add_argument('--no-build',action='store_true'); p.add_argument('--timeout',type=int,default=90); p.add_argument('--list',action='store_true'); a=p.parse_args()
    pairs=plan(a.extended,a.fixtures,a.temporal,a.hard_cache)
    if a.list:
        print(json.dumps(pairs,indent=2)); return 0
    root=Path(__file__).resolve().parent.parent; project=root/'Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj'
    if not project.exists(): p.error('extract into solution root')
    out=root/'Benchmarks'/('PO_visual_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f')); out.mkdir(parents=True)
    results=[]; status=0
    try:
        if not a.no_build:
            with (out/'build.log').open('w',encoding='utf-8') as log:
                if subprocess.call(['dotnet','build',str(project),'-c','Release'],cwd=root,stdout=log,stderr=subprocess.STDOUT): raise ValueError('build failed')
        hard_reference=None
        for group,reference,variants in pairs:
            captures={}
            for name,c,kind in [('reference',reference,'control'),('repeat',reference,'control'),*variants]:
                tag=group+'_'+name; print('Capturing '+tag+'; leave window focused and inputs neutral.',flush=True)
                time.sleep(.5); m,rgb=capture(c,out/tag,root,project,a.timeout)
                captures[name]=(m,rgb)
                if name=='reference':
                    if group=='hard_cache': hard_reference=m
                    if group=='moving_hard_cache' and hard_reference is not None:
                        validate_fixed_light_camera_pair(hard_reference,m)
                    continue
                base,ref=captures['reference']; same_state=all(m[k]==base[k] for k in ('World tick','Ticks','Scene','Lights','GPU','Player bounds','Camera'))
                if c.get('fixture','normal')!='normal': same_state=same_state and m['Light positions']==base['Light positions']
                metrics,diff=compare(ref,rgb); png(out/(tag+'_diff_x8.png'),1280,720,diff)
                if name=='repeat': control_ok=same_state and metrics['max_channel']==0
                if not same_state or not control_ok: verdict='INCONCLUSIVE_STATE_OR_CONTROL'
                elif group=='hard_cache' and name=='cached' and int(m.get('Static shadow hits','0'))<=0: verdict='INCONCLUSIVE_CACHE_NOT_EXERCISED'
                elif group=='soft_cache_guard' and int(m.get('Static shadow hits','0'))>0: verdict='DIFFERENCES_FOUND'
                elif c.get('fixture')=='static' and c.get('static_fields') and int(m.get('Static hits','0'))<=0: verdict='INCONCLUSIVE_CACHE_NOT_EXERCISED'
                elif kind=='quality': verdict='REVIEW_REQUIRED'
                else: verdict='EXACT_MATCH' if metrics['max_channel']==0 else 'DIFFERENCES_FOUND'
                results.append(dict(group=group,name=name,comparison=kind,state_matches=same_state,reference_repeat_exact=control_ok,verdict=verdict,**metrics))
                print('  '+verdict,flush=True)
                if verdict.startswith('INCONCLUSIVE') or verdict=='DIFFERENCES_FOUND': status=2
    except (OSError,ValueError,KeyError,subprocess.TimeoutExpired) as e:
        results.append(dict(error=str(e))); print('FAILED:',e); status=1
    except KeyboardInterrupt: status=130
    finally:
        (out/'visual_summary.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
        print('Upload this ZIP:',bench.archive(out),flush=True)
    return status
if __name__=='__main__': sys.exit(main())
