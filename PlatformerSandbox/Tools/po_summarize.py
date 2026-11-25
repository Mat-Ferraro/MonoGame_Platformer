"""Summarize one or more PO captures without mixing settings or runs."""
import argparse
import csv
import math
import statistics
from pathlib import Path


def percentile(values, p):
    return sorted(values)[max(0, math.ceil(len(values) * p) - 1)]


def read_capture(path):
    with path.open(encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    if not rows:
        raise ValueError('empty capture')
    frame_key = 'preceding_frame_ms' if 'preceding_frame_ms' in rows[0] else 'step_ms'
    def column(key):
        if key not in rows[0]:
            return None
        values = [float(r[key]) for r in rows]
        if not all(math.isfinite(v) for v in values):
            raise ValueError('nonfinite column: ' + key)
        return values
    for key in ('width', 'height', 'fixed_step', 'vsync', 'optimization_mask', 'isolation', 'freeze_lights'):
        if key in rows[0] and len({r[key] for r in rows}) != 1:
            raise ValueError('mixed settings: ' + key)
    frames = column(frame_key)
    if any(v < 0 for v in frames) or (frame_key == 'preceding_frame_ms' and any(v <= 0 for v in frames)):
        raise ValueError('invalid intervals')
    print(f'\n{path.name} | {len(rows)} samples | {frame_key}')
    mean = statistics.mean(frames)
    print(f'mean {mean:.3f} ms | P50 {percentile(frames,.50):.3f} | P95 {percentile(frames,.95):.3f} | P99 {percentile(frames,.99):.3f} | max {max(frames):.3f}')
    if frame_key == 'preceding_frame_ms':
        print(f'draw cadence {1000/mean:.1f} FPS | intervals > 16.667 ms: {sum(x > 1000/60 for x in frames)}')
    for key in ('updates','update_cpu_ms','previous_draw_cpu_ms','present_wall_ms','simulation_cpu_ms','diagnostic_cpu_ms','cpu_submit_ms','material_geometry_cpu_ms','layer_geometry_cpu_ms','debug_hud_cpu_ms','thread_alloc_bytes','sprites','draw_calls','target_changes'):
        values = column(key)
        if values is not None:
            print(f'  {key:28s} mean {statistics.mean(values):12.3f}   P95 {percentile(values,.95):12.3f}')
    for key in ('gc0','gc1','gc2'):
        values = column(key)
        if values is not None:
            print(f'  {key}: {sum(values):.0f} collections (not pause duration)')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('captures', nargs='+', type=Path)
    args = parser.parse_args()
    for path in args.captures:
        read_capture(path)
    print('\nCompare only equivalent scene/configuration metadata. CPU wall time can include driver waits; not GPU timestamps.')


if __name__ == '__main__':
    main()
