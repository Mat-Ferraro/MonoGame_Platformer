"""Validate performance lab and benchmark tooling. Does not execute C# or GPU."""
from pathlib import Path
import json, csv, tempfile, subprocess, sys, re
root=Path(__file__).resolve().parents[1]
load=lambda name:json.loads((root/name).read_text(encoding='utf-8-sig'))
a=load('Content/Levels/Phase21LightingLab.ldtk')['levels'][0]
b=load('Content/Levels/LightingPerformanceLab.ldtk')['levels'][0]
assert a['identifier']=='PHASE21_LIGHTING_LAB' and b['identifier']=='LIGHTING_PERFORMANCE_LAB'
assert (a['pxWid'],a['pxHei'])==(b['pxWid'],b['pxHei'])
ents=lambda l:[e for layer in l['layerInstances'] for e in layer['entityInstances']]
x,y=ents(a),ents(b)
assert len(x)==len(y)
assert not ({e['iid'] for e in x} & {e['iid'] for e in y})
for e,f in zip(x,y):
    for key in ('__identifier','px','width','height','fieldInstances'): assert e[key]==f[key], key
    for field in f['fieldInstances']:
        if field['__identifier'] in ('Image','NormalMap','EdgeMap','EmissionMap') and field['__value']:
            assert (root/'Content/Levels'/field['__value']).is_file(),field['__value']
source=(root/'Source/Game1.cs').read_text();renderer=(root/'Source/Lighting/GameplayLightingRenderer.cs').read_text()
for i,flag in enumerate(('ReuseGeometry','ReuseLayerFields','PruneAreaSamples','CacheAcrossFrames','ReuseHardMasks')):
    assert f'{flag} = PerfEnabled({1<<i});' in source and f'public bool {flag}' in renderer
assert 'IsPerformanceLab' in source and 'LIGHTING_PERFORMANCE_LAB' in (root/'PlatformerSandbox.Gameplay/GameFlow/GameSceneCoordinator.cs').read_text()
assert 'LIGHTING / PERFORMANCE LAB' in (root/'PlatformerSandbox.Gameplay/GameFlow/GameUiNavigation.cs').read_text()
assert 'IsFixedTimeStep = true' in source
# Test the actual CSV writer schema against the analysis script with a complete synthetic sweep.
header=re.search(r'File.WriteAllLines\(Path.Combine\(folder,name\),new\[\]\{"([^\"]+)"\}',source).group(1).split(',')
with tempfile.TemporaryDirectory() as t:
    for mask,iso,ms in [(0,0,40),(1,0,35),(2,0,36),(4,0,39),(8,0,31),(16,0,34),(31,0,26),(31,1,17),(31,2,20),(31,3,24),(31,4,25),(31,5,19)]:
        file=Path(t)/f'20261125-000000-000-mode5-High-mask{mask}-isolation{iso}.csv'
        row={k:0 for k in header};row.update(preceding_frame_ms=ms,width=1280,height=720,optimization_mask=mask,isolation=iso,freeze_lights=1)
        with file.open('w',newline='') as f:
            w=csv.DictWriter(f,fieldnames=header);w.writeheader();w.writerows([row]*120)
    result=subprocess.check_output([sys.executable,str(root/'Tools/phase21_compare_performance.py'),t],text=True)
    assert 'Persistent geometry: +9.00 ms/frame' in result and 'All five optimizations' in result and 'Area 1 sample' in result
print('PASS: lab workload parity, independent switches, CSV schema and synthetic sweep analysis. C# / GPU execution still required.')
