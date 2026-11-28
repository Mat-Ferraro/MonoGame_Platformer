"""PB01 correctness and opt-in integrated performance baselines. Standard library only."""
import argparse
import csv
import math
from datetime import datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile


def plan(root, tier, headless=False, no_bench=False, bench_only=False, repeats=None, allow_battery=False):
    tests=root/'PlatformerSandbox.Tests/PlatformerSandbox.Tests.csproj'
    game=root/'Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj'
    steps=[]
    if not bench_only:
        for i,mode in enumerate(('Reference','Indexed','Verify')):
            cmd=['dotnet','test',str(tests),'-c','Release']
            if i: cmd+=['--no-build']
            steps.append(('tests_'+mode.lower(),cmd,mode,None))
    if not headless:
        steps.append(('build',['dotnet','build',str(game),'-c','Release'],'Reference',None))
        if not bench_only:
            steps.append(('feature_visual',[sys.executable,str(root/'Tools/po_visual_regression.py'),'--pb01','--po05-tier',tier,'--no-build'],'Reference','PO_visual_*.zip'))
        if not no_bench:
            cmd=[sys.executable,str(root/'Tools/po_run_benchmarks.py'),'--pb01-baseline','--tier',tier,'--repeat',str(repeats or (1 if tier=='quick' else 3)),'--no-build']
            if allow_battery: cmd+=['--allow-battery']
            steps.append(('integrated_benchmark',cmd,'Reference','PO_auto_*.zip'))
    return steps



def validate_closeout_evidence(target, tier, mode):
    """Reject missing, partial, stale-mode or non-finite acceptance CSVs."""
    replay_ticks={'quick':600,'standard':3000,'full':10000}[tier]
    scale_ticks={'quick':120,'standard':300,'full':600}[tier]
    def rows(name):
        with (target/name).open(newline='',encoding='utf-8') as f:
            return list(csv.DictReader(f))
    def numeric(row, keys):
        for key in keys:
            x=float(row[key])
            if not math.isfinite(x) or x<0:raise ValueError(f'invalid {key}: {x}')
    for level in ('PHYSICS_INTERACTION_LAB','ENGINE_INTEGRATION_LAB'):
        r=rows(level+'_closeout_replay.csv')
        if len(r)!=1:raise ValueError(f'{level}: expected one replay row')
        row=r[0]
        if (row['level'],row['mode'],int(row['ticks']))!=(level,mode,replay_ticks):raise ValueError(f'{level}: wrong replay configuration')
        numeric(row,('mean_world_step_ms','p95_world_step_ms','allocated_bytes_per_tick','committed_dynamic_cpu_ms_per_tick','platform_stops','recovery_attempts'))
        if min(int(row[k]) for k in ('contacts','platform_contacts','committed_coupled_event_iterations','deaths','respawns'))<=0:raise ValueError(f'{level}: replay workload not exercised')
    scale=rows('PB01_closeout_scaling.csv')
    if len(scale)!=3 or {int(r['body_count']) for r in scale}!={8,32,64}:raise ValueError('wrong body scaling cases')
    for row in scale:
        if row['mode']!=mode or int(row['ticks'])!=scale_ticks:raise ValueError('wrong scaling configuration')
        numeric(row,('mean_step_ms','p95_step_ms','allocated_bytes_per_tick'))
        if min(int(row[k]) for k in ('contacts','pair_contacts','committed_coupled_event_iterations'))<=0:raise ValueError('scaling workload not exercised')

def write_closeout_comparison(out):
    """Same workload/machine comparison; Verify is a diagnostic cost, not a shipping preset."""
    samples={}
    for mode in ('Reference','Indexed','Verify'):
        target=out/('tests_'+mode.lower())
        for filename in ('PHYSICS_INTERACTION_LAB_closeout_replay.csv','ENGINE_INTEGRATION_LAB_closeout_replay.csv','PB01_closeout_scaling.csv'):
            with (target/filename).open(newline='',encoding='utf-8') as f:
                for row in csv.DictReader(f):
                    name=row.get('level') or 'scaling_'+row['body_count']
                    samples.setdefault(name,{})[mode]=row
    with (out/'PB01_closeout_comparison.csv').open('w',newline='',encoding='utf-8') as f:
        w=csv.writer(f);w.writerow(['workload','mode','ticks','mean_ms','p95_ms','allocated_bytes_per_tick','mean_ratio_vs_reference'])
        for name,modes in sorted(samples.items()):
            def mean(r):return float(r.get('mean_world_step_ms') or r['mean_step_ms'])
            baseline=mean(modes['Reference'])
            for mode,row in modes.items():
                w.writerow([name,mode,row['ticks'],mean(row),row.get('p95_world_step_ms') or row['p95_step_ms'],row['allocated_bytes_per_tick'],mean(row)/baseline if baseline>0 else ''])


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--tier',choices=('quick','standard','full'),default='quick')
    p.add_argument('--headless',action='store_true',help='run C# tests and fixed-tick measurements only')
    p.add_argument('--no-bench',action='store_true',help='skip optional graphical benchmark, still run feature visual regression')
    p.add_argument('--bench-only',action='store_true',help='build and run only the integrated graphical baseline')
    p.add_argument('--repeat',type=int,choices=range(1,6),help='graphical repeats (quick default 1; others 3)')
    p.add_argument('--allow-battery',action='store_true',help='explicitly collect a battery baseline')
    p.add_argument('--list',action='store_true')
    a=p.parse_args()
    if a.bench_only and (a.headless or a.no_bench):p.error('--bench-only requires graphics and benchmark')
    root=Path(__file__).resolve().parents[1]
    steps=plan(root,a.tier,a.headless,a.no_bench,a.bench_only,a.repeat,a.allow_battery)
    if a.list:print(json.dumps(steps,indent=2));return 0
    out=root/'Benchmarks'/('PB01_validation_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f'));out.mkdir(parents=True)
    results=[];status=0
    try:
        for name,cmd,mode,pattern in steps:
            target=out/name;target.mkdir()
            # Fresh process preferences and scoped spatial modes. Preserve system PATH, SDK and GPU environment.
            env={k:v for k,v in os.environ.items() if not k.upper().startswith(('PS_PO_','PS_PO05_','PS_PB01_'))}
            env.update(PS_PO05_MODE=mode,PS_PO05_TIER=a.tier,PS_PB01_TIER=a.tier,PS_PB01_REPORT_DIR=str(target),PS_PO05_REPORT_DIR=str(target))
            if name.startswith('tests_'):cmd=cmd+['--logger','trx;LogFileName=results.trx','--results-directory',str(target)]
            before=set((root/'Benchmarks').glob(pattern)) if pattern else set()
            print(f'\n{name}: {" ".join(cmd)}',flush=True)
            try:
                with (target/'output.log').open('w',encoding='utf-8') as log:
                    with subprocess.Popen(cmd,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace') as proc:
                        for line in proc.stdout:print(line,end='',flush=True);log.write(line)
                        code=proc.wait()
            except OSError as exc:(target/'output.log').write_text(str(exc),encoding='utf-8');code=1
            archives=set((root/'Benchmarks').glob(pattern))-before if pattern else set()
            for archive in archives:shutil.copy2(archive,target/archive.name)
            if pattern and not archives and code==0:code=1
            if name.startswith('tests_') and code==0:
                # Require real evidence, not a successful zero-test run.
                if not all((target/n).is_file() for n in ('results.trx','PHYSICS_INTERACTION_LAB_fixed_ticks.csv','ENGINE_INTEGRATION_LAB_fixed_ticks.csv','PHYSICS_INTERACTION_LAB_dynamic_ticks.csv','ENGINE_INTEGRATION_LAB_dynamic_ticks.csv','PB01_character_prop_ticks.csv')):code=1
                if code==0:
                    try:validate_closeout_evidence(target,a.tier,mode)
                    except (OSError,ValueError,KeyError) as exc:
                        (target/'closeout_evidence_error.txt').write_text(str(exc),encoding='utf-8');print('Closeout evidence:',exc);code=1
            results.append(dict(step=name,mode=mode,exit_code=code,archives=sorted(p.name for p in archives)))
            if code:status=code;break
    except KeyboardInterrupt:status=130
    finally:
        if all(any(r['step']=='tests_'+mode and r['exit_code']==0 for r in results) for mode in ('reference','indexed','verify')):
            try:write_closeout_comparison(out)
            except (OSError,ValueError,KeyError) as exc:
                (out/'comparison_error.txt').write_text(str(exc),encoding='utf-8');status=1
        summary=dict(tier=a.tier,headless=a.headless,benchmark_only=a.bench_only,exit_code=status,automated_closeout_candidate=(status==0 and a.tier=='full' and not (a.headless or a.no_bench or a.bench_only)),manual_acceptance_required=True,steps=results,not_run=[n for n,_,_,_ in steps if n not in {r['step'] for r in results}])
        (out/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
        (out/'summary.md').write_text('# PB01 validation\n\n'+ '\n'.join(f"- {r['step']}: {'PASS' if r['exit_code']==0 else 'FAIL'}" for r in results)+'\n\nNot run: '+', '.join(summary['not_run'])+'\n',encoding='utf-8')
        archive=out.with_suffix('.zip')
        with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
            for f in sorted(out.rglob('*')):
                if f.is_file():z.write(f,f.relative_to(out))
        print('Validation archive:',archive,flush=True)
    return status

if __name__=='__main__':sys.exit(main())
