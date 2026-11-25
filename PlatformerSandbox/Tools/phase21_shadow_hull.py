"""Offline alpha-to-LDtk shadow hull authoring. Requires Pillow and Shapely.
Never reads GPU pixels at runtime; writes only explicitly requested outputs.
"""
import argparse, json
from pathlib import Path
from PIL import Image, ImageDraw
from shapely.geometry import box
from shapely.ops import unary_union


def extract(image, alpha=128, tolerance=1.0, min_area=4):
    if not (1 <= alpha <= 255 and tolerance >= 0 and min_area >= 0):
        raise ValueError("Invalid alpha/tolerance/min-area")
    im = image.convert('RGBA'); w,h = im.size
    if w*h > 16_000_000: raise ValueError('Image exceeds 16 MP offline budget')
    mask = im.getchannel('A'); pix=mask.load(); runs=[]
    for y in range(h):
        x=0
        while x<w:
            if pix[x,y]<alpha: x+=1; continue
            start=x
            while x<w and pix[x,y]>=alpha: x+=1
            runs.append(box(start,y,x,y+1))
            if len(runs)>250000: raise ValueError('Alpha mask too fragmented; simplify artwork')
    if not runs: raise ValueError('No solid pixels')
    geom=unary_union(runs)
    parts=list(geom.geoms) if geom.geom_type=='MultiPolygon' else [geom]
    parts=[p.simplify(tolerance,preserve_topology=True) for p in parts if p.area>=min_area]
    parts.sort(key=lambda p:(p.bounds[0],p.bounds[1],-p.area))
    if not parts: raise ValueError('No contours survive minimum area')
    if any(p.geom_type!='Polygon' or not p.is_valid or p.interiors for p in parts):
        raise ValueError('Enclosed holes or invalid contours are not supported by current runtime; author a manual open/compound hull')
    for i,p in enumerate(parts):
        if any(p.intersects(q) for q in parts[:i]): raise ValueError('Touching contours are unsupported')
    rings=[list(p.exterior.coords)[:-1] for p in parts]
    if any(not 3<=len(r)<=64 for r in rings) or sum(map(len,rings))>128:
        raise ValueError('Edge budget exceeded (64/contour, 128/artwork); increase tolerance or author manually')
    # Round-trip normalized coordinates at sufficient precision; preserve topology.
    encoded='|'.join(';'.join(f'{x/w:.9f},{y/h:.9f}' for x,y in r) for r in rings)
    return encoded,rings


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('image',type=Path); p.add_argument('--alpha',type=int,default=128)
    p.add_argument('--tolerance',type=float,default=1); p.add_argument('--min-area',type=float,default=4)
    p.add_argument('--preview',type=Path); p.add_argument('--json',type=Path)
    args=p.parse_args(); im=Image.open(args.image).convert('RGBA')
    value,rings=extract(im,args.alpha,args.tolerance,args.min_area)
    data={'ShadowShape':'Custom','ShadowVertices':value,'ShadowHullInsetPx':0}
    print(json.dumps(data,indent=2))
    if args.json: args.json.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
    if args.preview:
        draw=ImageDraw.Draw(im)
        for ring in rings:
            draw.line(ring+[ring[0]],fill=(255,180,0,255),width=2)
            for x,y in ring: draw.ellipse((x-2,y-2,x+2,y+2),fill=(255,230,100,255))
        im.save(args.preview)

if __name__=='__main__': main()
