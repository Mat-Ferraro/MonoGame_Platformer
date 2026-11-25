# Dual wall fixture correction

Supersedes the original Phase 21B2a fixture. The left wall has MaterialDepthZ=0, the right wall has MaterialDepthZ=1, and both emitters have MaterialDepthZ=0. The right wall's back rim is disabled to isolate the dark-face overlap comparison. The two walls retain the same artwork and dimensions. Preset 3 compares both simultaneously; preset 5 still places the emitters behind both.

Update both existing xUnit fixture tests and the Python fixture preflight to require two walls and verify distinct face depths, emitter routing, and representative unobstructed/overlap/behind samples. No changes to renderer, shaders, physics or Game1.cs are needed on top of Phase 21B2a.

Local build, tests and GPU validation remain required.
