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

GPU_REQUIRED_PROJECTS=('Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj',
                       'PlatformerSandbox.Tests/PlatformerSandbox.Tests.csproj',
                       'PlatformerSandbox.Foundation/PlatformerSandbox.Foundation.csproj',
                       'PlatformerSandbox.Gameplay/PlatformerSandbox.Gameplay.csproj',
                       'PlatformerSandbox.Simulation/PlatformerSandbox.Simulation.csproj',
                       'PlatformerSandbox.Diagnostics/PlatformerSandbox.Diagnostics.csproj',
                       'Content/Content.csproj', 'Content/Builder/Builder.cs')
GPU_REQUIRED_EFFECTS=('L03_SurfaceMaterial.fx','L_BASIC_SurfaceLighting.fx','L20_PolygonRim.fx','L21_TangentTransform.fx')

def gpu_missing(root, files):
    rel={p.relative_to(root).as_posix().lower() for p in files}
    effects={p.name.lower() for p in files if p.suffix.lower()=='.fx' and 'content' in p.relative_to(root).parts[0].lower()}
    return [f for f in GPU_REQUIRED_PROJECTS if f.lower() not in rel]+[f for f in GPU_REQUIRED_EFFECTS if f.lower() not in effects]

def main():
    ap=argparse.ArgumentParser(description=__doc__); ap.add_argument('--output',type=Path); ap.add_argument('--require-gpu',action='store_true',help='fail if the known renderer/build inputs are missing'); a=ap.parse_args()
    root=Path(__file__).resolve().parent.parent
    target=(a.output or root.parent/('PlatformerSandbox_Source_'+datetime.now().strftime('%Y%m%d_%H%M%S')+'.zip')).resolve()
    files=list(collect(root)); counts={ext:sum(p.suffix.lower()==ext for p in files) for ext in sorted(EXTS)}
    missing=gpu_missing(root,files)
    if a.require_gpu and missing:
        ap.error('Missing GPU source inputs: '+', '.join(missing))
    if not counts['.csproj'] or not counts['.fx']:
        print('WARNING: no .csproj or .fx files found; shader development may remain blocked.')
    target.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as z:
        for p in files: z.write(p,p.relative_to(root).as_posix())
        z.writestr('SOURCE_EXPORT_MANIFEST.json',json.dumps({'extension_counts':counts,'gpu_required_missing':missing,'files':[p.relative_to(root).as_posix() for p in files]},indent=2))
    print(f'{len(files)} files | {counts[".fx"]} FX | {counts[".csproj"]} projects | {counts[".mgcb"]} MGCB')
    print('Review ZIP for any sensitive project-specific information before uploading: '+str(target))
if __name__=='__main__': main()
