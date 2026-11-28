import csv
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import po_run_benchmarks as b
import po_visual_regression as v

class GpuCompareTests(unittest.TestCase):
    def test_five_matched_configurations(self):
        cases=b.gpu_compare_plan()
        self.assertEqual([c['gpu_variant'] for c in cases],['reference','all','no_indexed','shaders','shaders_local'])
        for c in cases:
            self.assertEqual((c['light_scale'],c['mask_scale'],c['bloom_scale'],c['quality'],c['area'],c['adaptive']), (100,100,100,'High',0,0))
            self.assertEqual((c['mask'],c['static_fields'],c['static_shadows'],c['motion'],c['soft'],c['vsync'],c['cap'],c['gpu_stress'],c['gpu_motion']),(127,1,1,1,0,0,0,0,0))
            self.assertTrue(all(c['flags'].values()) and all(c['bflags'].values()) and all(c['tflags'].values()))
            self.assertFalse(c['gpu_fast_bloom'])
        differences={k for k in cases[1] if cases[1][k]!=cases[2][k]}
        self.assertEqual(differences,{'name','gpu_variant','gpu_indexed'})
        self.assertEqual({k for k in cases[3] if cases[3][k]!=cases[4][k]},{'name','gpu_variant','gpu_local_masks'})
    def test_reference_clears_inherited_gpu_flags(self):
        with patch.dict('os.environ',{'PS_PO_GPU_SHADOW_BUFFERS':'1','PS_PO_GPU_FAST_BLOOM':'1','PS_PO_GPU_PARTICLE_STRESS':'5000'}):
            env=b.environment(b.gpu_compare_plan()[0],Path('capture'))
        self.assertEqual(env['PS_PO_GPU_SHADOW_BUFFERS'],'0')
        self.assertEqual(env['PS_PO_GPU_FAST_BLOOM'],'0')
        self.assertEqual(env['PS_PO_GPU_PARTICLE_STRESS'],'0')
    def test_adapter_guard(self):
        b.validate_adapter('RTX 5070','NVIDIA GeForce RTX 5070 Laptop GPU','NOT PROBED')
        b.validate_adapter('RTX 5070','NVIDIA GeForce RTX 5070 Laptop GPU','NVIDIA GeForce RTX 5070 Laptop GPU/PCIe')
        with self.assertRaises(ValueError): b.validate_adapter('RTX 5070','Intel')
        with self.assertRaises(ValueError): b.validate_adapter('RTX 5070','RTX 5070','Intel')
    def test_submission_guard(self):
        ref,all_on,no_idx,*_=b.gpu_compare_plan()
        b.compare_submission(ref,{'gpu_reference_draws':10})
        counts={'gpu_buffered_draws':10,'gpu_resident_misses':10,'gpu_indexed_draws':10}
        b.compare_submission(all_on,counts)
        with self.assertRaises(ValueError): b.compare_submission(all_on,dict(counts,gpu_fallback_draws=1))
        with self.assertRaises(ValueError): b.compare_submission(all_on,dict(counts,gpu_indexed_draws=0))
        with self.assertRaises(ValueError): b.compare_submission(no_idx,counts)
    def test_visual_plan(self):
        [(tag,ref,vs)]=v.plan(gpu_compare=True)
        self.assertEqual(len(vs),4)
        self.assertEqual(ref['visual_ticks'],60)
        self.assertTrue(all(c['visual_ticks']==60 and c['motion']==ref['motion'] and kind=='quality' for _,c,kind in vs))
    def test_comparison_summary(self):
        base=dict(status='ok',mean_ms=10,p95_ms=12,p99_ms=15,fps=100,cpu_submit_ms_mean=2,draw_calls_mean=50,target_changes_mean=30,gpu_adapter='RTX 5070',gpu_unexercised_optional='')
        with tempfile.TemporaryDirectory() as tmp:
            dest=Path(tmp)
            b.write_summary(dest,[dict(base,name='gx_compare_reference'),dict(base,name='gx_compare_shaders',fps=125,mean_ms=8,gpu_unexercised_optional='batch_unshadowed')])
            with (dest/'gpu_comparison.csv').open() as f: rows=list(csv.DictReader(f))
            self.assertEqual(float(rows[1]['fps_change_percent']),25)
            self.assertEqual(rows[1]['optional_features_not_exercised'],'batch_unshadowed')
if __name__=='__main__':unittest.main()
