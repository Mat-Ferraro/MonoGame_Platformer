"""PO10C runner plans, bounded warmup contract and fixture validation."""
import contextlib, csv, io, json, tempfile, unittest
from pathlib import Path
from unittest.mock import patch
import po_run_benchmarks as bench
import po_visual_regression as visual

class PO10CTests(unittest.TestCase):
    def plan(self,*args):
        out=io.StringIO()
        with patch.object(bench.sys,'argv',['runner','--po10c','--list',*args]),contextlib.redirect_stdout(out):
            self.assertEqual(0,bench.main())
        return json.loads(out.getvalue())
    def test_quick_and_dynamic(self):
        cases=self.plan('--tier','quick'); self.assertEqual(6,len(cases))
        self.assertTrue(all(c['adaptive_warmup'] and c['max_warmup_frames']==240 for c in cases))
        self.assertEqual([0,1], [c['lock_camera'] for c in cases[:2]])
        for c in self.plan():
            if c['motion']: self.assertFalse(c['adaptive_warmup']); self.assertEqual(120,c['max_warmup_frames'])
        self.assertTrue(all(not c['adaptive_warmup'] for c in self.plan('--fixed-warmup')))
    def test_fixture_env(self):
        for c in self.plan('--tier','quick'):
            e=bench.environment(c,Path('result'))
            self.assertEqual(c['fixture'],e['PS_PO_10C_FIXTURE']); self.assertEqual('1',e['PS_PO_AUTO_ADAPTIVE_WARMUP'])
        self.assertEqual(2,len(visual.plan(fixtures=True)))
        self.assertEqual('strict',visual.plan(fixtures=True)[0][2][0][2])
    def test_capture_validation(self):
        c=next(c for c in self.plan('--tier','quick') if c['name']=='adaptive_fixture_enabled')
        m={'Build':'Release','Debugger':'False','Scene':'LIGHTING_PERFORMANCE_LAB','Lighting quality':'High',
            'Lighting preset':'Phase21LabNeutral','Lab mode':'5','Isolation':'0','Motion frozen':'True','Overlay':'True',
            'Fixed timestep':'False','Requested VSync':'False','Render cap':'0','Automatic case':c['name'],
            'PO B area samples':'0','PO 10 light scale':'50','PO 10 mask scale':'50','PO 10 shadow edge':'Hard',
            'PO 10B bloom scale':'100','PO 10B adaptive':'True','PO 10B static fields':'False',
            'Warmup frames':'72','Warmup minimum':'60','Warmup maximum':'240','Warmup status':'stable','Recorded frames':'120',
            'Fixture':'adaptive','Fixture coordinates':'world_fixed_v1','Camera locked':'True'}
        for flags,meta,key in [(bench.FLAGS,bench.META,'flags'),(bench.BFLAGS,bench.BMETA,'bflags'),(bench.TFLAGS,bench.TMETA,'tflags')]:
            m.update({k:str(bool(c[key][f])) for f,k in zip(flags,meta)})
        cols='update_cpu_ms previous_draw_cpu_ms present_wall_ms simulation_cpu_ms diagnostic_cpu_ms cpu_submit_ms material_geometry_cpu_ms layer_geometry_cpu_ms debug_hud_cpu_ms scene_draw_cpu_ms thread_alloc_bytes draw_calls target_changes logical_lights area_sample_evaluations persistent_mask_hits shared_mask_fallbacks mask_fallback_capacity mask_fallback_current_frame soft_filter_pixels gc0 gc1 gc2 estimated_target_mb soft_filter_passes shared_mask_targets layer_builds layer_reuses static_field_hits bloom_pixels bloom_passes view_changed lights_changed mask_invalidated mask_evictions player_changed logical_area_samples shared_mask_misses'.split()
        row={k:1 for k in cols+list(bench.TCOUNTERS)}
        row.update(width=1280,height=720,fixed_step=0,vsync=0,optimization_mask=127,isolation=0,freeze_lights=1,preceding_frame_ms=20,light_width=640,light_height=360,mask_width=640,mask_height=360,view_changed=0,lights_changed=0,mask_invalidated=0,shared_mask_misses=0,logical_area_samples=25,area_sample_counts='1|3|5|7|9')
        with tempfile.TemporaryDirectory() as t:
            dest=Path(t)
            def meta(): (dest/'x-metadata.txt').write_text('\n'.join(f'{k}: {v}' for k,v in m.items()))
            def data():
                with (dest/'x.csv').open('w',newline='') as f:
                    w=csv.DictWriter(f,fieldnames=['frame']+list(row)); w.writeheader(); w.writerows(dict(frame=i,**row) for i in range(1,121))
            def trace(n,ready): (dest/'warmup_trace.tsv').write_text('frame\tready\n'+''.join(f'{i}\t{ready}\n' for i in range(1,n+1)))
            meta(); data(); trace(72,1)
            r=bench.read_result(c,dest); self.assertEqual(72,r['warmup_frames']); self.assertEqual(120,r['steady_frames'])
            trace(72,0)
            with self.assertRaisesRegex(ValueError,'stable warmup streak'): bench.read_result(c,dest)
            trace(72,1); row['area_sample_counts']='9|9|9|9|9'; data()
            with self.assertRaisesRegex(ValueError,'expected sample counts'): bench.read_result(c,dest)
            row['area_sample_counts']='1|3|5|7|9'; data(); m['Warmup status']='max_reached'; meta()
            with self.assertRaisesRegex(ValueError,'early maximum'): bench.read_result(c,dest)
if __name__=='__main__': unittest.main()
