"""Host-side checks for the PO10A plan and requested/actual target validation."""
import csv
import tempfile
from pathlib import Path
import unittest
import po_run_benchmarks as runner

class PO10ATests(unittest.TestCase):
    def test_plan_and_environment(self):
        plan=runner.po10a_plan()
        self.assertEqual(26,len(plan))
        self.assertEqual(len(plan),len({c['name'] for c in plan}))
        for c in plan:
            env=runner.environment(c,Path('capture'))
            self.assertEqual(str(c['light_scale']),env['PS_PO_10_LIGHT_SCALE'])
            self.assertEqual(str(c['mask_scale']),env['PS_PO_10_MASK_SCALE'])
            for f in runner.TFLAGS:
                self.assertEqual(str(c['tflags'][f]),env['PS_PO_10_'+f])
        baseline=plan[0]
        self.assertEqual(100,baseline['light_scale']); self.assertEqual(100,baseline['mask_scale'])
        self.assertFalse(any(baseline['tflags'].values()))

    def test_actual_sizes_and_metadata(self):
        c=next(c for c in runner.po10a_plan() if c['name']=='combined_50')
        metadata={'Build':'Release','Debugger':'False','Scene':'LIGHTING_PERFORMANCE_LAB','Lighting quality':'High',
                  'Lighting preset':'Phase21LabNeutral','Lab mode':'5','Isolation':'0','Motion frozen':'True','Overlay':'True',
                  'Fixed timestep':'False','Requested VSync':'False','Render cap':'0','Automatic case':c['name'],
                  'PO B area samples':'0','PO 10 light scale':'50','PO 10 mask scale':'50','PO 10 shadow edge':'Hard'}
        for f,k in zip(runner.FLAGS,runner.META): metadata[k]=str(bool(c['flags'][f]))
        for f,k in zip(runner.BFLAGS,runner.BMETA): metadata[k]=str(bool(c['bflags'][f]))
        for f,k in zip(runner.TFLAGS,runner.TMETA): metadata[k]=str(bool(c['tflags'][f]))
        cols='update_cpu_ms previous_draw_cpu_ms present_wall_ms simulation_cpu_ms diagnostic_cpu_ms cpu_submit_ms material_geometry_cpu_ms layer_geometry_cpu_ms debug_hud_cpu_ms scene_draw_cpu_ms thread_alloc_bytes draw_calls target_changes logical_lights area_sample_evaluations persistent_mask_hits shared_mask_fallbacks mask_fallback_capacity mask_fallback_current_frame soft_filter_pixels gc0 gc1 gc2 estimated_target_mb soft_filter_passes shared_mask_targets layer_builds layer_reuses'.split()
        base={k:1 for k in cols+list(runner.TCOUNTERS)}
        base.update(width=1280,height=720,fixed_step=0,vsync=0,optimization_mask=127,isolation=0,freeze_lights=1,preceding_frame_ms=20,light_width=640,light_height=360,mask_width=640,mask_height=360)
        with tempfile.TemporaryDirectory() as tmp:
            dest=Path(tmp)
            (dest/'x-metadata.txt').write_text('\n'.join(f'{k}: {v}' for k,v in metadata.items()))
            def write():
                with (dest/'x.csv').open('w',newline='') as f:
                    w=csv.DictWriter(f,fieldnames=['frame']+list(base));w.writeheader()
                    for i in range(1,301): w.writerow(dict(frame=i,**base))
            write(); self.assertEqual(50,runner.read_result(c,dest)['fps'])
            base['mask_width']=1280; write()
            with self.assertRaisesRegex(ValueError,'wrong actual mask_width'): runner.read_result(c,dest)

if __name__=='__main__': unittest.main()
