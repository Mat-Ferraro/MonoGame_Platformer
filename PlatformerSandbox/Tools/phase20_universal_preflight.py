"""Portable fixture/source preflight; does not compile C# or execute a GPU."""
from pathlib import Path
import json, math, struct
root = Path(__file__).resolve().parents[1]
lab = json.loads((root/'Content/Levels/LightTypesLab.ldtk').read_text(encoding='utf-8-sig'))
level = lab['levels'][0]
assert level['identifier'] == 'LIGHT_TYPES_LAB'
entities = [e for layer in level['layerInstances'] for e in layer.get('entityInstances', [])]
assert len({e['iid'] for e in entities}) == len(entities)
defs = {d['identifier']: {f['identifier']: f for f in d['fieldDefs']} for d in lab['defs']['entities']}
shapes = {}
lights = {}
for e in entities:
    fs = {f['__identifier']: f for f in e['fieldInstances']}
    assert len(fs) == len(e['fieldInstances'])
    for name in ('ShadowShape', 'ShadowVertices'):
        if name in fs:
            assert fs[name]['defUid'] == defs[e['__identifier']][name]['uid']
    f = {k: v['__value'] for k,v in fs.items()}
    if e['__identifier'] == 'LightSource': lights[f['LightRole']] = f
    if f.get('ShadowShape') != 'Custom': continue
    contours = [[tuple(map(float, pt.split(','))) for pt in c.split(';')] for c in f['ShadowVertices'].split('|')]
    assert sum(map(len,contours)) <= 128
    for v in contours:
        assert 3 <= len(v) <= 64 and len(set(v)) == len(v)
        assert all(len(pt)==2 and all(math.isfinite(x) and 0 <= x <= 1 for x in pt) for pt in v)
        area = sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(v,v[1:]+v[:1]))
        assert abs(area)>1e-8
    assert f['CastsShadows'] and f['MaterialShading'] != 'Legacy'
    target = root/'Content/Levels'/f['Image']
    raw=target.read_bytes()
    assert raw[:8]==b'\x89PNG\r\n\x1a\n'
    assert struct.unpack('>II',raw[16:24]) == (e['width'],e['height'])
    shapes[target.stem] = (contours,f)
assert set(lights)=={'SUN','MOON','POINT','SPOT','TUBE'}
assert all(0 < f['Intensity'] < 1 for f in lights.values())
assert len(shapes)==7
assert any(len(c)==2 for c,f in shapes.values())
assert any(len(v)==16 for c,f in shapes.values() for v in c)
assert any(f.get('BlockerMotionAxis')=='Horizontal' and f.get('BlockerRotationMode')=='Clockwise' for c,f in shapes.values())
assert any(f.get('Opacity')<1 for c,f in shapes.values())
config=json.loads((root/'Content/Lighting/L456_GameplayLighting.json').read_text())
assert config['defaultPreset']=='Illustrated'
assert next(s for s in config['scenePresets'] if s['sceneId']=='LIGHT_TYPES_LAB')['defaultPreset']=='LightTypesNeutral'
shader=(root/'Content/Assets/Effects/L20_PolygonRim.fx').read_text()
renderer=(root/'Source/Lighting/GameplayLightingRenderer.cs').read_text()
builder=(root/'Content/Builder/Builder.cs').read_text()
game=(root/'Source/Game1.cs').read_text()
assert 'L20_PolygonRim.fx' in builder and 'Effects/L20_PolygonRim' in game
for name in ('PolygonEdges','VisibilityMap','ViewportSize','EdgeCount','LightPositionRadius','LightShapeDirection','LightExtra','LightEnergy','RimSettings','InsideCenter'):
    assert name in shader and f'Parameters["{name}"]' in renderer,name
assert 'ProductionWallLightRouting.OwnWalls(' in renderer
assert 'BindProductionWallShader(' not in renderer
assert 'productionGain: art.LightMultiplier' in renderer
assert 'AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One' in renderer
assert 'int wallRimShadowSlot = -1, double productionGain = 1' in renderer
print('PASS: seven polygon witnesses and PNG dimensions; owner/edge budgets; five tuned lights; neutral preset; shader/renderer bindings.')
print('NOTE: this is static preflight only; run .NET tests and the GPU lab separately.')
