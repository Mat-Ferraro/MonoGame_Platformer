"""Static fixture and independent sample geometry checks, not .NET/GPU execution."""
from pathlib import Path
import json,math,copy,sys
from PIL import Image,ImageDraw
from phase21_shadow_hull import extract
from phase21_apply_lighting_preset import apply
root=Path(__file__).resolve().parents[1]
load=lambda n:json.loads((root/n).read_text(encoding='utf-8-sig'))
lab=load('Content/Levels/Phase21LightingLab.ldtk'); level=lab['levels'][0]
assert level['identifier']=='PHASE21_LIGHTING_LAB'
es=[e for l in level['layerInstances'] for e in l['entityInstances']]
assert len({e['iid'] for e in es})==len(es)
f=lambda e:{v['__identifier']:v['__value'] for v in e['fieldInstances']}
defs={d['identifier']:{v['identifier']:v for v in d['fieldDefs']} for d in lab['defs']['entities']}
for e in es:
    for field in e['fieldInstances']:
        df=defs[e['__identifier']][field['__identifier']]
        assert df['uid']==field['defUid'] and df['__type']==field['__type']
    for key in ('Image','NormalMap','EdgeMap','EmissionMap'):
        if f(e).get(key):
            path=root/'Content/Levels'/f(e)[key];assert path.is_file(),path
            with Image.open(path) as im: im.verify()
lights={f(e)['LightRole']:e for e in es if e['__identifier']=='LightSource'}
assert set(lights)=={'AREA_POINT','AREA_LINE','AREA_DISC','AREA_RECT','SHOWCASE'}
assert f(lights['AREA_POINT'])['LightType']=='Point'
for tag,shape in [('AREA_LINE','Line'),('AREA_DISC','Disc'),('AREA_RECT','Rectangle')]:
    assert f(lights[tag])['LightType']=='Tube' and f(lights[tag])['EmitterFootprint']==shape
assert len({tuple(lights[tag]['px']) for tag in lights if tag.startswith('AREA_')})==1
assert f(lights['SHOWCASE'])['MotionAxis']=='Horizontal' and f(lights['SHOWCASE'])['LightPulseDepth']>0
mapped=[e for e in es if f(e).get('NormalMap')];assert len(mapped)==2
for e in mapped:
    v=f(e); im=Image.open(root/'Content/Levels'/v['Image']);value,rings=extract(im)
    assert value==v['ShadowVertices'] and 4<len(rings[0])<=64
assert any(f(e).get('BlockerRotationMode')=='Clockwise' and f(e).get('EmissiveLightRadius',0)>0 for e in mapped)
assert len([e for e in es if f(e).get('EmissiveLightRadius',0)>0])==3
assert any(f(e).get('Opacity')==.55 for e in es)
# Independent reference: midpoint ray is blocked; distributed source can see the receiver from outer samples.
# Source on x-axis, blocker spans x +/- 0.3 and y [1,2], receiver at (0,4).
def blocked(x):
    return min(abs(x*(1-1/4)),abs(x*(1-2/4)))<=.3
for n in (3,5,9):
    positions=[8*((i+.5)/n-.5) for i in range(n)]
    assert abs(sum(positions))<1e-9 and blocked(0)
    visible=sum(not blocked(x) for x in positions)
    assert 0<visible<n
    weights=[2/n for x in positions];assert abs(sum(weights)-2)<1e-9
    energy=lambda x: max(0,1-math.hypot(x,4)/12)**2*2/n
    assert 0<sum(energy(x) for x in positions if not blocked(x))<sum(energy(x) for x in positions)
# Disjoint contours, empty and holed sprites; reject unsupported topology without silently filling holes.
im=Image.new('RGBA',(40,40));dr=ImageDraw.Draw(im);dr.rectangle((2,2,10,10),fill='white');dr.rectangle((25,25,35,35),fill='white')
assert len(extract(im)[1])==2
hole=Image.new('RGBA',(40,40));dr=ImageDraw.Draw(hole);dr.rectangle((2,2,35,35),fill='white');dr.rectangle((10,10,25,25),fill=(0,0,0,0))
for bad in (hole,Image.new('RGBA',(40,40))):
    try:extract(bad)
    except ValueError:pass
    else:raise AssertionError('unsupported/empty geometry should be rejected')
cat=load('Tools/phase21_lighting_presets.json'); target=lights['AREA_RECT']; changed=apply(copy.deepcopy(lab),cat['presets']['soft_line'],target['iid']);new=next(e for l in changed['levels'][0]['layerInstances'] for e in l['entityInstances'] if e['iid']==target['iid']);assert f(new)['EmitterFootprint']=='Line' and f(target)['EmitterFootprint']=='Rectangle'
source=(root/'Source/Game1.cs').read_text();render=(root/'Source/Lighting/GameplayLightingRenderer.cs').read_text()
assert 'ApplyPhase21LabPreset(2)' in source and 'DrawPhase21LabHud' in source and 'L21_TangentTransform' in source
assert 'expandedSamples: true' in render and render.count('AreaEmitterSampling.Expand(')>=3
assert 'Effects/L21_TangentTransform.fx' in (root/'Content/Builder/Builder.cs').read_text()
assert 'Phase21LightingLab' in (root/'PlatformerSandbox.Gameplay/GameFlow/GameUiNavigation.cs').read_text()
print('PASS: isolated lab, assets, field bindings, generated contours, independent partial-area ray reference, preset application, shader/host integration static checks.')
print('NOTE: local .NET and GPU tests still required.')
