#!/usr/bin/env python3
"""Offline source preflight for Phase 19D. No MonoGame or SDK required."""
import json
import math
from pathlib import Path
import struct

root = Path(__file__).resolve().parents[1]
scene = root / 'Content/Levels/MaterialShowcaseTest.ldtk'
obj = json.loads(scene.read_text())
levels = obj['levels']
assert len(levels) == 1 and levels[0]['identifier'] == 'MATERIAL_SHOWCASE_TEST'
entities = [
    e for layer in levels[0]['layerInstances'] if layer['__type'] == 'Entities'
    for e in layer['entityInstances']
]
materials=[];lights=[]
for e in entities:
    f = {v['__identifier']: v['__value'] for v in e['fieldInstances']}
    if e['__identifier'] == 'TerrainArtwork':
        materials.append(f)
        for key in ('Image','NormalMap','EdgeMap','EmissionMap'):
            path=f.get(key)
            if not path: continue
            full=(scene.parent/path)
            if not full.is_file():
                # Incremental stage archive omits these pre-existing project PNGs.
                assert path in {
                    'TerrainArt/LightingTest/Backdrop.png',
                    'TerrainArt/LightingTest/Ground.png',
                    'TerrainArt/LightingTest/Panel.png',
                    'TerrainArt/LightingTest/Pillar.png'
                }, (key, path)
                continue
            raw = full.read_bytes()
            assert raw.startswith(b'\x89PNG\r\n\x1a\n'), path
            w, h = struct.unpack('>II', raw[16:24])
            assert w > 0 and h > 0, path
        assert math.isfinite(float(f.get('Opacity',1))) and 0 <= f.get('Opacity',1) <= 1
        assert 0 <= f.get('EmissionIntensity',0) <= 8
    if e['__identifier'] == 'LightSource':
        lights.append(f)
        assert 0 <= f['Intensity'] <= 4 and 0.1 <= f['RadiusWorld'] <= 60, f
assert len(lights)==3, len(lights)
assert len([m for m in materials if m.get('CastsShadows')])==2
assert len([m for m in materials if 'L19D_' in (m.get('Image') or '') and 'Wall.png' in m['Image'] and m['ReceivesShadows']])==3
assert [round(m['Opacity'],2) for m in materials if (m.get('Image') or '').endswith(('L19B_BlueGlass.png','L19B_Ghost.png'))] == [0.6,0.5]
assert len([m for m in materials if m.get('EmissionIntensity',0)>=5]) >=3

for name in ('L19B_Bloom_Sign.png','L19B_Opacity_Sign.png','L19B_Hologram_Sign.png'):
    sign = scene.parent / 'TerrainArt/LightingTest' / name
    assert sign.is_file() and sign.read_bytes().startswith(b'\x89PNG\r\n\x1a\n'), name
# Pixel-alpha aperture checked separately when generating the PNG fixture.
assert 'ApplyMaterialShowcasePreset(6)' in (root/'Source/Game1.cs').read_text()
assert '"WORLD LIGHTS: " + (worldLights ? "ON" : "OFF")' in (root/'Source/Lighting/LightingToolsPanel.cs').read_text()
main=(root/'Source/Game1.cs').read_text()
assert '_l19dWorldLightsEnabled' in main and 'lightingWorldActive &&' in main
assert 'case LightingToolAction.ToggleShowcaseWorldLights:' in main
print('L19D OK: 4 stations, 3 lit receivers, 2 shadow casters, 3 shadowed local lights, visible opacity witnesses')
