import contextlib,io,json,unittest,tempfile
from pathlib import Path
from unittest.mock import patch
import po_run_benchmarks as bench
import po_visual_regression as visual
import po_export_source as export

class GPU01Tests(unittest.TestCase):
    def test_quick_and_full_plan(self):
        for tier,count in [('quick',10),('standard',25),('full',30)]:
            out=io.StringIO()
            with patch.object(bench.sys,'argv',['runner','--gpu01','--tier',tier,'--list']),contextlib.redirect_stdout(out):
                self.assertEqual(0,bench.main())
            cases=json.loads(out.getvalue()); self.assertEqual(count,len(cases))
            for i in range(0,len(cases),5):
                group=cases[i:i+5]; a=group[0]
                for c,(variant,b,r,x) in zip(group,bench.GPU_VARIANTS):
                    self.assertEqual((variant,b,r,x),(c['gpu_variant'],c['gpu_buffers'],c['gpu_resident'],c['gpu_indexed']))
                    ignore=('name','gpu_variant','gpu_buffers','gpu_resident','gpu_indexed')
                    self.assertEqual({k:v for k,v in a.items() if k not in ignore},{k:v for k,v in c.items() if k not in ignore})
                    env=bench.environment(c,Path('out'))
                    for k,v in zip(('BUFFERS','RESIDENT','INDEXED'),(b,r,x)): self.assertEqual(str(v),env['PS_PO_GPU_SHADOW_'+k])
                    self.assertEqual(0,c['mask'] & 64)
    def test_activity_positive_and_failure(self):
        def counts(b=0,r=0,f=0,u=0,hit=0,miss=0,idx=0,bypass=0):return dict(gpu_buffered_draws=b,gpu_reference_draws=r,gpu_fallback_draws=f,gpu_upload_bytes=u,gpu_resident_hits=hit,gpu_resident_misses=miss,gpu_indexed_draws=idx,gpu_index_bypasses=bypass)
        bench.validate_gpu_activity({'gpu_buffers':0},counts(r=10))
        bench.validate_gpu_activity({'gpu_buffers':1},counts(b=10,u=480))
        bench.validate_gpu_activity({'gpu_buffers':1,'gpu_resident':1,'gpu_group':'static_half'},counts(b=10,hit=10))
        bench.validate_gpu_activity({'gpu_buffers':1,'gpu_indexed':1},counts(b=10,u=160,idx=10))
        bench.validate_gpu_activity({'gpu_buffers':1,'gpu_resident':1,'gpu_indexed':1},counts(b=10,hit=10,idx=10))
        for mode,c in [(1,counts()),(1,counts(r=10,f=10)),(1,counts(b=1,r=1,u=48)),(0,counts(b=1,u=48))]:
            with self.assertRaises(ValueError):bench.validate_gpu_activity({'gpu_buffers':mode},c)
        for flags,c in [({'gpu_resident':1,'gpu_group':'static_half'},counts(b=10,u=480,miss=10)),({'gpu_indexed':1},counts(b=10,u=480)),({'gpu_indexed':1},counts(b=10,u=480,idx=1,bypass=1)),({},counts(b=10,u=480,idx=1))]:
            with self.assertRaises(ValueError):bench.validate_gpu_activity({'gpu_buffers':1,**flags},c)
    def test_visual_pairs(self):
        for quick,count in [(False,5),(True,2)]:
            groups=visual.plan(gpu01=True,gpu_quick=quick); self.assertEqual(count,len(groups)); self.assertEqual(count*6,sum(2+len(v) for _,_,v in groups))
            for _,a,variants in groups:
                for _,b,kind in variants:
                    self.assertEqual('strict',kind)
                    ignore=('name','gpu_variant','gpu_buffers','gpu_resident','gpu_indexed')
                    self.assertEqual({k:v for k,v in a.items() if k not in ignore},{k:v for k,v in b.items() if k not in ignore})
    def test_no_nvidia_tools_does_not_prevent_compatibility(self):
        with tempfile.TemporaryDirectory() as t,patch.object(bench.shutil,'which',return_value=None):
            self.assertIsNone(bench.start_gpu_telemetry(Path(t)))
            self.assertIn('UNAVAILABLE',(Path(t)/'gpu_telemetry_status.txt').read_text())
    def test_legacy_suite_defaults_compatibility(self):
        for c in bench.po10f_plan():
            for k in ('BUFFERS','RESIDENT','INDEXED'): self.assertEqual('0',bench.environment(c,Path('out'))['PS_PO_GPU_SHADOW_'+k])
    def test_preflight_detects_incomplete_export(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t); files=[]
            self.assertTrue(export.gpu_missing(root,files))
            for filename in export.GPU_REQUIRED_PROJECTS:
                p=root/filename; p.parent.mkdir(parents=True,exist_ok=True); p.write_text('project'); files.append(p)
            for filename in export.GPU_REQUIRED_EFFECTS:
                p=root/'Content'/'Effects'/filename; p.parent.mkdir(parents=True,exist_ok=True); p.write_text('effect'); files.append(p)
            self.assertEqual([],export.gpu_missing(root,files))
            files.pop(); self.assertTrue(export.gpu_missing(root,files))
if __name__=='__main__':unittest.main()
