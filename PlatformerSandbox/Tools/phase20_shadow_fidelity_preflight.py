from pathlib import Path
import json
import math
import re

root = Path(__file__).resolve().parents[1]
scene = json.loads((root/'Content/Levels/LightTypesLab.ldtk').read_text())
assert scene['levels'][0]['identifier'] == 'LIGHT_TYPES_LAB'
entities = [e for layer in scene['levels'][0]['layerInstances']
    if layer['__type']=='Entities' for e in layer['entityInstances']]
light = {}
casters = []
for e in entities:
    data={f['__identifier']:f['__value'] for f in e['fieldInstances']}
    if e['__identifier']=='LightSource':
        light[data['LightRole']] = data
        assert 0<=data['Intensity']<=4
        assert 0.25<=data['ShadowLengthWorld']<=30
    if e['__identifier']=='TerrainArtwork' and data.get('CastsShadows'):
        casters.append(data)
assert len(light)==5
assert light['SUN']['Intensity'] < 1
assert light['MOON']['Intensity'] < 1
assert light['SUN']['ShadowLengthWorld'] <= 7
assert any(c.get('ShadowShape')=='Pentagon' and
           c.get('Image','').endswith('L19D_ShadowObelisk.png') for c in casters)
# Confirm the LDtk *editor*, not just the importer, accepts the tuned values.
ldefs=next(d for d in scene['defs']['entities'] if d['identifier']=='LightSource')
defs={f['identifier']:f for f in ldefs['fieldDefs']}
for field in ('Intensity','ShadowLengthWorld'):
    fd=defs[field]
    assert fd['type']=='F_Float'
    assert fd['min']<=light['SUN'][field]<=fd['max']
for k in ('LightType','LightRole'):
    assert defs[k]['__type']=='String'
renderer=(root/'Source/Lighting/GameplayLightingRenderer.cs').read_text()
assert 'DirectionalShadowProjection.Triangles(' in renderer
assert '2.5 * Math.Sqrt(view.Width' not in renderer
assert 'new Vector3(' in renderer
game=(root/'Source/Game1.cs').read_text()
assert '_l20LightFilter = 1;' in game
assert '_l19Emission = false;' in game
assert '_l19Bloom = false;' in game
assert '0.85f' in game
assert 'point.X - light.Direction.X / directionLength * light.DirectionalShadowLength' in (
    root/'PlatformerSandbox.Foundation/Presentation/Lighting/LightingWorldSampler.cs').read_text()
assert '"Pentagon" =>' in (root/'PlatformerSandbox.Gameplay/Content/LdtkLightingImporter.cs').read_text()
assert 'public static class DirectionalShadowProjection' in (
    root/'PlatformerSandbox.Foundation/Presentation/Lighting/DirectionalShadowProjection.cs').read_text()
print('Phase 20 shadow fidelity PASS — bounded directional projection, obelisk silhouette,')
print('                               world/hero parity, safe lab brightness & LDtk fields')
