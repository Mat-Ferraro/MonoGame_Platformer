# PO05 validation

Extract into the solution root over GPU01 Refinement 01. Keep the original source/commit for rollback. From PowerShell in the root:

```
py .\Tools\po05_validate.py --tier quick
```

The runner executes the full existing/new C# suite in Reference, Indexed and Verify modes, compiles DesktopGL Release, then captures repeated Reference and Indexed screenshots in Engine Scalability Lab and Lighting Quality Lab at tick 60. Six captures, four comparisons. Native GPU experiments are off for both image paths. Indexed capture must report at least one actual indexed static-collider query. The runner fails on any child failure and archives logs, TRX files, scalability CSVs and nested image archive. `Benchmarks/PO05_validation_*.zip` is the only archive to upload. A build failure still generates a report; subsequent steps are marked not run.

```
py .\Tools\po05_validate.py --tier quick --headless
py .\Tools\po05_validate.py --suite spatial
py .\Tools\po05_validate.py --suite collision
py .\Tools\po05_validate.py --suite visual
py .\Tools\po05_validate.py --tier full
```

Quick: 1,000 randomized comparisons per C# invocation (10 seeds x 100), 100/1000-object query benchmark, 600-tick authored lab replay, 6 images. Standard: 50,000 comparisons (100 x 500), up to 10,000 objects, 1,800-tick lab replay, 18 images. Full: 500,000 comparisons (500 x 1000), up to 50,000 objects, 3,600-tick lab replay, 18 images. Repeated test modes rerun scoped spatial tests by design. Synthetic query benchmark measures examined entries, match count, wall time and current-thread managed allocations, not game FPS. No fixed FPS gate. Report reproducibility via seed and operation; visual differences fail strict comparison. `--list` prints commands without executing them.

Manual: Launch Release, select Engine Scalability Lab, use Reference then Indexed then Verify. Walk, jump, test platforms, horizontal and vertical sweeps, toggle grid and query overlay, compare each subsystem switch, respawn, change levels, return to lab, export diagnostics. Test existing Movement Gym and Connected Cavern. Test on integrated graphics as well. Verify mode must not throw mismatches. Do not promote Indexed to production until regression and manual acceptance succeed.

Local verification at packaging: 55 Python unittest cases passed; Python compileall and planning checks passed; authored IntGrid dimensions and both platform paths checked. No .NET SDK or MonoGame execution available in this delivery environment. C# and all runtime results pending.
