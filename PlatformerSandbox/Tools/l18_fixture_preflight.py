"""Phase 18 source/LDtk preflight; no MonoGame/.NET dependency.

From the PlatformerSandbox root: py tools/l18_fixture_preflight.py
Does not claim shader compilation or GPU visual correctness.
"""
from __future__ import annotations
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
LEVEL = ROOT / 'Content/Levels/LightingIntegrationTest.ldtk'
SHADER = ROOT / 'Content/Assets/Effects/L_BASIC_SurfaceLighting.fx'
RENDERER = ROOT / 'Source/Lighting/GameplayLightingRenderer.cs'
PANEL = ROOT / 'Source/Lighting/LightingToolsPanel.cs'


def fields(entity):
    return {field['__identifier']: field['__value'] for field in entity['fieldInstances']}


def run():
    project = json.loads(LEVEL.read_text(encoding='utf8'))
    level, = [x for x in project['levels'] if x['identifier'] == 'LIGHTING_INTEGRATION_TEST']
    entities = [x for layer in level['layerInstances'] for x in layer['entityInstances']]
    lights = [x for x in entities if x['__identifier'] == 'LightSource']
    materials = [x for x in entities if x['__identifier'] == 'TerrainArtwork'
                 and fields(x).get('MaterialShading') in ('Normal', 'Hybrid')]
    assert len(lights) == 11, f'Expected 11 light sources, found {len(lights)}'
    assert len(materials) == 2, f'Expected two GPU-material test surfaces, found {len(materials)}'
    assert len({x['iid'] for x in entities}) == len(entities), 'Duplicate LDtk instance UUID'
    field_defs = {f['uid'] for e in project['defs']['entities'] for f in e['fieldDefs']}
    for obj in entities:
        for f in obj['fieldInstances']:
            assert f['defUid'] in field_defs, f'Unknown LDtk field ID: {f["defUid"]}'
    expected = {'Normal': 5, 'Hybrid': 4}
    for surface in materials:
        sf = fields(surface)
        material = sf['MaterialShading']
        cx = surface['px'][0] + (0.5 - surface['__pivot'][0]) * surface['width']
        cy = surface['px'][1] + (0.5 - surface['__pivot'][1]) * surface['height']
        touching = [source for source in lights
                    if 'PLAYER' in fields(source)['AffectsLayers'].split(',')
                    and math.hypot(source['px'][0] + (0.5 - source['__pivot'][0]) * source['width'] - cx,
                                   source['px'][1] + (0.5 - source['__pivot'][1]) * source['height'] - cy)
                    < fields(source)['RadiusWorld'] * 32]
        assert len(touching) >= expected[material], (material, len(touching))
        print(f'{material} material: {len(touching)} overlapping source circles (expected >= {expected[material]})')
        if material == 'Hybrid':
            back = [x for x in touching if fields(x).get('LightDepthZ', 0) < sf.get('MaterialDepthZ', 0)]
            front = [x for x in touching if fields(x).get('LightDepthZ', 0) > sf.get('MaterialDepthZ', 0)]
            assert len(back) >= 3 and front, (len(back), len(front))
            print(f'  pillar depth split: {len(back)} rear / {len(front)} front')
    source = SHADER.read_text(encoding='utf8')
    render = RENDERER.read_text(encoding='utf8')
    panel = PANEL.read_text(encoding='utf8')
    for token in ('ProductionLightPassMode', 'ProductionVisibility0', 'ProductionVisibility1'):
        assert token in source and token in render, token
    for token in ('ProductionMaterialLightPlan.Batches', 'DrawProductionMaterialRegion',
                  'LastMaterialLightsEvaluated', 'LastMaterialLightPasses'):
        assert token in render, token
    assert 'MATERIAL LIGHTS:' in panel and 'ToggleMaterialLights' in panel
    print('Phase 18 fixture/source preflight PASSED (not a GPU or .NET test)')


if __name__ == '__main__':
    try:
        run()
    except (AssertionError, KeyError, FileNotFoundError) as ex:
        print(f'FAILED: {ex}', file=sys.stderr)
        sys.exit(1)
