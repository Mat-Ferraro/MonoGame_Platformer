"""Structural content regression checks; not a substitute for LDtk/C# runtime tests."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

class HousekeepingTests(unittest.TestCase):
    def setUp(self):
        def level(name):
            return json.loads((ROOT / 'Content' / 'Levels' / (name + '.ldtk')).read_text(encoding='utf-8-sig'))['levels'][0]
        self.bench = level('LightingPerformanceLab')
        self.manual = level('LightingQualityLab')

    def test_independent_dimensions(self):
        self.assertEqual((1280, 1280), (self.bench['pxWid'], self.bench['pxHei']))
        self.assertEqual((3840, 1280), (self.manual['pxWid'], self.manual['pxHei']))
        self.assertNotEqual(self.bench['iid'], self.manual['iid'])
        self.assertEqual(self.bench['identifier'], self.manual['identifier'])

    def test_continuous_floor(self):
        layer = next(x for x in self.manual['layerInstances'] if x['__identifier'] == 'Collisions')
        grid = layer['intGridCsv']
        self.assertEqual(len(grid), 120 * 40)
        self.assertEqual(grid[:120*26], [0] * (120*26))
        self.assertEqual(grid[120*26:], [1] * (120*14))

    def test_entity_identity_and_spawn(self):
        entities = [e for l in self.manual['layerInstances'] for e in l['entityInstances']]
        self.assertEqual(len(entities), len({e['iid'] for e in entities}))
        spawn = [e for e in entities if e['__identifier'] == 'PlayerSpawn']
        self.assertEqual(len(spawn), 1)
        self.assertTrue(1280 <= spawn[0]['px'][0] < 2560)
        for section in range(3):
            lights = [e for e in entities if e['__identifier'] == 'LightSource' and section*1280 <= e['px'][0] < (section+1)*1280]
            self.assertEqual(len(lights), 5)

    def test_one_lab_control_path(self):
        source = (ROOT / 'Source' / 'Game1.cs').read_text(encoding='utf-8-sig')
        controls = (ROOT / 'Source' / 'Game1.PO10E.cs').read_text(encoding='utf-8-sig')
        for retired in ('DrawPerformancePanel', 'BuildPerformancePanel', 'HitPerformancePanel', 'BeginPerfSweep'):
            self.assertNotIn(retired, source + controls)
        self.assertEqual(source.count('DrawManualLab();'), 1)
        self.assertIn('"CAMERA MODE"', controls)
        self.assertIn('"ENABLE ALL OPTIMIZATIONS"', controls)

if __name__ == '__main__':
    unittest.main()
