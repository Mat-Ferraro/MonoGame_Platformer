"""Python-side plan and authored fixture checks; C# correctness is tested separately."""
import json
import math
from pathlib import Path
import unittest
import tempfile
import csv
import po_run_benchmarks as bench
import po_visual_regression as visual
import pb01_validate as validate

ROOT=Path(__file__).resolve().parents[1]

class PB01ToolTests(unittest.TestCase):
    def test_benchmark_plans(self):
        for tier,n in [('quick',2),('standard',4),('full',4)]:
            cases=bench.pb01_baseline_plan(tier)
            self.assertEqual(n,len(cases))
            self.assertEqual({'Reference','Indexed'},{c['spatial_mode'] for c in cases})
            for c in cases:
                self.assertEqual('ENGINE_INTEGRATION_LAB',c['scene'])
                self.assertEqual(1,c['motion']);self.assertEqual(0,c['isolation'])
                self.assertFalse(any(c.get('gpu_'+f,0) for f in bench.GPU_EXTRA_FLAGS))
                self.assertEqual(c['spatial_mode'],bench.environment(c,Path('capture'))['PS_PO05_MODE'])
            for v in {c['vsync'] for c in cases}:
                pair=[{k:x for k,x in c.items() if k not in ('name','spatial_mode')} for c in cases if c['vsync']==v]
                self.assertEqual(pair[0],pair[1])

    def test_visual_plan_does_not_replace_po05(self):
        self.assertEqual(2,len(visual.spatial_plan('quick')))
        for tier,n in [('quick',1),('standard',3),('full',3)]:
            cases=visual.spatial_plan(tier,('PHYSICS_INTERACTION_LAB',))
            self.assertEqual(n,len(cases))
            for name,ref,variants in cases:
                self.assertEqual('PHYSICS_INTERACTION_LAB',ref['scene'])
                self.assertEqual('Reference',ref['spatial_mode'])
                self.assertEqual('Indexed',variants[0][1]['spatial_mode'])
                self.assertEqual('strict',variants[0][2])

    def test_dynamic_fixture_geometry_and_visual_coverage(self):
        fixture=json.loads((ROOT/'Content/Physics/DynamicBodies.json').read_text())
        for level,filename in [('PHYSICS_INTERACTION_LAB','PhysicsInteractionLab'),('ENGINE_INTEGRATION_LAB','EngineIntegrationLab')]:
            data=json.loads((ROOT/'Content/Levels'/f'{filename}.ldtk').read_text())['levels'][0]
            grid=next(x for x in data['layerInstances'] if x['__identifier']=='Collisions');w=grid['__cWid'];g=grid['__gridSize']
            solid=[(i%w*g,i//w*g,(i%w+1)*g,(i//w+1)*g) for i,v in enumerate(grid['intGridCsv']) if v]
            entries=fixture['Levels'][level];self.assertGreaterEqual(len(entries),5);self.assertEqual(len(entries),len({e['Id'] for e in entries}))
            for e in entries:
                x=e['X']*32;y=data['pxHei']-e['Y']*32;hx=e.get('HalfWidth',.5)*32;hy=e.get('HalfHeight',.5)*32
                self.assertFalse(any(x-hx<b[2] and x+hx>b[0] and y-hy<b[3] and y+hy>b[1] for b in solid),e)
        for tier,n in [('quick',5),('full',10)]:
            plan=visual.pb_dynamic_plan(tier);self.assertEqual(n,len(plan))
            for _,ref,variants in plan:
                self.assertTrue(ref['pb_dynamic_visual']);self.assertEqual(0,ref['camera_pan'])
            self.assertTrue(any(ref['scene']=='ENGINE_INTEGRATION_LAB' and ref['pb_visual_body']==13 for _,ref,_ in plan))
        for c in bench.pb01_baseline_plan('full'):
            env=bench.environment(c,Path('capture'));self.assertEqual('1800',env['PS_PB01_RECORDED_TICKS']);self.assertEqual('300',env['PS_PB01_WARMUP_TICKS'])

    def test_coupled_fixture_pairs_and_capture_target(self):
        fixture=json.loads((ROOT/'Content/Physics/DynamicBodies.json').read_text())['Levels']
        for level,base,count in [('PHYSICS_INTERACTION_LAB',6,10),('ENGINE_INTEGRATION_LAB',9,14)]:
            entries=fixture[level];self.assertEqual(count,len(entries))
            self.assertTrue(all(set(e['CollisionMask'].split(', '))=={'Terrain','Platform','Dynamic','Character'} for e in entries))
            def box(e):
                x,y=e['X'],e['Y'];hx,hy=e.get('HalfWidth',.5),e.get('HalfHeight',.5)
                return x-hx,y-hy,x+hx,y+hy
            for i,a in enumerate(entries):
                aa=box(a)
                for b in entries[i+1:]:
                    bb=box(b);self.assertFalse(aa[0]<bb[2] and aa[2]>bb[0] and aa[1]<bb[3] and aa[3]>bb[1],(level,a,b))
            a,b=[next(e for e in entries if e['Id']==n) for n in (base,base+1)]
            self.assertEqual(a['X'],b['X']);self.assertEqual(box(a)[3],box(b)[1])
            capture=next(ref for _,ref,_ in visual.pb_dynamic_plan('quick') if ref['scene']==level)
            self.assertEqual(base,capture['pb_visual_body'])

    def test_platform_enabled_fixtures_clear_starting_geometry(self):
        fixtures=json.loads((ROOT/'Content/Physics/DynamicBodies.json').read_text())['Levels']
        for filename,level,carrier in [('PhysicsInteractionLab','PHYSICS_INTERACTION_LAB',9),('EngineIntegrationLab','ENGINE_INTEGRATION_LAB',12)]:
            data=json.loads((ROOT/f'Content/Levels/{filename}.ldtk').read_text())['levels'][0]
            platforms=[]
            for ly in data['layerInstances']:
                for e in ly['entityInstances']:
                    if e['__identifier']=='MovingPlatform':
                        x,y=e['px'];platforms.append((x/32,(data['pxHei']-y-e['height'])/32,(x+e['width'])/32,(data['pxHei']-y)/32))
            for e in fixtures[level]:
                x,y=e['X'],e['Y'];hx=e.get('HalfWidth',.5);hy=e.get('HalfHeight',.5)
                self.assertFalse(any(x-hx<b[2] and x+hx>b[0] and y-hy<b[3] and y+hy>b[1] for b in platforms),(level,e))
                if e['Id']==carrier:
                    self.assertTrue(any(b[0]<=x-hx and x+hx<=b[2] and y-hy==b[3] for b in platforms),(level,e))

    def test_character_enabled_fixtures_clear_authored_spawn(self):
        fixtures=json.loads((ROOT/'Content/Physics/DynamicBodies.json').read_text())['Levels']
        for filename,level in [('PhysicsInteractionLab','PHYSICS_INTERACTION_LAB'),('EngineIntegrationLab','ENGINE_INTEGRATION_LAB')]:
            data=json.loads((ROOT/f'Content/Levels/{filename}.ldtk').read_text())['levels'][0]
            spawn=next(e for layer in data['layerInstances'] for e in layer['entityInstances'] if e['__identifier']=='PlayerSpawn')
            x=(spawn['px'][0]+spawn['width']/2)/32; y=(data['pxHei']-spawn['px'][1]-spawn['height'])/32
            for e in fixtures[level]:
                self.assertIn('Character',e['CollisionMask'])
                hx=e.get('HalfWidth',.5);hy=e.get('HalfHeight',.5)
                self.assertFalse(x-.4<e['X']+hx and x+.4>e['X']-hx and y<e['Y']+hy and y+1.6>e['Y']-hy,(level,e))

    def test_absolute_tick_window_and_exercised_workload(self):
        c=bench.pb01_baseline_plan('quick')[0]
        m={'PB measurement start tick':'180','PB measurement end tick':'480','PB warmup start tick':'23',
           'PB tick benchmark':'True','PB workload version':'7','PB committed coupled event iterations':'3','PB platform recovery attempts':'0','PB platform prop contacts':'1','PB prop pair impacts':'1','PB measured platform commands':'5',
           'PB measured deaths':'1','PB measured respawns':'1'}
        self.assertEqual(300,bench.validate_pb_tick_window(c,m))
        self.assertEqual('1',bench.environment(c,Path('capture'))['PS_PB01_PLATFORM_SCRIPT'])
        for k,v in [('PB measurement start tick','181'),('PB measurement end tick','481'),('PB warmup start tick','121'),('PB measured platform commands','0'),('PB measured respawns','0'),('PB prop pair impacts','0'),('PB platform prop contacts','0'),('PB committed coupled event iterations','0'),('PB platform recovery attempts','-1'),('PB workload version','6')]:
            with self.assertRaises(ValueError):bench.validate_pb_tick_window(c,{**m,k:v})

    def test_crush_lifecycle_authored_defaults(self):
        data=json.loads((ROOT/'Content/Physics/CrushLifecycle.json').read_text())
        self.assertEqual(1,data['Version']);self.assertEqual('Death',data['Crush'])
        self.assertEqual('Immediate',data['Respawn']['Mode']);self.assertIsNone(data['Respawn']['MaximumRespawns'])
        self.assertEqual({},data['Levels'])

    def test_runner_modes(self):
        self.assertEqual(['tests_reference','tests_indexed','tests_verify','build','feature_visual','integrated_benchmark'],[s[0] for s in validate.plan(ROOT,'quick')])
        self.assertEqual(3,len(validate.plan(ROOT,'quick',headless=True)))
        self.assertEqual(5,len(validate.plan(ROOT,'quick',no_bench=True)))
        self.assertEqual(['build','integrated_benchmark'],[s[0] for s in validate.plan(ROOT,'full',bench_only=True)])
        self.assertIn('--allow-battery',validate.plan(ROOT,'quick',bench_only=True,allow_battery=True)[-1][1])

    def test_material_data(self):
        data=json.loads((ROOT/'Content/Physics/Materials.json').read_text())
        self.assertEqual(1,data['Version']); self.assertEqual('GeometricMean',data['FrictionCombine']);self.assertEqual('Maximum',data['RestitutionCombine'])
        names=set()
        for m in data['Materials']:
            self.assertNotIn(m['Id'],names);names.add(m['Id'])
            self.assertTrue(all(math.isfinite(m[k]) for k in ('StaticFriction','DynamicFriction','Restitution','AreaDensity','LinearDamping')))
            self.assertGreaterEqual(m['StaticFriction'],m['DynamicFriction']);self.assertGreaterEqual(m['DynamicFriction'],0)
            self.assertTrue(0<=m['Restitution']<=1);self.assertGreater(m['AreaDensity'],0);self.assertGreaterEqual(m['LinearDamping'],0)
        self.assertEqual({'Default','Wood','Metal','Ice','Stone','Rubber','Slime'},names)

    def test_dedicated_geometry(self):
        d=json.loads((ROOT/'Content/Levels/PhysicsInteractionLab.ldtk').read_text());l=d['levels'][0]
        self.assertEqual('PHYSICS_INTERACTION_LAB',l['identifier'])
        layers={x['__identifier']:x for x in l['layerInstances']};c=layers['Collisions'];g=c['__gridSize'];w=c['__cWid'];h=c['__cHei']
        self.assertEqual(l['pxWid'],g*w);self.assertEqual(l['pxHei'],g*h);self.assertEqual(w*h,len(c['intGridCsv']))
        occupied=[(i%w*g,i//w*g,(i%w+1)*g,(i//w+1)*g) for i,v in enumerate(c['intGridCsv']) if v]
        def overlap(a,b):return a[0]<b[2] and a[2]>b[0] and a[1]<b[3] and a[3]>b[1]
        entities=layers['Entities']['entityInstances'];platforms=[e for e in entities if e['__identifier']=='MovingPlatform'];self.assertEqual(3,len(platforms))
        ids=set();sweeps=[]
        for e in platforms:
            f={x['__identifier']:x['__value'] for x in e['fieldInstances']};self.assertNotIn(f['ColliderId'],ids);ids.add(f['ColliderId'])
            x,y=e['px'];dx=f['VelocityX']*32;dy=-f['VelocityY']*32;a=(min(x,x+dx),min(y,y+dy),max(x,x+dx)+e['width'],max(y,y+dy)+e['height'])
            self.assertFalse(any(overlap(a,b) for b in occupied),f'platform path penetrates terrain: {f["ColliderId"]}')
            self.assertTrue(0<=a[0]<a[2]<=l['pxWid'] and 0<=a[1]<a[3]<=l['pxHei'])
            self.assertFalse(any(overlap(a,b) for b in sweeps));sweeps.append(a)
        # Legacy LDtk VelocityX/Y fields are actually total travel offsets in world units.
        self.assertEqual(704,sweeps[0][2]);self.assertEqual(896,sweeps[1][3])
        spawn=next(e for e in entities if e['__identifier']=='PlayerSpawn');x,y=spawn['px'];box=(x,y-64,x+spawn['width'],y+spawn['height'])
        self.assertFalse(any(overlap(box,b) for b in occupied+sweeps))

    def test_integrated_level_contains_existing_systems_and_cannot_exit(self):
        d=json.loads((ROOT/'Content/Levels/EngineIntegrationLab.ldtk').read_text());self.assertEqual(1,len(d['levels']));l=d['levels'][0]
        self.assertEqual('ENGINE_INTEGRATION_LAB',l['identifier']);self.assertFalse(l['__neighbours'])
        entities=[e for a in l['layerInstances'] for e in a['entityInstances']];kinds={e['__identifier'] for e in entities}
        self.assertTrue({'PlayerSpawn','MovingPlatform','Checkpoint','WindZone','ParticleZone','LightSource','TerrainArtwork','ScenerySprite','PaintedSprite'}<=kinds)
        self.assertNotIn('LevelExit',kinds)
        self.assertEqual(len(entities),len({e['iid'] for e in entities}))

    def test_showcase_art_matches_collision_and_lights(self):
        import pb01_showcase_art as art
        level=json.loads(art.LEVEL.read_text())['levels'][0]
        grid=next(l for l in level['layerInstances'] if l['__identifier']=='Collisions')
        g=grid['__gridSize']; w=grid['__cWid']; cells=grid['intGridCsv']
        entities=[e for l in level['layerInstances'] for e in l['entityInstances']]
        solids=[e for e in entities if art.fields(e).get('Plane')=='Terrain']
        self.assertEqual(len(art.rectangles(grid)),len(solids))
        occupied=set()
        for e in solids:
            f=art.fields(e); x,y=e['px']; width,height=e['width'],e['height']
            self.assertTrue(all(v%g==0 for v in (x,y,width,height)))
            self.assertEqual([0,0],e['__pivot'])
            self.assertEqual((1,0,0,0,False,False,True),(f['Opacity'],f['RotationDeg'],f['ParallaxX'],f['ParallaxY'],f['FlipX'],f['FlipY'],f['CastsShadows']))
            rect=(x//g,y//g,width//g,height//g)
            self.assertIn(rect,art.rectangles(grid))
            self.assertEqual(art.stone(grid,rect),(art.LEVEL.parent/f['Image']).read_bytes())
            for cy in range(rect[1],rect[1]+rect[3]):
                for cx in range(rect[0],rect[0]+rect[2]):
                    self.assertNotIn(cy*w+cx,occupied);occupied.add(cy*w+cx)
        self.assertEqual({i for i,v in enumerate(cells) if v},occupied)
        lights=[e for e in entities if e['__identifier']=='LightSource']
        markers=[e for e in entities if art.fields(e).get('Image')==art.PREFIX+'Lamp.png']
        self.assertEqual(len(art.LIGHTS),len(lights));self.assertEqual(len(lights),len(markers))
        for light in lights:
            x,y=light['px']; f=art.fields(light)
            self.assertTrue(all(not cells[cy*w+cx] for cy in range(y//g,(y+23)//g+1) for cx in range(x//g,(x+23)//g+1)))
            self.assertTrue(any(m['px']==light['px'] and art.fields(m)['Tint']==f['Color'] and not art.fields(m)['CastsShadows'] for m in markers))
        self.assertTrue(any(art.fields(e)['CastShadows'] for e in lights))
        self.assertTrue(any(not art.fields(e)['CastShadows'] for e in lights))
        for i in occupied:
            if i>=w and cells[i-w]:continue
            x=(i%w+.5)*g;y=(i//w)*g
            self.assertTrue(any(math.hypot(x-(e['px'][0]+12),y-(e['px'][1]+12))<art.fields(e)['RadiusWorld']*g for e in lights),(x,y))
        for e in entities:
            f=art.fields(e)
            if f.get('Plane') in ('Background','Foreground'):
                self.assertFalse(f['CastsShadows'])
                if e not in markers:self.assertLessEqual(f['Opacity'],.30)

    def test_integrated_elevator_sweep_and_crush_station(self):
        level=json.loads((ROOT/'Content/Levels/EngineIntegrationLab.ldtk').read_text())['levels'][0]
        grid=next(x for x in level['layerInstances'] if x['__identifier']=='Collisions');w=grid['__cWid'];g=grid['__gridSize']
        solid=[(i%w*g,i//w*g,(i%w+1)*g,(i//w+1)*g) for i,v in enumerate(grid['intGridCsv']) if v]
        def overlaps(a,b):return a[0]<b[2] and a[2]>b[0] and a[1]<b[3] and a[3]>b[1]
        paths={}
        for e in (e for l in level['layerInstances'] for e in l['entityInstances'] if e['__identifier']=='MovingPlatform'):
            f={x['__identifier']:x['__value'] for x in e['fieldInstances']};x,y=e['px'];dx=f['VelocityX']*32;dy=-f['VelocityY']*32
            swept=(min(x,x+dx),min(y,y+dy),max(x,x+dx)+e['width'],max(y,y+dy)+e['height'])
            self.assertFalse(any(overlaps(swept,b) for b in solid));self.assertFalse(any(overlaps(swept,b) for b in paths.values()));paths[f['ColliderId']]=swept
        self.assertEqual({8001,8002},set(paths))
        entries=json.loads((ROOT/'Content/Physics/DynamicBodies.json').read_text())['Levels']['ENGINE_INTEGRATION_LAB'];carrier,blocker=[next(e for e in entries if e['Id']==i) for i in (13,14)]
        for e in (carrier,blocker):self.assertEqual('StopPlatform',e['CrushResponse'])
        def box(e):return ((e['X']-.5)*32,level['pxHei']-(e['Y']+.5)*32,(e['X']+.5)*32,level['pxHei']-(e['Y']-.5)*32)
        self.assertEqual(box(carrier)[3],paths[8002][1]);self.assertTrue(overlaps(box(blocker),paths[8002]));self.assertTrue(any(box(blocker)[3]==b[1] and box(blocker)[0]<b[2] and box(blocker)[2]>b[0] for b in solid))

    def test_closeout_evidence_rejects_short_or_missing_runs(self):
        with tempfile.TemporaryDirectory() as path:
            target=Path(path)
            for level in ('PHYSICS_INTERACTION_LAB','ENGINE_INTEGRATION_LAB'):
                (target/(level+'_closeout_replay.csv')).write_text('level,mode,ticks,mean_world_step_ms,p95_world_step_ms,allocated_bytes_per_tick,committed_dynamic_cpu_ms_per_tick,platform_stops,recovery_attempts,contacts,platform_contacts,committed_coupled_event_iterations,deaths,respawns\n'+level+',Indexed,10000,1,2,100,0.5,3,3,5,5,5,1,1\n')
            scale=target/'PB01_closeout_scaling.csv'
            scale.write_text('mode,body_count,ticks,mean_step_ms,p95_step_ms,allocated_bytes_per_tick,contacts,pair_contacts,committed_coupled_event_iterations\n'+''.join(f'Indexed,{n},600,1,2,100,3,3,3\n' for n in (8,32,64)))
            validate.validate_closeout_evidence(target,'full','Indexed')
            with self.assertRaises(ValueError):validate.validate_closeout_evidence(target,'quick','Indexed')
            with self.assertRaises(ValueError):validate.validate_closeout_evidence(target,'full','Verify')
            scale.unlink()
            with self.assertRaises(OSError):validate.validate_closeout_evidence(target,'full','Indexed')

    def test_benchmark_summary_is_separate_from_gpu_experiments(self):
        with tempfile.TemporaryDirectory() as path:
            out=Path(path)
            result=dict(name='pb01_indexed_uncapped',status='ok',fps=60,p95_ms=18,simulation_cpu_ms_mean=.5,previous_draw_cpu_ms_mean=2,thread_alloc_bytes_mean=100,simulation_ticks_between_recorded_frames=60,drawn_particles_mean=4,logical_lights_mean=2,gpu_gl_renderer='test adapter')
            bench.write_summary(out,[result])
            with (out/'pb01_baseline.csv').open(newline='') as f:rows=list(csv.DictReader(f))
            self.assertEqual(1,len(rows));self.assertEqual('60',rows[0]['median_simulation_ticks']);self.assertEqual('test adapter',rows[0]['native_renderer'])

if __name__=='__main__':unittest.main()
