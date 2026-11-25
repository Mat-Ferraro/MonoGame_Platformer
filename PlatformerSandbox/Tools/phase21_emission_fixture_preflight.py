"""Dedicated emission lab: independent CPU geometry / fixture checks, not GPU validation."""
from pathlib import Path
import json, math, struct
root=Path(__file__).resolve().parents[1]
def load(name): return json.loads((root/name).read_text(encoding='utf-8-sig'))
def entities(d): return [e for l in d['levels'][0]['layerInstances'] for e in l.get('entityInstances',[])]
def fields(e): return {f['__identifier']:f['__value'] for f in e['fieldInstances']}
d=load('Content/Levels/EmissiveLightingLab.ldtk'); level=d['levels'][0]; es=entities(d)
assert level['identifier']=='EMISSIVE_LIGHTING_LAB' and level['pxWid']==1280
assert len({e['iid'] for e in es})==len(es) and len(es)==10
assert not any(e['__identifier']=='LightSource' for e in es)
for l in level['layerInstances']:
    assert l['__cWid']*l['__gridSize']==level['pxWid']
    assert l['__cHei']*l['__gridSize']==level['pxHei']
    if l['__type']=='IntGrid':
        assert len(l['intGridCsv'])==l['__cWid']*l['__cHei']
        assert l['intGridCsv']==[0]*(26*40)+[1]*(14*40)
defs={e['identifier']:{f['identifier']:f for f in e['fieldDefs']} for e in d['defs']['entities']}
by_name={}
for e in es:
    v=fields(e); assert len(v)==len(e['fieldInstances'])
    for f in e['fieldInstances']:
        spec=defs[e['__identifier']][f['__identifier']]
        assert f['defUid']==spec['uid'] and f['__type']==spec['__type']
    if e['__identifier']=='PlayerSpawn': continue
    path=root/'Content/Levels'/v['Image']; raw=path.read_bytes()
    assert raw[:8]==b'\x89PNG\r\n\x1a\n'
    w,h=struct.unpack('>II',raw[16:24]); assert w>0 and h>0
    if v.get('EmissionMap'): assert (path.parent/'EmitterMask.png').is_file()
    name=path.stem
    if name=='LowBlocker' and e['px'][0]>640: name='DarkBlocker'
    assert name not in by_name
    by_name[name]=(e,v)
assert set(by_name)=={'LabBackdrop','LabGround','CyanEmitter','MagentaEmitter','NeutralStrip','NeutralItem','LowBlocker','DarkBlocker','GlowOnly'}
assert sum(v.get('EmissiveLightRadius',0)>0 for _,v in by_name.values())==2
assert sum(v.get('CastsShadows',False) for _,v in by_name.values())==2
for name,color in [('CyanEmitter','#00E5FF'),('MagentaEmitter','#FF168C')]:
    e,v=by_name[name]; assert v['EmissionColor']==color and v['EmissiveLightIntensity']==2.5 and v['EmissiveLightCastsShadows']
assert by_name['GlowOnly'][1]['EmissiveLightRadius']==0 and by_name['GlowOnly'][1]['EmissionIntensity']>0
for name in ('NeutralStrip','NeutralItem','LabBackdrop','LabGround'):
    _,v=by_name[name]; assert v['ReceivesLight'] and v['ReceivesShadows'] and not v['CastsShadows'] and v.get('EmissionIntensity',0)==0
old=entities(load('Content/Levels/LightTypesLab.ldtk'))
assert not any('L21Emission/' in str(fields(e).get('Image')) for e in old)
assert {fields(e)['LightRole'] for e in old if e['__identifier']=='LightSource'}=={'SUN','MOON','POINT','SPOT','TUBE'}
b,bv=by_name['LowBlocker']; c,cv=by_name['CyanEmitter'];m,mv=by_name['MagentaEmitter']
def center(e): return tuple(e['px'][i]+e[k]/2 for i,k in enumerate(('width','height')))
def gain(e,v,p): return v['EmissiveLightIntensity']*max(0,1-math.dist(center(e),p)/(32*v['EmissiveLightRadius']))**2
def blocked(a,p,wall=b):
    lo,hi=0.,1.
    for i,k in enumerate(('width','height')):
        x=wall['px'][i];y=x+wall[k];delta=p[i]-a[i]
        if not delta:
            if not x<=a[i]<=y:return False
            continue
        t,u=sorted(((x-a[i])/delta,(y-a[i])/delta));lo=max(lo,t);hi=min(hi,u)
        if lo>hi:return False
    return True
assert not blocked(center(c),(550,755)) and blocked(center(c),(550,825))
assert gain(c,cv,(550,755))>.5 and gain(c,cv,(550,825))>.5
assert gain(c,cv,(640,740))>.2 and gain(m,mv,(640,740))>.2
assert gain(c,cv,(340,740))>1 and gain(m,mv,(340,740))==0
assert gain(m,mv,(940,740))>1 and gain(c,cv,(940,740))==0
right,rv=by_name['DarkBlocker']
assert bv['MaterialDepthZ']==0 and rv['MaterialDepthZ']==1
assert cv['MaterialDepthZ']==mv['MaterialDepthZ']==0
assert rv['BackRimIntensity']==0 and rv['ReceivesLight'] and rv['ReceivesShadows']
assert (b['width'],b['height'])==(right['width'],right['height'])
assert not blocked(center(m),(860,800),right)
assert blocked(center(m),(832,810),right) and blocked(center(m),(800,825),right)
assert gain(m,mv,(832,810))>.5 and gain(c,cv,(832,810))==0
assert gain(c,cv,(442,810))>.5 and gain(m,mv,(442,810))==0
cfg=load('Content/Lighting/L456_GameplayLighting.json'); p=next(p for p in cfg['presets'] if p['id']=='EmissiveLabNeutral'); assert p['ambient']==dict(r=.14,g=.14,b=.14)
s=(root/'Source/Game1.cs').read_text(); assert 'ApplyEmissionLabPreset(3)' in s and 'preset is 3 or 4' in s and 'preset is 2 or 4' in s and '_l19Bloom = preset == 4' in s and 'ACTIVE LIGHTS' in s
assert 'GameLevelIds.EmissiveLightingLab' in (root/'PlatformerSandbox.Gameplay/GameFlow/GameUiNavigation.cs').read_text()
print('PASS: separate 1280px lab, two real emitters, two distinct wall depths, one glow-only control, isolated neutral receivers, clear/blocked rays and overlap, direct preset wiring.')
print('NOTE: actual MonoGame rendering and .NET tests must be validated locally.')
