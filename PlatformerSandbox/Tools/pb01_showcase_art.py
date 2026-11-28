"""Regenerate presentation-only PB01 integration terrain from the solid IntGrid.
Standard library only. Run after editing the collision grid; never edits it.
"""
import copy, json, struct, uuid, zlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
LEVEL=ROOT/'Content/Levels/EngineIntegrationLab.ldtk'
ASSETS=LEVEL.parent/'TerrainArt/PB01Integration'
PREFIX='TerrainArt/PB01Integration/'
NAMESPACE=uuid.UUID('b2911d88-0b27-4cb2-b4d9-6fa6850744f7')
# center x/y in LDtk pixels, color, radius (world), intensity, shadow
LIGHTS=[(210,410,'#FFD08A',9.0,1.55,True), (430,435,'#84C9FF',8.5,1.35,False), (475,630,'#83B9DA',7.0,1.25,False),
        (665,470,'#C5A2FF',9.0,1.45,True), (880,430,'#78E6BE',9.0,1.4,False),
        (1110,620,'#FFB36F',8.5,1.55,True), (1370,570,'#8DDFFF',9.0,1.45,True),
        (1680,470,'#FFC786',9.5,1.65,True), (1900,595,'#8CB6FF',9.0,1.5,False),
        (2180,595,'#F29AD8',9.0,1.45,True), (2450,590,'#8FE4C6',9.5,1.45,False)]

def fields(e): return {f['__identifier']:f['__value'] for f in e['fieldInstances']}
def update(e, **values):
    for f in e['fieldInstances']:
        if f['__identifier'] in values: f['__value']=values.pop(f['__identifier'])
    if values: raise ValueError(f'Missing field definitions: {values}')
def rectangles(grid):
    w,h=grid['__cWid'],grid['__cHei']; a=grid['intGridCsv']; active={}; result=[]
    for y in range(h):
        next_row={}; x=0
        while x<w:
            if not a[y*w+x]: x+=1; continue
            start=x
            while x<w and a[y*w+x]: x+=1
            key=(start,x-start); old=active.get(key,(start,y,x-start,0))
            next_row[key]=(*old[:3],old[3]+1)
        result.extend(v for k,v in active.items() if k not in next_row); active=next_row
    return sorted(result+list(active.values()),key=lambda r:(r[1],r[0]))
def png_bytes(w,h,rgba):
    def chunk(t,d):return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
    rows=b''.join(b'\0'+rgba[y*w*4:(y+1)*w*4] for y in range(h))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows,9))+chunk(b'IEND',b'')
def stone(grid,r):
    col,row,cw,ch=r; g=grid['__gridSize']; w,h=cw*g,ch*g; a=grid['intGridCsv']; cols,rows=grid['__cWid'],grid['__cHei']
    def solid(cx,cy):return 0<=cx<cols and 0<=cy<rows and a[cy*cols+cx]==1
    rgba=bytearray()
    for y in range(h):
        gy=row*g+y; cy=gy//g
        for x in range(w):
            gx=col*g+x; cx=gx//g; lx=gx%g; ly=gy%g
            # Continuous global texture; edges only at actual exposed collision faces.
            n=((gx*37+gy*73+(gx//7)*11+(gy//5)*19)%19)-9
            shade=n + (5 if (gy//6)%2==0 else -3)
            c=(88+shade,111+shade,119+shade)
            if ly<3 and not solid(cx,cy-1): c=(206,225,207)
            elif ly<7 and not solid(cx,cy-1): c=(137,168,153)
            elif ((lx<3 and not solid(cx-1,cy)) or (lx>=g-3 and not solid(cx+1,cy))):c=(124,153,160)
            elif ly>=g-3 and not solid(cx,cy+1):c=(48,64,73)
            rgba.extend((*c,255))
    return png_bytes(w,h,rgba)
def marker():
    # Small round, clearly decorative lamp indicator with transparent corners.
    a=bytearray()
    for y in range(24):
        for x in range(24):
            r=(x-11.5)**2+(y-11.5)**2
            v=255 if r<30 else 155 if r<63 else 60
            a.extend((v,v,v,255 if r<108 else 0))
    return png_bytes(24,24,a)
def positioned(template,key,x,y,w,h,grid):
    e=copy.deepcopy(template); e.update(iid=str(uuid.uuid5(NAMESPACE,key)),px=[x,y],__worldX=x,__worldY=y,__grid=[x//grid,y//grid],__pivot=[0,0],width=w,height=h);return e

def build():
    data=json.loads(LEVEL.read_text()); level=data['levels'][0]; layers=level['layerInstances']; grid=next(l for l in layers if l['__identifier']=='Collisions'); ents=next(l for l in layers if l['__identifier']=='Entities'); g=grid['__gridSize']; eg=ents['__gridSize']
    old=ents['entityInstances']; art=next(e for e in old if e['__identifier']=='TerrainArtwork'); lamp=next(e for e in old if e['__identifier']=='LightSource')
    # Idempotent: use stable IDs, discard only previously generated fixtures and old lights.
    generated=lambda e: fields(e).get('Image','').startswith(PREFIX)
    kept=[]
    for e in old:
        kind=e['__identifier']; f=fields(e)
        if generated(e) or kind=='LightSource':continue
        if kind in ('TerrainArtwork','ScenerySprite','PaintedSprite'):
            if f.get('Plane')=='Terrain':
                # Remove false ledges entirely; background art remains as separate scenery.
                continue
            if f.get('Plane')=='Background':
                update(e,Opacity=min(f['Opacity'],.30),CastsShadows=False)
            elif f.get('Plane')=='Foreground':
                update(e,Opacity=min(f['Opacity'],.16),CastsShadows=False)
        kept.append(e)
    for l in layers:
        if l['__identifier'].startswith('BG_'):l['__opacity']=.24
        if l['__identifier'].startswith('FG_'):l['__opacity']=.10
    for d in data['defs']['layers']:
        if d['identifier'].startswith('BG_'):d['displayOpacity']=.24
        if d['identifier'].startswith('FG_'):d['displayOpacity']=.10
    ASSETS.mkdir(parents=True,exist_ok=True)
    for r in rectangles(grid):
        x,y,w,h=[v*g for v in r]; name=f'Solid_{r[0]:02}_{r[1]:02}.png'; (ASSETS/name).write_bytes(stone(grid,r))
        e=positioned(art,'solid:'+name,x,y,w,h,eg)
        update(e,Image=PREFIX+name,Plane='Terrain',Depth=0,RotationDeg=0.0,Opacity=1.0,ParallaxX=0.0,ParallaxY=0.0,FlipX=False,FlipY=False,Tint='#FFFFFF',ReceivesLight=True,ReceivesShadows=True,CastsShadows=True,ContactShadow=False,LightingMode='Lit');kept.append(e)
    (ASSETS/'Lamp.png').write_bytes(marker())
    for i,(x,y,color,radius,intensity,shadows) in enumerate(LIGHTS):
        e=positioned(lamp,f'light:{i}',x-12,y-12,24,24,eg)
        update(e,Color=color,RadiusWorld=radius,Intensity=intensity,Priority=7,CastShadows=shadows);kept.append(e)
        e=positioned(art,f'marker:{i}',x-12,y-12,24,24,eg)
        update(e,Image=PREFIX+'Lamp.png',Plane='Background',Depth=90,RotationDeg=0.0,Opacity=1.0,ParallaxX=0.0,ParallaxY=0.0,FlipX=False,FlipY=False,Tint=color,ReceivesLight=False,ReceivesShadows=False,CastsShadows=False,ContactShadow=False,LightingMode='Unlit');kept.append(e)
    ents['entityInstances']=kept
    LEVEL.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':build()
