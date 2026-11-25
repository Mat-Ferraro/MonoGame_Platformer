"""Source/fixture preflight only; no GPU or .NET execution."""
from pathlib import Path
import json
r=Path(__file__).resolve().parents[1]
d=json.loads((r/'Content/Levels/EmissiveLightingLab.ldtk').read_text(encoding='utf-8-sig'))
defs={e['identifier']:{f['identifier']:f for f in e['fieldDefs']} for e in d['defs']['entities']}
emitters=[]
for l in d['levels'][0]['layerInstances']:
    for e in l.get('entityInstances',[]):
        fs={f['__identifier']:f for f in e['fieldInstances']}
        assert len(fs)==len(e['fieldInstances'])
        for name,f in fs.items():
            if name.startswith('EmissiveLight'):
                a=defs[e['__identifier']][name]
                assert a['uid']==f['defUid'] and a['__type']==f['__type']
        if fs.get('EmissiveLightRadius',{}).get('__value',0)>0:
            assert fs['EmissiveLightIntensity']['__value']>0
            assert fs['EmissiveLightCastsShadows']['__value'] is True
            emitters.append(e['iid'])
assert len(emitters)==2
renderer=(r/'Source/Lighting/GameplayLightingRenderer.cs').read_text()
assert 'SpatialSurfaceLighting.Candidates' in renderer
assert 'surface.MinimumAmbient' in renderer
assert 'ProductionMaterialPassPlanner.FilterVisibility(back, _shadowSoftnessPixels)' in renderer
assert 'DrawReceiverLight(' in renderer
level=(r/'Source/Gyms/LdtkLevelRenderer.cs').read_text()
assert 'SpatialSurfaceLighting.RuntimeSprite' in level and 'sb => DrawBody(sb, Color.White)' in level
art=(r/'Source/Gyms/LdtkVisualSceneAssets.cs').read_text()
assert art.count('SpatialSurfaceLighting.Promote(')==2
presenter=(r/'PlatformerSandbox.Gameplay/Presentation/GameplayLightingPresenter.cs').read_text()
assert 'EmissiveWorldLighting.Create' in presenter and 'all.Where(l => tags.Contains(l.Tag, StringComparer.Ordinal))' in presenter
print('PASS: two opt-in emissive witnesses, field bindings, receiver dispatch, region light selection, pre-budget filtering, and Phase 20 paths retained.')
print('NOTE: run .NET tests and actual GPU visual validation separately.')
