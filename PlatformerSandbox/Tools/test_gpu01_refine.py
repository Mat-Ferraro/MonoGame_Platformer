import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch
import po_run_benchmarks as b
import po_visual_regression as v
import gpu01_environment as e

class RefinementTests(unittest.TestCase):
    def test_strict_native_renderer(self):
        for native in ('','UNKNOWN','NOT PROBED','UNAVAILABLE / NO CURRENT GL CONTEXT','Intel'):
            with self.assertRaises(ValueError): b.validate_adapter('RTX 5070','RTX 5070',native,strict=True)
        b.validate_adapter('RTX 5070','RTX 5070','NVIDIA RTX 5070',strict=True)
    def test_matched_labs_and_flags(self):
        cases=b.gpu_refine_plan('quick')
        self.assertEqual(len(cases),13)
        for first,second in zip(cases[:5],cases[5:10]):
            self.assertEqual({k for k in first if first[k]!=second[k]},{'name','scene'})
        for c in cases[-3:]:
            env=b.environment(c,Path('capture'))
            for f in ('scoped_binds','material_clear'):
                self.assertEqual(env['PS_PO_GPU_'+f.upper()],str(c['gpu_'+f]))
    def test_full_particle_stress_is_isolated(self):
        cases=b.gpu_refine_plan('full')
        self.assertEqual(len({c['name'] for c in cases}),len(cases))
        stress=[c for c in cases if c.get('gpu_stress')]
        self.assertEqual({c['gpu_stress'] for c in stress},{5000,10000,25000,50000})
        self.assertEqual(len(stress),12)
        self.assertTrue(all(c['isolation']==8 for c in stress))
    def test_visual_policy(self):
        self.assertEqual(v.quality_verdict('strict',{'max_channel':1}),'DIFFERENCES_FOUND')
        self.assertEqual(v.quality_verdict('tolerance',{'max_channel':1}),'EQUIVALENT_WITHIN_TOLERANCE')
        self.assertEqual(v.quality_verdict('tolerance',{'max_channel':2}),'REVIEW_REQUIRED')
        self.assertEqual(v.quality_verdict('strict',{'max_channel':0}),'EXACT_MATCH')
        self.assertEqual(sum(2+len(c) for _,_,c in v.refinement_plan()),32)
    def test_power_guard(self):
        with tempfile.TemporaryDirectory() as folder:
            dest=Path(folder); proc=Mock(); proc.wait.return_value=0
            with patch.object(e,'power_source',side_effect=['AC','AC']):
                self.assertEqual(e.wait_checked(proc,5,dest,True,'AC','test'),0)
            with patch.object(e,'power_source',side_effect=['AC','battery']):
                with self.assertRaisesRegex(ValueError,'Power source changed'): e.wait_checked(proc,5,dest,True,'AC','test')
            with self.assertRaisesRegex(ValueError,'requires AC'): e.wait_checked(proc,5,dest,True,'battery','test')
            self.assertTrue((dest/'power.json').is_file())

if __name__=='__main__': unittest.main()
