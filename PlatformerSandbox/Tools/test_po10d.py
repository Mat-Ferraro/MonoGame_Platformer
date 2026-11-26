"""PO10D camera-pair plans and visual sequence validation."""
import contextlib, io, json, unittest
from pathlib import Path
from unittest.mock import patch
import po_run_benchmarks as bench
import po_visual_regression as visual

class PO10DTests(unittest.TestCase):
    def test_quick_plan(self):
        output=io.StringIO()
        with patch.object(bench.sys,'argv',['runner','--po10d','--tier','quick','--list']), contextlib.redirect_stdout(output):
            self.assertEqual(0,bench.main())
        cases=json.loads(output.getvalue()); self.assertEqual(5,len(cases))
        self.assertEqual([0,1],[c['camera_snap'] for c in cases[:2]])
        self.assertTrue(all(c['adaptive_warmup'] for c in cases[:2]))
        self.assertTrue(all(not c['adaptive_warmup'] for c in cases[2:]))
        self.assertEqual([100,100,50],[c['light_scale'] for c in cases[2:]])
        for c in cases:
            env=bench.environment(c,Path('out'))
            self.assertEqual(str(c['camera_pan']),env['PS_PO_10D_PAN_CAMERA'])
            self.assertEqual(str(c['camera_snap']),env['PS_PO_10D_CAMERA_SNAP'])
    def test_temporal_plan(self):
        groups=visual.plan(temporal=True)
        self.assertEqual([30,60,150],[base['visual_ticks'] for _,base,_ in groups])
        for _,base,variants in groups:
            self.assertEqual(['strict','quality'],[kind for _,_,kind in variants])
            self.assertTrue(all(c['camera_pan']==1 and c['visual_ticks']==base['visual_ticks'] for _,c,_ in variants))

if __name__=='__main__': unittest.main()
