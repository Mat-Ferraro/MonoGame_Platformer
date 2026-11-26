"""Tests for shorter capture plans and strict timing metadata validation."""
import contextlib
import csv
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import po_run_benchmarks as runner

class TierTests(unittest.TestCase):
    def plan(self,*args):
        output=io.StringIO()
        with patch.object(runner.sys,'argv',['runner','--po10a','--list',*args]), contextlib.redirect_stdout(output):
            self.assertEqual(0,runner.main())
        return json.loads(output.getvalue())

    def test_tiers(self):
        for tier,n,w,r in [('quick',6,60,120),('standard',12,90,180),('full',26,120,300)]:
            cases=self.plan('--tier',tier)
            self.assertEqual(n,len(cases))
            self.assertTrue(all(c['warmup_frames']==w and c['recorded_frames']==r for c in cases))
            self.assertEqual(len(cases),len({c['name'] for c in cases}))
        self.assertEqual(11,len(self.plan('--tier','standard','--no-wide')))
        self.assertEqual(3,len(self.plan('--smoke')))

    def test_overrides_and_env(self):
        c=self.plan('--case','combined_50','--warmup-frames','45','--recorded-frames','90')[0]
        env=runner.environment(c,Path('capture'))
        self.assertEqual('45',env['PS_PO_AUTO_WARMUP_FRAMES'])
        self.assertEqual('90',env['PS_PO_AUTO_RECORDED_FRAMES'])
        with self.assertRaises(SystemExit),contextlib.redirect_stderr(io.StringIO()):
            self.plan('--recorded-frames','59')

    def test_capture_length_and_metadata(self):
        c=self.plan('--tier','quick')[0]
        m={'Build':'Release','Debugger':'False','Scene':'LIGHTING_PERFORMANCE_LAB','Lighting quality':'High',
           'Lighting preset':'Phase21LabNeutral','Lab mode':'5','Isolation':'0','Motion frozen':'True','Overlay':'True',
           'Fixed timestep':'False','Requested VSync':'False','Render cap':'0','Automatic case':c['name'],
           'PO B area samples':'0','PO 10 light scale':'100','PO 10 mask scale':'100','PO 10 shadow edge':'Hard',
           'Warmup frames':'60','Recorded frames':'120'}
        for flags,meta,key in [(runner.FLAGS,runner.META,'flags'),(runner.BFLAGS,runner.BMETA,'bflags'),(runner.TFLAGS,runner.TMETA,'tflags')]:
            for f,k in zip(flags,meta): m[k]=str(bool(c[key][f]))
        cols='update_cpu_ms previous_draw_cpu_ms present_wall_ms simulation_cpu_ms diagnostic_cpu_ms cpu_submit_ms material_geometry_cpu_ms layer_geometry_cpu_ms debug_hud_cpu_ms scene_draw_cpu_ms thread_alloc_bytes draw_calls target_changes logical_lights area_sample_evaluations persistent_mask_hits shared_mask_fallbacks mask_fallback_capacity mask_fallback_current_frame soft_filter_pixels gc0 gc1 gc2 estimated_target_mb soft_filter_passes shared_mask_targets layer_builds layer_reuses'.split()
        row={k:1 for k in cols+list(runner.TCOUNTERS)}
        row.update(width=1280,height=720,fixed_step=0,vsync=0,optimization_mask=127,isolation=0,freeze_lights=1,preceding_frame_ms=20,light_width=1280,light_height=720,mask_width=1280,mask_height=720)
        with tempfile.TemporaryDirectory() as tmp:
            dest=Path(tmp)
            def meta(): (dest/'x-metadata.txt').write_text('\n'.join(f'{k}: {v}' for k,v in m.items()))
            def data(n,duplicate=False):
                with (dest/'x.csv').open('w',newline='') as f:
                    w=csv.DictWriter(f,fieldnames=['frame']+list(row)); w.writeheader()
                    for i in range(1,n+1): w.writerow(dict(frame=1 if duplicate and i==n else i,**row))
            meta(); data(120)
            r=runner.read_result(c,dest); self.assertEqual((120,60,'quick'),(r['frames'],r['warmup_frames'],r['tier']))
            data(119)
            with self.assertRaisesRegex(ValueError,'expected 120'): runner.read_result(c,dest)
            data(120,True)
            with self.assertRaisesRegex(ValueError,'missing/duplicate'): runner.read_result(c,dest)
            data(120); m['Warmup frames']='120'; meta()
            with self.assertRaisesRegex(ValueError,'metadata Warmup frames'): runner.read_result(c,dest)

if __name__=='__main__': unittest.main()
