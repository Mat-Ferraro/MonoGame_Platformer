# Phase 21B1 — Dedicated emissive lighting lab

Supersedes the first emission color fixture update. Apply on top of Phase 21A–B1 (also compatible with the first emission color test patch). The previous overlapping six witnesses are removed from Light Types Lab, restoring its original five authored light types and previous polygon/receiver fixtures. Unused old Instructions.png may remain on disk; it is no longer referenced.

Select LIGHTING / EMISSIVE COLOR LAB from level selection. The new 1280×1280 LDtk room uses the same player controller, GPU material receiver, emitter proxy creation, shadow geometry and light budgets as gameplay. No renderer or shader modifications are included. No normal authored lights exist in this room; emissive-only filtering happens before the frame budget, excluding unrelated flash, particle and stress lights. Two sources are cyan and magenta; the yellow source is glow-only. A presentation-only low blocker interrupts the cyan beam near the floor. Neutral gray ground, backdrop, strip and item reveal hue without painted-in tint. The new lab-specific preset has neutral low ambient and a modest hero readability floor. Other presets are not changed.

Default is mode 3 (world illumination only; bloom and self-emission disabled). Keys 1–4 choose reproducible states:

| Key | World lights | Visible self-emission | Bloom |
|---|---|---|---|
| 1 | off | off | off |
| 2 | off | on | off |
| 3 | on | off | off |
| 4 | on | on | on |

All modes restore High lighting quality, AllEligible materials, hard shadows, neutral preset, HDR requested, exposure 1 and stress disabled. The overlay reports current flags and selected light count, not just the requested mode. Expect 2 active lights in mode 3/4, 0 in mode 1/2. Move left and right, jump near the low blocker and compare modes without opening DEV TOOLS. A yellow self-emitter must not contribute a yellow world light. Source art itself is colored even in baseline. Use T/restart to restore mode 3.

Validation: run `py Tools/phase21_emission_fixture_preflight.py` and `py Tools/phase21_spatial_preflight.py`, then the normal desktop build and test suite. New xUnit tests exercise the actual importer, presenter, CPU reference lighting and old lab isolation. Python checks exercise fixture dimensions, field references, image availability, neutral receiver configuration, overlap and blocker geometry. No .NET compiler, target GPU or original shader sources were available in the development environment. This change makes an emission bug diagnosable rather than claiming to fix unverified GPU hue transfer. If 2 lights are reported but the gray surfaces remain gray in mode 3, the next investigation is the GPU contribution path.
