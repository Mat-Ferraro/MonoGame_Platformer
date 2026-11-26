import contextlib, io, json, unittest
from pathlib import Path
from unittest.mock import patch
import po_run_benchmarks as bench
import po_visual_regression as visual

class PO10FTests(unittest.TestCase):
    def test_quick_plan_and_environment(self):
        output=io.StringIO()
        with patch.object(bench.sys,'argv',['runner','--po10f','--tier','quick','--list']),contextlib.redirect_stdout(output):
            self.assertEqual(0,bench.main())
        cases=json.loads(output.getvalue())
        self.assertEqual(list(bench.F_QUICK),[c['name'] for c in cases])
        self.assertEqual([0,1,0,0,1],[c['static_shadows'] for c in cases])
        self.assertEqual([1,1,0,1,1],[c['caster_buffers'] for c in cases])
        self.assertEqual([True,True,True,False,False],[c['adaptive_warmup'] for c in cases])
        for c in cases:
            env=bench.environment(c,Path('out'))
            self.assertEqual(str(c['static_shadows']),env['PS_PO_10F_STATIC_SHADOWS'])
            self.assertEqual(str(c['caster_buffers']),env['PS_PO_10F_CASTER_BUFFERS'])
            self.assertEqual('static_shadow',env['PS_PO_10C_FIXTURE'])
    def test_visual_plan(self):
        groups=visual.plan(hard_cache=True)
        self.assertEqual(['hard_cache','moving_hard_cache','soft_cache_guard'],[g[0] for g in groups])
        self.assertEqual(10,sum(2+len(g[2]) for g in groups))
        for _,base,variants in groups:
            for _,c,kind in variants:
                self.assertEqual('strict',kind)
                self.assertEqual(base['light_scale'],c['light_scale'])
                self.assertEqual(base['mask_scale'],c['mask_scale'])
                self.assertEqual(base['visual_ticks'],c['visual_ticks'])
    def test_world_fixed_fixture_rejects_camera_coupled_lights(self):
        c={'fixture':'static_shadow'}
        bench.validate_world_fixed_fixture(c,[{'lights_changed':'0','view_changed':'1'}])
        with self.assertRaisesRegex(ValueError,'light geometry changed'):
            bench.validate_world_fixed_fixture(c,[{'lights_changed':'1','view_changed':'1'}])
    def test_cross_camera_visual_invariant(self):
        stationary={'Camera':'a','Light positions':'fixed'}
        visual.validate_fixed_light_camera_pair(stationary,{'Camera':'b','Light positions':'fixed'})
        with self.assertRaisesRegex(ValueError,'lights moved'):
            visual.validate_fixed_light_camera_pair(stationary,{'Camera':'b','Light positions':'changed'})
        with self.assertRaisesRegex(ValueError,'did not change camera'):
            visual.validate_fixed_light_camera_pair(stationary,stationary)
        groups=visual.plan(hard_cache=True)
        moving=next(g for g in groups if g[0]=='moving_hard_cache')
        self.assertEqual(30,moving[1]['visual_ticks'])
if __name__=='__main__': unittest.main()
