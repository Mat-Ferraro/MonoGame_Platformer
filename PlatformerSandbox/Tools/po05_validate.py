"""PO05 validation: three collision modes, headless scalability and fixed-tick visual comparisons."""
import argparse
from datetime import datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile


def plan(root, tier, suite, headless):
    tests=root/'PlatformerSandbox.Tests/PlatformerSandbox.Tests.csproj'
    game=root/'Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj'
    steps=[]
    if suite!='visual':
        modes=('Reference','Indexed','Verify') if suite in ('all','collision') else ('Reference',)
        for i,mode in enumerate(modes):
            cmd=['dotnet','test',str(tests),'-c','Release']
            if i: cmd+=['--no-build']
            if suite=='spatial': cmd+=['--filter','FullyQualifiedName~PO05SpatialTests']
            if suite=='collision': cmd+=['--filter','FullyQualifiedName~Simulation|FullyQualifiedName~Gameplay|FullyQualifiedName~PO05SpatialTests']
            steps.append(('tests_'+mode.lower(),cmd,mode))
    if not headless and suite in ('all','visual'):
        steps.append(('build',['dotnet','build',str(game),'-c','Release'],'Reference'))
        steps.append(('visual',[sys.executable,str(root/'Tools/po_visual_regression.py'),'--po05','--po05-tier',tier,'--no-build'],'Reference'))
    return steps


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--tier',choices=('quick','standard','full'),default='quick')
    p.add_argument('--suite',choices=('all','spatial','collision','visual'),default='all')
    p.add_argument('--headless',action='store_true');p.add_argument('--list',action='store_true');a=p.parse_args()
    if a.headless and a.suite=='visual':p.error('visual suite requires graphics')
    root=Path(__file__).resolve().parents[1];steps=plan(root,a.tier,a.suite,a.headless)
    if a.list: print(json.dumps(steps,indent=2));return 0
    out=root/'Benchmarks'/('PO05_validation_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f'));out.mkdir(parents=True)
    results=[]; status=0
    try:
        for name,cmd,mode in steps:
            target=out/name;target.mkdir();env=dict(os.environ,PS_PO05_MODE=mode,PS_PO05_TIER=a.tier,PS_PO05_REPORT_DIR=str(target))
            if name.startswith('tests_'):cmd=cmd+['--logger','trx;LogFileName=results.trx','--results-directory',str(target)]
            before=set((root/'Benchmarks').glob('PO_visual_*.zip'))
            print(f'\n{name}: {" ".join(cmd)}',flush=True)
            try:
                with (target/'output.log').open('w',encoding='utf-8') as log:
                    with subprocess.Popen(cmd,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace') as proc:
                        for line in proc.stdout: print(line,end='',flush=True);log.write(line)
                        code=proc.wait()
            except OSError as exc: (target/'output.log').write_text(str(exc));code=1
            if name=='visual':
                archives=set((root/'Benchmarks').glob('PO_visual_*.zip'))-before
                for archive in archives:shutil.copy2(archive,target/archive.name)
                if not archives and code==0:code=1
            results.append(dict(step=name,mode=mode,exit_code=code))
            if code:status=code;break
    except KeyboardInterrupt:status=130
    finally:
        summary=dict(tier=a.tier,suite=a.suite,headless=a.headless,exit_code=status,steps=results,not_run=[n for n,_,_ in steps if n not in {r['step'] for r in results}])
        (out/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
        (out/'summary.md').write_text('# PO05 validation\n\n'+ '\n'.join(f"- {r['step']}: {'PASS' if r['exit_code']==0 else 'FAIL'}" for r in results)+'\n\nNot run: '+', '.join(summary['not_run']),encoding='utf-8')
        archive=out.with_suffix('.zip')
        with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
            for f in sorted(out.rglob('*')):
                if f.is_file():z.write(f,f.relative_to(out))
        print('Validation archive:',archive,flush=True)
    return status

if __name__=='__main__':sys.exit(main())
