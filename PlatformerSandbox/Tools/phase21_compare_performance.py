"""Compare one sweep at a time. Frame time includes pacing; CPU is not GPU time."""
import argparse, csv, math, os, re, statistics
from pathlib import Path

OPT={0:'Reference (all off)',1:'Frame geometry',2:'Layer field reuse',4:'Sample culling',8:'Persistent geometry',16:'Hard GPU mask reuse',31:'All five optimizations'}
DIAG={1:'Lighting off',2:'Shadows off',3:'Bloom off',4:'World particles draw off',5:'Area 1 sample (quality tradeoff)'}
PATTERN=re.compile(r'^(.*)-mode(\d+)-(\w+)-mask(\d+)-isolation(\d+)\.csv$')
def read(path):
    m=PATTERN.match(path.name)
    if not m: return None
    run,mode,quality,mask,iso=m.groups(); mask=int(mask); iso=int(iso)
    with path.open(newline='',encoding='utf-8-sig') as f: rows=list(csv.DictReader(f))
    if not rows: return None
    cfg={(r['width'],r['height'],r['freeze_lights'],r['optimization_mask'],r['isolation']) for r in rows}
    if len(cfg)!=1 or next(iter(cfg))[3:]!=(str(mask),str(iso)):
        print('Skip mixed configuration:',path.name); return None
    w,h,freeze,_,_=next(iter(cfg))
    vals=[float(r['preceding_frame_ms']) for r in rows]
    if any(not math.isfinite(v) or v<=0 for v in vals): return None
    def mean(k): return statistics.mean(float(r[k]) for r in rows)
    data={'ms':statistics.mean(vals),'p95':sorted(vals)[math.ceil(.95*len(vals))-1], 'n':len(vals)}
    for k in ('update_cpu_ms','previous_draw_cpu_ms','layer_cpu_ms','material_cpu_ms','post_cpu_ms','geometry_builds','geometry_reuses','layer_reuses','persistent_hits','hard_mask_hits','pruned_samples','area_sample_evaluations','material_masks','layer_masks'):
        data[k]=mean(k)
    data['geometry_ms']=mean('material_geometry_cpu_ms')+mean('layer_geometry_cpu_ms')
    return (run,mode,quality,w,h,freeze),(mask,iso),data

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('folder',nargs='?',type=Path,default=Path(os.environ.get('LOCALAPPDATA','.'))/'PlatformerSandbox'/'LightingBenchmarks');p.add_argument('--all-runs',action='store_true');a=p.parse_args()
    groups={}
    for f in sorted(a.folder.glob('*.csv')):
        try: result=read(f)
        except (OSError,ValueError,KeyError) as e: print('Skip',f.name,':',e);continue
        if result:
            k,c,d=result;groups.setdefault(k,{})[c]=d
    if not groups:p.exit(1,'No compatible captures. Use Shift+9 in LIGHTING / PERFORMANCE LAB.\n')
    # Never combine different runs or resolutions. Prefer latest full reference sweep.
    keys=sorted(groups)
    if not a.all_runs: keys=[next((k for k in reversed(keys) if (0,0) in groups[k] and (31,0) in groups[k]),keys[-1])]
    for k in keys:
        run,mode,quality,w,h,freeze=k;cases=groups[k];ref=cases.get((0,0));all_on=cases.get((31,0))
        print(f'\nRun {run} | mode {mode} | {quality} | {w}x{h} | frozen lights {freeze}')
        print(f'{"Case":<38} {"FPS":>7} {"ms":>8} {"P95":>8} {"Saved ms":>10} {"CPU draw":>9}')
        for (mask,iso),d in sorted(cases.items(),key=lambda item:(item[0][1],item[0][0])):
            baseline=all_on if iso else ref;delta=f'{baseline["ms"]-d["ms"]:+.2f}' if baseline else 'n/a'
            label=DIAG.get(iso,f'Diagnostic {iso}') if iso else OPT.get(mask,f'Custom mask {mask}')
            print(f'{label:<38} {1000/d["ms"]:7.1f} {d["ms"]:8.2f} {d["p95"]:8.2f} {delta:>10} {d["previous_draw_cpu_ms"]:9.2f}')
            print(f'  CPU geometry/layer/material/post ms {d["geometry_ms"]:.2f}/{d["layer_cpu_ms"]:.2f}/{d["material_cpu_ms"]:.2f}/{d["post_cpu_ms"]:.2f}; samples {d["area_sample_evaluations"]:.1f}; masks {d["material_masks"]+d["layer_masks"]:.1f}; geo/field/persist/GPU hits {d["geometry_reuses"]:.1f}/{d["layer_reuses"]:.1f}/{d["persistent_hits"]:.1f}/{d["hard_mask_hits"]:.1f}; pruned {d["pruned_samples"]:.1f}; n={d["n"]}')
        if ref:
            print('Independent optimizations ranked by observed savings vs reference:')
            for (mask,iso),d in sorted(cases.items(),key=lambda item:item[1]['ms']):
                if iso==0 and mask in (1,2,4,8,16):print(f'  {OPT[mask]}: {ref["ms"]-d["ms"]:+.2f} ms/frame')
        if all_on: print('Feature-off diagnostics are compared against all-on, not against reference. Savings overlap and are not additive.')
    print('\nFixed timestep/VSync may cap FPS. CPU draw is wall-clock submission, not GPU elapsed time. Keep camera, window, input, focus and settings fixed. Repeat sweeps to check noise.')
if __name__=='__main__':main()
