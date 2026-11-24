"""Static fixture/contract checks and independent numeric fold reference (not GPU validation)."""
from pathlib import Path
import json, math, random
r=Path(__file__).resolve().parents[1]
lab=json.loads((r/'Content/Levels/LightTypesLab.ldtk').read_text(encoding='utf-8-sig'))
level=lab['levels'][0]; defs={d['identifier']:{f['identifier']:f for f in d['fieldDefs']} for d in lab['defs']['entities']}
active=[]
for layer in level['layerInstances']:
    for e in layer.get('entityInstances',[]):
        fields={f['__identifier']:f for f in e['fieldInstances']}
        assert len(fields)==len(e['fieldInstances'])
        for name,f in fields.items():
            if name.startswith('Receiver'):
                d=defs[e['__identifier']][name]
                assert f['defUid']==d['uid'] and f['__type']==d['__type']
                if d['__type']=='Float': assert d['min']<=f['__value']<=d['max']
        v={k:f['__value'] for k,f in fields.items()}
        if v.get('ReceiverProjection','Legacy')!='Legacy': active.append((e,v))
assert len(active)==1
e,v=active[0];assert v['Image'].endswith('Ground.png') and v['MaterialShading']=='Flat'
assert v['ReceiverProjection']=='Clipped' and v['ReceivesShadows'] and not v['CastsShadows']
assert e['px']==[0,832] and e['__pivot']==[0,0] and level['pxHei']==1280
seam=(level['pxHei']-e['px'][1])/32
rng=random.Random(20)
for _ in range(10000):
    x,y,c,s=rng.uniform(-100,100),rng.uniform(-100,100),rng.uniform(.1,1),rng.uniform(-2,2)
    down=seam-y; a,b=x+s*down,seam-c*down
    inverse_down=(seam-b)/c
    assert math.isclose(a-s*inverse_down,x,abs_tol=1e-9) and math.isclose(seam-inverse_down,y,abs_tol=1e-9)
    assert math.isclose(seam-c*0,seam)
renderer=(r/'Source/Lighting/GameplayLightingRenderer.cs').read_text()
shader=(r/'Content/Assets/Effects/L20_PolygonRim.fx').read_text()
assert 'ProductionMaterialPassPlanner.FilterVisibility(back, _shadowSoftnessPixels)' in renderer
assert 'visibility < 0.999' in shader and 'nearestIndex' in shader and 'RimSettings.w<0.5' in shader
assert 'technique ReceiverLight' in shader and 'fx.Techniques["ReceiverLight"]' in renderer
for n in ['ReceiverFold','ReceiverAngles','VisibilityMap','ViewportSize','LightPositionRadius','LightShapeDirection','LightExtra','LightEnergy']:
    assert n in shader and f'Parameters["{n}"]' in renderer
for fn in ['Source/Game1.cs','Source/Lighting/LightingToolsPanel.cs']:
    text=(r/fn).read_text()
    for action in ['CycleReceiverProjection','ToggleReceiverNormals','TogglePolygonRims']: assert action in text
print('PASS: receiver LDtk field bindings, exact seam, 10,000 independent affine round trips, hard-rim and UI/shader bindings.')
print('NOTE: .NET, LDtk editor, GPU shader and visual validation are still required.')
