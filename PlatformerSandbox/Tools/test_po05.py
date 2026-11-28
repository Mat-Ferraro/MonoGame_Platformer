import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch, Mock
import po05_validate as validate
import po_visual_regression as visual
import po_run_benchmarks as bench

ROOT=Path(__file__).resolve().parents[1]
class PO05Tests(unittest.TestCase):
    def test_three_modes_and_headless(self):
        steps=validate.plan(ROOT,'quick','all',True)
        self.assertEqual([s[2] for s in steps],['Reference','Indexed','Verify'])
        self.assertNotIn('--no-build',steps[0][1]);self.assertIn('--no-build',steps[1][1])
        self.assertEqual([s[0] for s in validate.plan(ROOT,'full','visual',False)],['build','visual'])
    def test_visual_pairs(self):
        for tier,count in [('quick',2),('standard',6),('full',6)]:
            pairs=visual.spatial_plan(tier);self.assertEqual(len(pairs),count)
            for name,ref,variants in pairs:
                _,candidate,kind=variants[0];self.assertEqual(kind,'strict')
                self.assertEqual(ref['spatial_mode'],'Reference');self.assertEqual(candidate['spatial_mode'],'Indexed')
                self.assertEqual({k:v for k,v in ref.items() if k not in ('name','spatial_mode')},{k:v for k,v in candidate.items() if k not in ('name','spatial_mode')})
                env=bench.environment(candidate,Path('capture'));self.assertEqual(env['PS_PO05_MODE'],'Indexed')
                self.assertEqual(env['PS_PO_GPU_SHADOW_BUFFERS'],'0');self.assertEqual(env['PS_PO_GPU_FUSED_LAYER'],'0')
                self.assertEqual(ref['soft'],0);self.assertEqual(candidate['soft'],0);self.assertEqual(env['PS_PO_10_SOFT'],'0')
    def test_lighting_capture_metadata_and_pixels(self):
        # Exercise the actual capture decoder, not just plan generation.
        _,reference,variants=visual.spatial_plan('quick')[1]
        for case in (reference,variants[0][1]):
            with self.subTest(mode=case['spatial_mode']), tempfile.TemporaryDirectory() as td:
                root=Path(td);dest=root/'capture'
                m={'Spatial mode':case['spatial_mode'],'Spatial queries':'65' if case['spatial_mode']=='Indexed' else '0',
                   'Width':'1280','Height':'720','Ticks':'60','Scene':'LIGHTING_QUALITY_LAB',
                   'Light scale':'100','Mask scale':'100','Bloom scale':'100','Adaptive':'False',
                   'Static fields':'False','Motion frozen':'False','Shadow edge':'Hard','Light width':'1280',
                   'Mask width':'1280','Original mask':'127','Quality':'High','Camera pan':'True',
                   'Camera convergence':'True','Material regions':'False','Light regions':'False',
                   'Shared fields':'False','Skip clears':'False','Cache capacity':'False','Lights':'8'}
                raw=bytes([1,2,3,255,4,5,6,255])*(1280*720//2)
                def launch(*args,**kwargs):
                    (dest/'visual.txt').write_text(''.join(f'{k}: {v}\n' for k,v in m.items()))
                    (dest/'frame.rgba').write_bytes(raw)
                    return Mock(wait=Mock(return_value=0))
                with patch.object(bench.gpuenv,'command',return_value=['mock-game']), patch.object(visual.subprocess,'Popen',side_effect=launch):
                    meta,rgb=visual.capture(case,dest,root,root/'mock.csproj',1)
                self.assertEqual(meta,m);self.assertEqual(rgb[:6],bytes([1,2,3,4,5,6]));self.assertTrue((dest/'frame.png').exists())
                self.assertFalse((dest/'frame.rgba').exists())
    def test_fixture_floor_and_platform_paths(self):
        project=json.loads((ROOT/'Content/Levels/EngineScalabilityLab.ldtk').read_text());level=project['levels'][0]
        self.assertEqual(level['identifier'],'ENGINE_SCALABILITY_LAB');self.assertEqual((level['pxWid'],level['pxHei']),(6144,2048))
        layers={x['__identifier']:x for x in level['layerInstances']};c=layers['Collisions'];w,h=c['__cWid'],c['__cHei'];cells=c['intGridCsv']
        self.assertEqual(len(cells),w*h);self.assertTrue(all(cells[48*w:49*w]))
        platforms=[e for e in layers['Entities']['entityInstances'] if e['__identifier']=='MovingPlatform'];self.assertEqual(len(platforms),2)
        for e in platforms:
            fields={f['__identifier']:f['__value'] for f in e['fieldInstances']};x,y=e['px'];dx,dy=fields['VelocityX']*32,-fields['VelocityY']*32
            left,right=min(x,x+dx),max(x,x+dx)+e['width'];top,bottom=min(y,y+dy),max(y,y+dy)+e['height']
            for row in range(h):
                for col in range(w):
                    if cells[row*w+col] and left<(col+1)*32 and right>col*32 and top<(row+1)*32 and bottom>row*32:
                        self.fail(f'Platform {fields["ColliderId"]} intersects cell {col},{row}')

if __name__=='__main__':unittest.main()
