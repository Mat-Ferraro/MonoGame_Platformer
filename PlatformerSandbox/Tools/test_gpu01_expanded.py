import unittest
import po_run_benchmarks as b
import po_visual_regression as v
from pathlib import Path
from unittest.mock import patch
class ExpandedGpuTests(unittest.TestCase):
    def test_quick_covers_every_new_experiment(self):
        plan=b.gpu_expanded_plan('quick')
        self.assertEqual(len(plan),12)
        self.assertTrue(all(any(c.get('gpu_'+f) for c in plan) for f in b.GPU_EXTRA_FLAGS))
        self.assertTrue(any(c.get('gpu_motion') for c in plan))
        self.assertEqual(len({c['name'] for c in plan}),len(plan))
    def test_pair_quality_and_motion_identical(self):
        for tag,ref,variants in v.plan(gpu_expanded=True,gpu_quick=True):
            for name,c,kind in variants:
                for k in ('mask','light_scale','mask_scale','soft','motion','wide','quality','visual_ticks'):
                    self.assertEqual(ref[k],c[k],(tag,name,k))
    def test_reference_does_not_inherit_gpu_env(self):
        c=b.gpu_expanded_plan('quick')[0]
        with patch.dict('os.environ',{'PS_PO_GPU_LOCAL_MASKS':'1','PS_PO_GPU_PARTICLE_MOTION':'1'}):
            env=b.environment(c,Path('capture'))
        self.assertEqual(env['PS_PO_GPU_LOCAL_MASKS'],'0');self.assertEqual(env['PS_PO_GPU_PARTICLE_MOTION'],'0')
    def test_local_requires_cross_frame_hits(self):
        c=next(c for c in b.gpu_expanded_plan('quick') if c['gpu_variant']=='local')
        with self.assertRaises(ValueError): b.validate_gpu_expanded(c,{'gpu_local_hits':1})
        b.validate_gpu_expanded(c,{'gpu_local_hits':1,'gpu_local_prior_hits':1})
    def test_particles_reject_fallback(self):
        c=next(c for c in b.gpu_expanded_plan('quick') if c['gpu_variant']=='analytic')
        with self.assertRaises(ValueError):b.validate_gpu_expanded(c,{'gpu_particle_analytic':1000,'gpu_particle_fallbacks':1})
        b.validate_gpu_expanded(c,{'gpu_particle_analytic':1000})
    def test_reference_rejects_unexpected_work(self):
        c=b.gpu_expanded_plan('quick')[0]
        with self.assertRaises(ValueError):b.validate_gpu_expanded(c,{'gpu_fused_layers':1})
    def test_wide_and_stress_only_in_longer_performance_suite(self):
        self.assertFalse(any(c['wide'] for c in b.gpu_expanded_plan('quick')))
        self.assertTrue(any(c['wide'] for c in b.gpu_expanded_plan('full')))
        self.assertTrue(any(c.get('gpu_stress')==10000 for c in b.gpu_expanded_plan('full')))
    def test_visual_review_not_strict_for_fast_bloom(self):
        pairs=v.plan(gpu_expanded=True,gpu_quick=True)
        self.assertTrue(any(name=='fast_bloom' and kind=='quality' for _,_,vs in pairs for name,_,kind in vs))
if __name__=='__main__':unittest.main()
