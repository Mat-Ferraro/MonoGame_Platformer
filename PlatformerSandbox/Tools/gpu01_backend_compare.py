"""Independent backend build/run experiments; never modifies the production solution."""
import argparse,json,subprocess,sys,shutil
from pathlib import Path
from datetime import datetime
import po_run_benchmarks as bench

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--backends',nargs='+',choices=('DesktopGL','WindowsDX','WindowsDX12','DesktopVK'),default=['DesktopGL','WindowsDX','WindowsDX12','DesktopVK'])
    p.add_argument('--build-only',action='store_true')
    p.add_argument('--expect-adapter')
    a=p.parse_args(); adapter=['--expect-adapter',a.expect_adapter] if a.expect_adapter else []; root=Path(__file__).resolve().parents[1]
    out=root/'Benchmarks'/('GPU01_backends_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f'));out.mkdir(parents=True)
    records=[]
    prior=set((root/'Benchmarks').glob('*.zip'))
    try:
        for name in a.backends:
            project=root/'Platforms'/name/f'PlatformerSandbox.{name}.csproj'
            cmds=[('build',['dotnet','build',str(project),'-c','Release'])]
            if not a.build_only:
                cmds.append(('reference',[sys.executable,str(root/'Tools/po_run_benchmarks.py'),'--gpu01-refine','--tier','full','--case','gx_compare_reference','--case','gx_compare_shaders','--warmup-frames','60','--recorded-frames','120','--no-build','--project',str(project)]+adapter))
                cmds.append(('visual',[sys.executable,str(root/'Tools/po_visual_regression.py'),'--gpu01-compare','--no-build','--project',str(project)]+adapter))
            for phase,cmd in cmds:
                print(name,phase,flush=True)
                with (out/f'{name}_{phase}.log').open('w',encoding='utf-8') as log:
                    try: rc=subprocess.call(cmd,cwd=root,stdout=log,stderr=subprocess.STDOUT)
                    except OSError as e:log.write(str(e));rc=1
                records.append({'backend':name,'phase':phase,'returncode':rc})
                if phase=='build' and rc:break
    finally:
        for item in set((root/'Benchmarks').glob('*.zip'))-prior:
            if item.name.startswith(('PO_auto_','PO_visual_')): shutil.copy2(item,out/item.name)
        (out/'backend_status.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
        print('Backend results:',bench.archive(out))
    return int(any(r['returncode'] for r in records))
if __name__=='__main__':sys.exit(main())
