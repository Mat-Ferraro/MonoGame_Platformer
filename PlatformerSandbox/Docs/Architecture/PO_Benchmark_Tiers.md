# Benchmark tiers (PO10A)

Install both `Source/Game1.cs` and `Tools/po_run_benchmarks.py` together on top of the PO10A candidate. Rebuild on the first run (omit `--no-build`). No lighting algorithms change. Manual captures and in-game sweeps retain their original frame counts.

| Tier | Cases | Warmup | Measured | Recommended repetitions |
|---|---:|---:|---:|---:|
| Quick | 6 | 60 | 120 | 1 |
| Standard | 12 | 90 | 180 | 1–2 |
| Full | 26 | 120 | 300 | 3 for milestone validation |

Commands (PowerShell from solution root):

```
py .\Tools\po_run_benchmarks.py --po10a --tier quick
py .\Tools\po_run_benchmarks.py --po10a --tier standard --repeat 2 --no-build
py .\Tools\po_run_benchmarks.py --po10a --repeat 3 --no-build
py .\Tools\po_run_benchmarks.py --po10a --case combined_50 --case motion_combined_50 --warmup-frames 60 --recorded-frames 120 --no-build
```

Quick uses reference, combined full/50/25, moving 50, and soft 50. Standard adds combined 75, individual light/mask 50, shared fields, 1080p combined 50, and lighting off. Use `--no-wide` to omit 1080p. Full remains default; `--repeat` remains 1 unless specified. `--list` previews settings. Custom frame counts accepted within 30–1200 warmup and 60–3000 recorded. Short tiers are screening tests: the smaller time window may underrepresent GC, moving-light cycles, thermal changes and tail latencies. Keep the same tier and capture lengths for A/B comparisons; use full validation for acceptance. Do not run cases concurrently. Fresh processes, 2s focus settling, strict metadata and sequential frame checks, actual target validation and focus retries remain intact. Old archived captures can still be parsed when using legacy case definitions without timing fields. New runs require the matching rebuilt game, including for full mode.

Tested host-side: Python test suites for PO10A, focus retries, and tiers. C# compilation and live game execution not available in this environment.
