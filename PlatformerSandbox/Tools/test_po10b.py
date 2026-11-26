import tempfile, unittest, struct
from pathlib import Path
import po_visual_regression as visual
import po_run_benchmarks as bench

class PO10BTests(unittest.TestCase):
    def test_matrix(self):
        cases=bench.po10b_plan()
        self.assertEqual(17,len(cases)); self.assertEqual(17,len({c['name'] for c in cases}))
        self.assertEqual(6,len(bench.B_QUICK)); self.assertEqual(12,len(bench.B_STANDARD))
        for c in cases:
            e=bench.environment(c,Path('result'))
            self.assertEqual(str(c['adaptive']),e['PS_PO_10B_ADAPTIVE'])
            self.assertEqual(str(c['bloom_scale']),e['PS_PO_10B_BLOOM_SCALE'])
    def test_visual_pixels(self):
        a=bytes([0,20,100,0,20,100]); b=bytes([0,22,100,0,20,200])
        m,d=visual.compare(a,b); self.assertEqual(100,m['max_channel']); self.assertEqual(.5,m['over_2_fraction']); self.assertEqual(255,d[-1]); self.assertEqual(0,visual.compare(a,a)[0]['rmse'])
        with self.assertRaises(ValueError): visual.compare(a,b[:-1])
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'a.png'; visual.png(p,2,1,a); self.assertEqual(b'\x89PNG\r\n\x1a\n',p.read_bytes()[:8]); self.assertEqual((2,1),struct.unpack('>II',p.read_bytes()[16:24]))
    def test_visual_plan(self):
        self.assertEqual(1,len(visual.plan())); self.assertEqual(3,len(visual.plan(True)))
        self.assertEqual(3,len(visual.plan()[0][2]))
if __name__=='__main__': unittest.main()
