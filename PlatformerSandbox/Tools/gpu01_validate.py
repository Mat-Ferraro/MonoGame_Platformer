"""One-command GPU01 verification. Runs existing tests and capture tools, not native GPU timing."""
import argparse
from datetime import datetime
import json
from pathlib import Path
import subprocess
import shutil
import sys
import po_run_benchmarks as bench


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--tier', choices=('quick','standard','full'), default='quick')
    p.add_argument('--repeat', type=int, default=1)
    p.add_argument('--no-tests', action='store_true', help='skip C# regression tests, still build Release')
    p.add_argument('--gpu-telemetry', action='store_true', help='diagnostic run; device-wide NVIDIA telemetry')
    p.add_argument('--refine',action='store_true')
    p.add_argument('--expect-adapter')
    p.add_argument('--diagnostics',action='store_true',help='separate instrumented refinement benchmark')
    a=p.parse_args()
    if a.repeat<1: p.error('--repeat must be >=1')
    root=Path(__file__).resolve().parents[1]
    tests=root/'PlatformerSandbox.Tests/PlatformerSandbox.Tests.csproj'
    game=root/'Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj'
    if not game.is_file() or not tests.is_file(): p.error('run from a complete solution; project files missing')
    out=root/'Benchmarks'/('GPU01_validation_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    out.mkdir(parents=True)
    steps=[]
    if not a.no_tests: steps.append(('tests',['dotnet','test',str(tests),'-c','Release']))
    steps.append(('build',['dotnet','build',str(game),'-c','Release']))
    if a.refine:
        adapter=['--expect-adapter',a.expect_adapter] if a.expect_adapter else []
        perf=[sys.executable,str(root/'Tools/po_run_benchmarks.py'),'--gpu01-refine','--tier',a.tier,'--no-build']+adapter
        steps.append(('refinement_performance',perf+['--repeat',str(a.repeat)]))
        steps.append(('refinement_visual',[sys.executable,str(root/'Tools/po_visual_regression.py'),'--gpu01-refine','--no-build']+adapter))
        if a.diagnostics or a.gpu_telemetry:
            steps.append(('refinement_diagnostics',perf+['--gpu-timing','--gpu-telemetry']))
    else:
        steps.append(('performance',[sys.executable,str(root/'Tools/po_run_benchmarks.py'),'--gpu01','--tier',a.tier,'--repeat',str(a.repeat),'--no-build']+(['--gpu-telemetry'] if a.gpu_telemetry else [])))
        steps.append(('visual',[sys.executable,str(root/'Tools/po_visual_regression.py'),'--gpu01','--no-build']+(['--gpu-quick'] if a.tier=='quick' else [])))
        steps.append(('expanded_performance',[sys.executable,str(root/'Tools/po_run_benchmarks.py'),'--gpu01-expanded','--tier',a.tier,'--repeat',str(a.repeat),'--no-build']+(['--gpu-telemetry'] if a.gpu_telemetry else [])))
        steps.append(('expanded_visual',[sys.executable,str(root/'Tools/po_visual_regression.py'),'--gpu01-expanded','--no-build']+(['--gpu-quick'] if a.tier=='quick' else [])))
    prior_archives=set((root/'Benchmarks').glob('*.zip'))
    results=[]; status=0
    try:
        for name,cmd in steps:
            if status and name!='tests' and any(r['step'] in ('tests','build') and r['returncode']!=0 for r in results): break
            print('=== '+name+' ===',flush=True)
            with (out/(name+'.log')).open('w',encoding='utf-8') as log:
                try:
                    # Forward output in real time; existing runners retain their own ZIPs.
                    with subprocess.Popen(cmd,cwd=root,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace') as proc:
                        for line in proc.stdout:
                            print(line,end='',flush=True);log.write(line);log.flush()
                        rc=proc.wait()
                except OSError as e:
                    log.write(str(e));rc=1
            results.append(dict(step=name,returncode=rc,command=cmd)); status=max(status,1 if rc else 0)
    except KeyboardInterrupt:
        status=130
    finally:
        # Aggregate only archives generated during this invocation; retain original children.
        for artifact in sorted(set((root/'Benchmarks').glob('*.zip'))-prior_archives):
            if artifact.name.startswith(('PO_auto_','PO_visual_')):
                shutil.copy2(artifact,out/artifact.name)
        (out/'summary.json').write_text(json.dumps(dict(tier=a.tier,repeat=a.repeat,steps=results),indent=2),encoding='utf-8')
        print('Validation log ZIP:',bench.archive(out),flush=True)
    return status
if __name__=='__main__':sys.exit(main())
