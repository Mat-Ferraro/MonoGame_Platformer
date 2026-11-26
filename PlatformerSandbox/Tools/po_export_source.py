"""Create a source-only ZIP for renderer review. Does not include artwork/binaries."""
import argparse, json, os, zipfile
from datetime import datetime
from pathlib import Path

EXTS={'.cs','.fx','.fxh','.hlsl','.glsl','.vert','.frag','.csproj','.props','.targets','.sln','.slnx','.mgcb','.ldtk','.py','.ps1','.md','.json','.config','.manifest'}
SKIP={'.git','.vs','.idea','.vscode','bin','obj','debug','release','benchmarks','__pycache__','.venv','packages','testresults','.pytest_cache','build','dist'}

def collect(root):
    for folder,dirs,files in os.walk(root):
        dirs[:]=sorted(d for d in dirs if d.lower() not in SKIP and not (Path(folder)/d).is_symlink())
        for name in sorted(files):
            p=Path(folder)/name
            if p.is_symlink() or p.suffix.lower() not in EXTS: continue
            if name.lower().startswith(('secrets.','credentials.','appsettings.','launchsettings.')): continue
            yield p

def main():
    ap=argparse.ArgumentParser(description=__doc__); ap.add_argument('--output',type=Path); a=ap.parse_args()
    root=Path(__file__).resolve().parent.parent
    target=(a.output or root.parent/('PlatformerSandbox_Source_'+datetime.now().strftime('%Y%m%d_%H%M%S')+'.zip')).resolve()
    files=list(collect(root)); counts={ext:sum(p.suffix.lower()==ext for p in files) for ext in sorted(EXTS)}
    if not counts['.csproj'] or not counts['.fx']:
        print('WARNING: no .csproj or .fx files found; shader development may remain blocked.')
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as z:
        for p in files: z.write(p,p.relative_to(root).as_posix())
        z.writestr('SOURCE_EXPORT_MANIFEST.json',json.dumps({'extension_counts':counts,'files':[p.relative_to(root).as_posix() for p in files]},indent=2))
    print(f'{len(files)} files | {counts[".fx"]} FX | {counts[".csproj"]} projects | {counts[".mgcb"]} MGCB')
    print('Review ZIP for any sensitive project-specific information before uploading: '+str(target))
if __name__=='__main__': main()
