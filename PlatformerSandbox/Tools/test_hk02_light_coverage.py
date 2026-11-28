"""Headless LDtk coverage reproduction and wiring checks; C# tests exercise the real planner."""
import json
import math
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def inventory():
    level = json.loads((ROOT / 'Content/Levels/LightingQualityLab.ldtk').read_text(encoding='utf-8-sig'))['levels'][0]
    for layer in level['layerInstances']:
        for e in layer['entityInstances']:
            fields = {f['__identifier']: f['__value'] for f in e['fieldInstances']}
            x, y = [(p + (.5 - pivot) * size) / 32 for p, pivot, size in zip(e['px'], e['__pivot'], (e['width'], e['height']))]
            y = level['pxHei'] / 32 - y
            if e['__identifier'] == 'LightSource' and fields['LightRole'] in ('AREA_RECT', 'SHOWCASE'):
                extent = math.hypot(fields['TubeLengthWorld'], fields['EmitterWidthWorld']) / 2 if fields['LightType'] == 'Tube' else 0
                yield (e['iid'], 'level', x, y, fields['RadiusWorld'] + extent, fields['Priority'], fields.get('MotionDistanceWorld', 0))
            elif fields.get('EmissiveLightRadius', 0) > 0:
                yield ('emissive:' + e['iid'], 'emissive', x, y, fields['EmissiveLightRadius'], 0, 0)


def visible(lights, x, y, width=1280, height=720, padding=False):
    return sorted((l for l in lights if abs(l[2] - x) <= width / 64 + l[4] + (l[6] + (2 if l[1]=='emissive' and l[4]==5 else 0) if padding else 0)
                   and abs(l[3] - y) <= height / 64 + l[4] + (2 if padding and l[1]=='emissive' and l[4]==5 else 0)), key=lambda l: (-l[5], l[0]))


def choose(lights, quota):
    # All lights in this frozen fixture request shadows; authored level lights
    # receive quota first, then emissive proxies in priority / ID order.
    owners = [l for l in lights if l[1]=='level'] + [l for l in lights if l[1]!='level']
    shadow_ids = {l[0] for l in owners[:quota]}
    return {l[0] for l in lights[:12] if l[1]=='level' or l[0] in shadow_ids}


class LightCoverageTests(unittest.TestCase):
    def test_reproduce_reported_transition(self):
        lights = list(inventory())
        pink = 'emissive:6800ddc5-07fe-53b4-896c-df96407d741f'
        near = visible(lights, 71.51, 15.04)
        far = visible(lights, 78.17, 15.04)
        self.assertIn(pink, choose(near, 5))
        self.assertNotIn(pink, choose(far, 5))
        self.assertIn(pink, {l[0] for l in far})
        for candidates in (near, far):
            self.assertEqual({l[0] for l in candidates}, choose(candidates, 12))

    def test_coverage_capacity_at_720p_and_1080p(self):
        lights = list(inventory())
        # Bound horizontal animation and the small rotating emissive proxies
        # conservatively; use the real authored inventory and viewport extent.
        for width, height, expected in ((1280, 720, 10), (1920, 1080, 12)):
            half = width / 64
            maximum = max(len(visible(lights, half + i/10, 20, width, height, True))
                          for i in range(round((120-2*half)*10)+1))
            self.assertEqual(expected, maximum)

    def test_same_budget_for_planner_and_renderer(self):
        game = (ROOT/'Source/Game1.cs').read_text(encoding='utf-8-sig')
        controls = (ROOT/'Source/Game1.PO10E.cs').read_text(encoding='utf-8-sig')
        self.assertIn('LightingFrameBudget frameLightBudget = EffectiveFrameLightBudget;', game)
        self.assertIn('IsQualityLab ? frameLightBudget : null', game)
        self.assertIn('LightingFrameBudget shadowBudget = frameLightBudget;', game)
        self.assertIn('art!, frameLightBudget, layer', game)
        self.assertIn('frameLightBudget,\n                                        frameLighting.Layers.Gameplay', game)
        self.assertIn('ManualCoverageAvailable => IsQualityLab &&', controls)
        self.assertIn('"LIGHT COVERAGE"', controls)
        self.assertIn('"INSPECT SCENE LIGHT"', controls)
        self.assertIn('"SELECTED LIGHT STATUS"', controls)

if __name__ == '__main__':
    unittest.main()
