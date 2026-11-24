from pathlib import Path
import json

root = Path(__file__).resolve().parents[1]
level = root / 'Content' / 'Levels' / 'MaterialShowcaseTest.ldtk'
obj = json.loads(level.read_text())
entities = [
    e for layer in obj['levels'][0]['layerInstances'] if layer['__type']=='Entities'
    for e in layer['entityInstances']
]
materials=[]
lights=[]
for e in entities:
    if e['__identifier']=='TerrainArtwork':
        fields={f['__identifier']:f['__value'] for f in e['fieldInstances']}
        materials.append(fields)
    elif e['__identifier']=='LightSource':
        fields={f['__identifier']:f['__value'] for f in e['fieldInstances']}
        lights.append(fields)
print('materials', len(materials), 'lights', len(lights))
for light in lights:
    intensity = light.get('Intensity', 1.4)
    assert isinstance(intensity, (int, float)) and 0 <= intensity <= 4, (
        'LDtk importer permits LightSource.Intensity only in [0, 4]', intensity)
    radius = light.get('RadiusWorld', 5)
    assert isinstance(radius, (int, float)) and 0.1 <= radius <= 60, (
        'LDtk importer permits LightSource.RadiusWorld only in [0.1, 60]', radius)
emissive = [m for m in materials if float(m.get('EmissionIntensity') or 0) >= 5]
print('emissive>=5', len(emissive))
assert len(emissive) >= 3
print('Phase 19C material showcase preflight OK')
