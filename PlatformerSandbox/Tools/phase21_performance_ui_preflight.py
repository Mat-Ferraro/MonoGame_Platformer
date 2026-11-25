"""Static integration checks for the clickable performance lab. Not a C# or GPU test."""
from pathlib import Path
import re
root = Path(__file__).resolve().parents[1]
s = (root / 'Source/Game1.cs').read_text(encoding='utf-8-sig')
assert 'toolClicked = IsPerformanceLab && uiClicked && HitPerformancePanel(uiPoint);' in s
assert 'HandleSceneNavigation(keys, gamepadState, uiClicked && !toolClicked, uiPoint);' in s
assert 'if (!_flow.IsTransitionActive) DrawPerformancePanel(lightingActive);' in s
assert s.count('Rectangle panel=BuildPerformancePanel(GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height,') == 2
for action, handler in {'Workload':'SelectPerfWorkload(button.Value)', 'Quality':'SetPerfQuality(PerfQualities[button.Value])', 'Diagnostic':'SetPerfIsolation(button.Value)', 'Optimization':'TogglePerfOptimization(button.Value)', 'AllOn':'SetAllPerfOptimizations(true)', 'AllOff':'SetAllPerfOptimizations(false)', 'Freeze':'TogglePerfFreeze()', 'Hulls':'TogglePerfHulls()', 'Capture':'BeginPerfCapture()', 'Sweep':'BeginPerfSweep()', 'Cancel':'CancelPerfCapture()'}.items():
    assert f'case PerfUiAction.{action}: {handler}; break;' in s, action
for key, flag in enumerate(('ReuseGeometry','ReuseLayerFields','PruneAreaSamples','CacheAcrossFrames','ReuseHardMasks')):
    assert f'{flag} = PerfEnabled({1<<key});' in s
assert 'int flag=1<<i; bool enabled=PerfEnabled(flag);' in s
assert 'PerfOptimizationNames[i]+(enabled?" ON":" OFF")' in s
assert 'if (!_perfPanelExpanded) return new Rectangle' in s
assert 'bool paged = height < 760;' in s
assert 'if (IsPerformanceLab) _perfPanelExpanded = false;' in s
assert 'private void BeginPerfSweep(bool quick = false)\n    {\n        _perfPanelExpanded = false;' in s
assert 'if(PerfCaptureActive) CancelPerfCapture();\n                    _perfPanelExpanded=!_perfPanelExpanded;' in s
assert 'int warmup = _perfAutoSweep ? PerfSweepWarmup : 120;' in s
assert 'int total = warmup + (_perfAutoSweep ? PerfSweepRecorded : 300);' in s
assert 'if (_perfOverlay && _spriteBatch is not null && _pixel is not null)' in s
assert 'WARMUP {_phase21BenchmarkFrame}/{warmup}' in s and 'RECORDING {_phase21BenchmarkFrame-warmup}/{total-warmup}' in s
# Both sweeps include all seven individual flags and every diagnostic.
quick=s[s.index('private static readonly (int Mask, int Isolation)[] PerfQuickSweep'):s.index('private bool _perfQuickSweep')]
repeated=s[s.index('private static readonly (int Mask, int Isolation)[] PerfSweep'):s.index('private static readonly (int Mask, int Isolation)[] PerfQuickSweep')]
parse=lambda text:[(int(a),int(b)) for a,b in re.findall(r'\((\d+),(\d+)\)',text)]
expected={(0,0),(1,0),(2,0),(4,0),(8,0),(16,0),(32,0),(64,0),(127,0)} | {(127,i) for i in range(1,9)}
assert set(parse(quick))==expected and len(parse(quick))==17
assert set(parse(repeated))==expected and all(parse(repeated).count(c)>=2 for c in expected)
for action in ('Pacing','QuickSweep'): assert f'case PerfUiAction.{action}:' in s
print('PASS: shared layout/input routing, seven toggles, pacing and both sweeps (static checks only).')
