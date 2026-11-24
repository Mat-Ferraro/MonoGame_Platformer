#!/usr/bin/env python3
"""Phase 19B isolated material fixture checks: only stdlib required."""
from pathlib import Path
import json, struct, sys
base=Path(__file__).resolve().parent.parent
path=base/'Content/Levels/MaterialShowcaseTest.ldtk'
scene=json.loads(path.read_text(encoding='utf-8'))
assert len(scene['levels']) == 1
level=scene['levels'][0]
assert level['identifier'] == 'MATERIAL_SHOWCASE_TEST'
entities=[e for l in level['layerInstances'] for e in l.get('entityInstances',[])]
assert len({e['iid'] for e in entities}) == len(entities)
art=[e for e in entities if e['__identifier']=='TerrainArtwork']
lights=[e for e in entities if e['__identifier']=='LightSource']
assert len(lights)==3
for light in lights:
  values={f['__identifier']:f['__value'] for f in light['fieldInstances']}
  assert 0 <= values.get('Intensity',1.4) <= 4
fields=lambda a:{f['__identifier']:f['__value'] for f in a['fieldInstances']}
assert sum(bool(fields(a).get('CastsShadows')) for a in art)==2
assert len([a for a in art if fields(a).get('EmissionIntensity',0)>0])==3
assert sorted(round(float(fields(a).get('Opacity',1)),2) for a in art if float(fields(a).get('Opacity',1))<1)==[.5,.5,.6,.7]
assets=0
for a in art:
  f=fields(a)
  for key in ('Image','NormalMap','EdgeMap','EmissionMap'):
   name=f.get(key)
   if not name:continue
   path=(base/'Content/Levels'/name)
   # Some reused PNGs are present in the user's full project, but not in
   # our incremental changes-only archive. Only verify NEW assets here.
   if name.startswith('TerrainArt/LightingTest/L19B_'):
    assert path.is_file(),name
    raw=path.read_bytes()
    assert raw[:8]==b'\x89PNG\r\n\x1a\n',name
    w,h=struct.unpack('>II',raw[16:24]);assert w>0 and h>0
    assets+=1
assert assets >= 8,assets
for name in ('ProductionSurfaceSubmission.cs','GameplayLightingRenderer.cs'):
 assert (base/('PlatformerSandbox.Foundation/Presentation/Lighting' if name.startswith('Production') else 'Source/Lighting')/name).is_file()
print('PASS — 4 stations, 3 local lights, 2 authored shadow casters, 3 emission samples,',assets,'L19B PNG references')
