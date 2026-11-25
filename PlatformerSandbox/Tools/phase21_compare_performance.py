"""Compare complete captures within one run/configuration; supports old and G2 CSVs."""
import argparse, csv, math, os, re, statistics as st
from pathlib import Path

OPT={0:'Reference (all off)',1:'Frame geometry',2:'Layer field reuse',4:'Sample culling',8:'Persistent geometry',16:'Hard GPU mask reuse',32:'Shared GPU masks',64:'Persistent GPU masks',31:'All five optimizations',127:'All seven optimizations'}
DIAG={1:'Lighting off',2:'Shadows off',3:'Bloom off',4:'World particles draw off',5:'Area 1 sample (quality tradeoff)',6:'Developer HUD off',7:'Lighting and world particles off',8:'World drawing off'}
PATTERN=re.compile(r'^(.*)-mode(\d+)-(\w+)-mask(\d+)-isolation(\d+)(?:-case(\d+))?\.csv$')

def percentile(values, p): return sorted(values)[max(0,math.ceil(p*len(values))-1)]

def read(path):
    match=PATTERN.fullmatch(path.name)
    if not match: return None
    run,mode,quality,mask,iso,case=match.groups();mask=int(mask);iso=int(iso)
    with path.open(newline='',encoding='utf-8-sig') as f: rows=list(csv.DictReader(f))
    if not rows: return None
    cfg={(r['width'],r['height'],r['freeze_lights'],r['optimization_mask'],r['isolation'],r.get('fixed_step','legacy'),r.get('vsync','legacy')) for r in rows}
    if len(cfg)!=1 or next(iter(cfg))[3:5]!=(str(mask),str(iso)):
        print('Skip mixed configuration:',path.name);return None
    w,h,freeze,_,_,fixed,vsync=next(iter(cfg))
    vals=[float(r['preceding_frame_ms']) for r in rows]
    if any(not math.isfinite(v) or v<=0 for v in vals): raise ValueError('invalid interval')
    data={'ms':st.mean(vals),'p95':percentile(vals,.95),'p99':percentile(vals,.99),'n':len(vals),'case':int(case or 0)}
    for k in rows[0]:
        if k in ('frame','preceding_frame_ms','width','height','freeze_lights','optimization_mask','isolation','fixed_step','vsync'):continue
        nums=[float(r[k]) for r in rows]
        if any(not math.isfinite(v) for v in nums):raise ValueError('invalid '+k)
        data[k]=st.mean(nums)
    data['geometry_ms']=data.get('material_geometry_cpu_ms',0)+data.get('layer_geometry_cpu_ms',0)
    data['unattributed_ms']=data['ms']-data.get('update_cpu_ms',0)-data.get('previous_draw_cpu_ms',0)
    return (run,mode,quality,w,h,freeze,fixed,vsync),(mask,iso),data

def med(captures,k):
    vals=[d[k] for d in captures if k in d]
    return st.median(vals) if vals else None

def fmt(captures,k,places=2):
    v=med(captures,k)
    return 'n/a' if v is None else f'{v:.{places}f}'

def full_mask(cases):return next((m for m in (127,31) if (m,0) in cases),None)

def matched_savings(captures, baselines):
    """Use nearest bracketing reference means; interpolate by case order, not wall time."""
    if not baselines:return None
    deltas=[]
    for c in captures:
        before=[b for b in baselines if b['case']<=c['case']]
        after=[b for b in baselines if b['case']>=c['case']]
        l=max(before,key=lambda b:b['case']) if before else None
        r=min(after,key=lambda b:b['case']) if after else None
        if l and r and r['case']!=l['case']:
            t=(c['case']-l['case'])/(r['case']-l['case']);base=l['ms']+(r['ms']-l['ms'])*t
        elif l or r:base=(l or r)['ms']
        else:base=st.median(b['ms'] for b in baselines)
        deltas.append(base-c['ms'])
    return st.median(deltas)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('folder',nargs='?',type=Path,default=Path(os.environ.get('LOCALAPPDATA','.'))/'PlatformerSandbox'/'LightingBenchmarks');p.add_argument('--all-runs',action='store_true');a=p.parse_args()
    groups={}
    for f in sorted(a.folder.glob('*.csv')):
        try: result=read(f)
        except (OSError,ValueError,KeyError,TypeError) as e:print('Skip',f.name,':',e);continue
        if result:
            k,c,d=result;groups.setdefault(k,{}).setdefault(c,[]).append(d)
    if not groups:p.exit(1,'No compatible captures. Use RUN BENCHMARK SWEEP in LIGHTING / PERFORMANCE LAB.\n')
    keys=sorted(groups)
    if not a.all_runs:keys=[next((k for k in reversed(keys) if (0,0) in groups[k] and full_mask(groups[k]) is not None),keys[-1])]
    for k in keys:
        run,mode,quality,w,h,freeze,fixed,vsync=k;cases=groups[k];ref=cases.get((0,0),[]);all_on=cases.get((full_mask(cases),0),[])
        print(f'\nRun {run} | mode {mode} | {quality} | {w}x{h} | frozen lights {freeze} | fixed framework {fixed} | requested VSync {vsync}')
        print('Median of capture means; P95/P99 are medians of per-capture percentiles. Savings use nearby reference cases.')
        print(f'{"Case":<38} {"FPS":>7} {"ms":>8} {"P95":>8} {"P99":>8} {"Saved ms":>10} {"Runs":>5}')
        ranks=[]
        for (mask,iso),ds in sorted(cases.items(),key=lambda item:(item[0][1],item[0][0])):
            base=all_on if iso else ref; delta=matched_savings(ds,base)
            label=DIAG.get(iso,f'Diagnostic {iso}') if iso else OPT.get(mask,f'Custom mask {mask}')
            print(f'{label:<38} {1000/med(ds,"ms"):7.1f} {med(ds,"ms"):8.2f} {med(ds,"p95"):8.2f} {med(ds,"p99"):8.2f} {f"{delta:+.2f}" if delta is not None else "n/a":>10} {len(ds):5}')
            print(f'  Capture range {min(d["ms"] for d in ds):.2f}–{max(d["ms"] for d in ds):.2f} ms; frames {sum(d["n"] for d in ds)}; update/draw/unattributed {fmt(ds,"update_cpu_ms")}/{fmt(ds,"previous_draw_cpu_ms")}/{fmt(ds,"unattributed_ms")} ms')
            print(f'  CPU geometry/layer/material/post ms {fmt(ds,"geometry_ms")}/{fmt(ds,"layer_cpu_ms")}/{fmt(ds,"material_cpu_ms")}/{fmt(ds,"post_cpu_ms")}; samples {fmt(ds,"area_sample_evaluations",1)}; masks {med(ds,"material_masks")+med(ds,"layer_masks"):.1f}; geo/field/persist/GPU hits {fmt(ds,"geometry_reuses",1)}/{fmt(ds,"layer_reuses",1)}/{fmt(ds,"persistent_hits",1)}/{fmt(ds,"hard_mask_hits",1)}')
            if 'shared_mask_hits' in ds[0]:
                print(f'  Shared/persistent/miss/fallback {fmt(ds,"shared_mask_hits",1)}/{fmt(ds,"persistent_mask_hits",1)}/{fmt(ds,"shared_mask_misses",1)}/{fmt(ds,"shared_mask_fallbacks",1)}; pool {fmt(ds,"shared_mask_targets",1)}; draws/target changes {fmt(ds,"draw_calls",1)}/{fmt(ds,"target_changes",1)}; alloc {fmt(ds,"thread_alloc_bytes",0)} B/frame; GC 0/1/2 {fmt(ds,"gc0",3)}/{fmt(ds,"gc1",3)}/{fmt(ds,"gc2",3)}')
                print(f'  Simulation/audio/particle-update/light-update/diagnostics/events {fmt(ds,"simulation_cpu_ms")}/{fmt(ds,"audio_cpu_ms")}/{fmt(ds,"particle_update_cpu_ms")}/{fmt(ds,"light_update_cpu_ms")}/{fmt(ds,"diagnostic_cpu_ms")}/{fmt(ds,"event_cpu_ms")} ms; scene draw (inclusive)/particle draw (nested)/light prepare/debug HUD {fmt(ds,"scene_draw_cpu_ms")}/{fmt(ds,"particle_draw_cpu_ms")}/{fmt(ds,"light_prepare_cpu_ms")}/{fmt(ds,"debug_hud_cpu_ms")} ms')
            if 'drawn_particles' in ds[0]:print(f'  Camera/animation {fmt(ds,"camera_animation_cpu_ms")} ms; particles drawn {fmt(ds,"drawn_particles",1)}; world/UI effects {fmt(ds,"active_world_effects",1)}/{fmt(ds,"active_ui_effects",1)}')
            if not iso and mask in (1,2,4,8,16,32,64) and delta is not None:ranks.append((delta,label))
        if ranks:
            print('Independent optimizations ranked by observed savings vs reference:')
            for delta,label in sorted(ranks,reverse=True):print(f'  {label}: {delta:+.2f} ms/frame')
        if len(ref)>1:print(f'Reference drift range: {min(d["ms"] for d in ref):.2f}–{max(d["ms"] for d in ref):.2f} ms. Inspect repeat ranges before accepting a speedup.')
        if all_on:print('Feature-off diagnostics compared against nearby all-on captures. Savings overlap; not additive.')
    print('\nUnattributed interval includes presentation/pacing, driver/GPU waiting and other uninstrumented work; it is NOT GPU time. CPU stage timings can be nested. Keep camera, focus, input and settings fixed.')
if __name__=='__main__':main()
