"""Apply a reusable lighting preset to one LDtk entity. Writes a separate file only.
The target LDtk project must already define each required field.
"""
import argparse,json
from pathlib import Path

def apply(project,preset,iid):
    entities=[e for level in project['levels'] for layer in level.get('layerInstances') or [] for e in layer.get('entityInstances',[]) if e['iid']==iid]
    if len(entities)!=1: raise ValueError('Expected exactly one entity IID')
    e=entities[0]
    if e['__identifier']!=preset['entity']: raise ValueError('Preset entity type mismatch')
    definition=next(d for d in project['defs']['entities'] if d['identifier']==e['__identifier'])
    defs={f['identifier']:f for f in definition['fieldDefs']}
    missing=set(preset['fields'])-defs.keys()
    if missing: raise ValueError('Target project lacks fields: '+', '.join(sorted(missing)))
    for key,value in preset['fields'].items():
        d=defs[key]; t=d['__type']
        valid=(t=='String' and isinstance(value,str) or t=='Color' and isinstance(value,str) and len(value)==7 and value[0]=='#' or t=='Bool' and type(value)==bool or t=='Float' and type(value) in (int,float) or t=='Int' and type(value)==int)
        if not valid: raise ValueError('Preset field type mismatch: '+key)
        if type(value) in (int,float) and (d.get('min') is not None and value<d['min'] or d.get('max') is not None and value>d['max']): raise ValueError('Preset field outside authored range: '+key)
        f=next((f for f in e['fieldInstances'] if f['__identifier']==key),None)
        if f is None:
            f=dict(__identifier=key,__type=t,__tile=None,defUid=d['uid'],realEditorValues=[]);e['fieldInstances'].append(f)
        f['__value']=value
    return project

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project',type=Path);parser.add_argument('iid');parser.add_argument('preset');parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--catalog',type=Path,default=Path(__file__).with_name('phase21_lighting_presets.json'))
    args=parser.parse_args()
    if args.project.resolve()==args.output.resolve():parser.error('Choose a separate output project')
    project=json.loads(args.project.read_text(encoding='utf-8-sig'));catalog=json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    result=apply(project,catalog['presets'][args.preset],args.iid)
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
