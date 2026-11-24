"""Opt in another embedded LDtk project to custom shadow field authoring.
Usage: py Tools/l20_add_shadow_authoring_fields.py path/to/file.ldtk
Creates a backup and modifies only missing field definitions/instances.
"""
import argparse, copy, json, shutil
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('path', type=Path)
args=parser.parse_args()
p=args.path
project=json.loads(p.read_text(encoding='utf-8-sig'))
if project.get('externalLevels') or project.get('simplifiedExport'):
    raise SystemExit('Only embedded, full LDtk exports are supported.')
reference=json.loads((Path(__file__).resolve().parents[1]/'Content/Levels/LightTypesLab.ldtk').read_text(encoding='utf-8-sig'))
ref=next(d for d in reference['defs']['entities'] if d['identifier']=='TerrainArtwork')
changed=False
for entity in project['defs']['entities']:
    kind=entity['identifier']
    if kind not in ('TerrainArtwork','ScenerySprite','LightBlocker'): continue
    for name,default in [('ShadowShape','Rectangle'),('ShadowVertices',None)]:
        fields=entity['fieldDefs']
        field=next((f for f in fields if f['identifier']==name),None)
        if field is None:
            field=copy.deepcopy(next(f for f in ref['fieldDefs'] if f['identifier']==name))
            field['uid']=project['nextUid']; project['nextUid']+=1
            fields.append(field); changed=True
        if field['__type']!='String' or field['isArray']:
            raise SystemExit(f'Conflicting {kind}.{name} definition; no file written.')
        for level in project['levels']:
            for layer in level.get('layerInstances') or []:
                for instance in layer.get('entityInstances',[]):
                    if instance['defUid'] != entity['uid']: continue
                    if any(f['__identifier']==name for f in instance['fieldInstances']): continue
                    instance['fieldInstances'].append({'__identifier':name,'__type':'String','__value':default,
                        '__tile':None,'defUid':field['uid'],'realEditorValues':[]}); changed=True
if changed:
    backup=p.with_suffix(p.suffix+'.l20-backup')
    if backup.exists(): raise SystemExit(f'Backup already exists: {backup}. Move it before modifying again.')
    shutil.copy2(p,backup)
    p.write_text(json.dumps(project,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    print(f'Updated {p}; original saved as {backup}')
else: print('No changes needed.')
