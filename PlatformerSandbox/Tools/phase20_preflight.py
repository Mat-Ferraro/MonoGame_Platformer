from pathlib import Path
import struct
import json, math, re, hashlib, zipfile, sys
root = Path(__file__).resolve().parents[1]
scene = root / 'Content/Levels/LightTypesLab.ldtk'
legacy = root / 'Content/Levels/MaterialShowcaseTest.ldtk'
assert scene.is_file() and legacy.is_file()
lab = json.loads(scene.read_text())
showcase = json.loads(legacy.read_text())
assert lab['levels'][0]['identifier'] == 'LIGHT_TYPES_LAB'
assert showcase['levels'][0]['identifier'] == 'MATERIAL_SHOWCASE_TEST'
entities = [e for layer in lab['levels'][0]['layerInstances'] if layer['__type']=='Entities'
    for e in layer['entityInstances']]
lights = []
art = []
for e in entities:
    fields = {f['__identifier']: f['__value'] for f in e['fieldInstances']}
    if e['__identifier']=='LightSource':
        lights.append(fields)
    if e['__identifier']=='TerrainArtwork':
        art.append(fields)
for role,kind in [('SUN','Directional'),('MOON','Directional'),('POINT','Point'),
    ('SPOT','Spot'),('TUBE','Tube')]:
    values=[l for l in lights if l.get('LightRole')==role]
    assert len(values)==1,role
    l=values[0]
    assert l['LightType']==kind,(role,kind)
    assert 0 <= float(l['Intensity']) <=4
    assert .1 <= float(l['RadiusWorld']) <=60
    assert -360 <= float(l['DirectionDegrees']) <=360
    assert 5 <= float(l['ConeAngleDegrees']) <=170
    assert 0 <= float(l['ConeSoftness']) <=.95
    assert .1 <= float(l['TubeLengthWorld']) <=40
for a in art:
    for pathField in ('Image','EmissionMap','NormalMap','EdgeMap'):
        file=a.get(pathField)
        if not file:continue
        target = scene.parent / file
        if not target.is_file():
            # Incremental ZIPs include changed files only. The caller's Phase19D
            # installation already supplies older artwork maps and sign textures.
            assert file.startswith('TerrainArt/LightingTest/') and not file.endswith(
                ('L20A_SunMoon_Sign.png','L20B_Point_Sign.png',
                 'L20C_Spot_Sign.png','L20D_Tube_Sign.png')), f'Missing new asset: {file}'
            continue
        raw=target.read_bytes()
        assert raw.startswith(b'\x89PNG\r\n\x1a\n'),file
        width,height=struct.unpack('>II',raw[16:24])
        assert width>0 and height>0, file
sources=[
 'PlatformerSandbox.Foundation/Presentation/Lighting/LightDefinitions.cs',
 'PlatformerSandbox.Foundation/Presentation/Lighting/LightingWorldSampler.cs',
 'PlatformerSandbox.Gameplay/Content/LdtkLightingImporter.cs',
 'Source/Lighting/GameplayLightingRenderer.cs',
 'Source/Game1.cs',
 'Source/Lighting/LightingToolsPanel.cs',
 'Content/Assets/Effects/L_BASIC_SurfaceLighting.fx',
]
for file in sources:
    s=(root/file).read_text()
    assert not re.search(r'^(<<<<<<<|=======|>>>>>>>)',s,re.M),file
    bal=0
    for c in s:
        if c=='{':bal+=1
        if c=='}':bal-=1
    assert bal==0,(file,bal)
renderer=(root/'Source/Lighting/GameplayLightingRenderer.cs').read_text()
shader=(root/'Content/Assets/Effects/L_BASIC_SurfaceLighting.fx').read_text()
for name in ('L20LightField','L20BloomDownsample','L20HdrBloomComposite','L20SdrBloomComposite'):
    assert name in renderer and f'technique {name}' in shader,name
for name in ('Light0Shape','Light1Shape','Light0ShapeExtra','Light1ShapeExtra'):
    assert name in shader,name
assert 'Light{i}Shape' in renderer and 'Light{i}ShapeExtra' in renderer
assert 'DrawDirectionalMask' in renderer
assert 'DisposeBloomPyramid' in renderer
assert 'LastBloomPasses = 10' in renderer
assert 'LIGHT TYPES / L20' in (root/'Source/Lighting/LightingToolsPanel.cs').read_text()
ui=(root/'Source/Lighting/LightingToolsPanel.cs').read_text()
assert ui.count('"BLOOM SCATTER: " + bloomScatter')==2
assert ui.count('"BLOOM VIEW: " + (bloomOnly ? "BLOOM ONLY" : "FINAL COMPOSITE")')==2
assert 'CycleLightTypeFilter' in (root/'Source/Game1.cs').read_text()
print('Phase 20 preflight PASS: 5 tagged light types, shadow geometry, 3-scale bloom,',
      len(art), 'art assets, UI/source bindings')
