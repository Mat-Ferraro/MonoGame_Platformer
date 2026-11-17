# PlatformerSandbox — Consolidated Architecture & Development Roadmap

**Reconciliation baseline:** 2026-11-14  
**Current working project root:** `C:\ProgrammingProjects\MonoGame_Projects\MonoGame_Platformer\PlatformerSandbox`  
**Evidence:** ten uploaded architecture/design documents; the previously retained expanded PS-01–PS-30 particle plan; reported and locally validated M24–M27 and A08 Phase 1 updates. This is a planning synthesis, **not** an audit of a fresh repository checkout.

## 1. Source authority and supersession register

| Subject | Current authority | Earlier or lower-authority source | Rule |
|---|---|---|---|
| Game Feel / Juice | `MonoGame_Platformer_Game_Feel_and_Juice_Plan_updated(1).docx` | `...Game_Feel_and_Juice_Plan(1).docx` | **Replaces/expands** original. Use updated concrete examples and vertical-slice acceptance matrix, retain shared principles. |
| Particle/VFX | **Previously retained** 19-page `MonoGame_Platformer_Particle_System_and_Visual_Effects_Architecture.docx` with PS-01–PS-30 | The uploaded 14-page `(1).docx` version covering PS-01–PS-20 | **Extends** original: PS-21–PS-30 are approved additions after audio integration review. Do not silently lose them. |
| Sound/audio | Uploaded `...Sound_System_and_Audio_Architecture(1).docx`, AS-01–AS-35 | Earlier AS-01–AS-24 work | AS-25–AS-35 **extend** earlier decisions and establish shared presentation-event contract AS-25. |
| Scene lifecycle / game flow | Uploaded `...Scene_Management_and_Game_Flow_Architecture(1).docx`, SM-01–SM-20, approved | Scene-management suggestions in `...Architecture_Recommendations_and_Decisions(1).docx` (proposed ADR-07) | The **approved specialist scene specification takes precedence** on transition/lifetime/menu matters. Earlier proposals about world clock, identity, determinism remain useful where consistent. |
| UI/UX | Uploaded `...UI_UX_Design_Principles_and_Decisions(1).docx`, adopted | UI suggestions in general architecture / original juice plan | Specialist approved UI policy controls UI focus, input gating and lifecycle. Final art and gameplay-specific HUD remain open. |
| Player input and multiplayer readiness | Uploaded `...Input_and_Multiplayer_Readiness_Architecture(1).docx` plus compatible world/player ownership detail in general ADR | Early gym keyboard-only implementation descriptions | Player, input device, and character must be distinct. Single player first; prove 2 players early; four slots tested; networking deferred. |
| Lighting/rendering | Uploaded `...2D_Lighting_and_Rendering_Architecture(1).docx`, LT-01–LT-43 | General rendering suggestions | Approved architecture direction, **not** permission to skip required backend/render prototypes. Hybrid deferred-style/forward, selective shadows, optional normals. |
| Performance | Uploaded `...Performance_and_Optimization_Strategy_Reference(1).docx`, OPT-01–OPT-24 | Generic optimization suggestions | **Proposed research backlog**, not individually approved mandatory optimizations. Instrument early; adopt invasive changes only on measured evidence. |
| Overall implementation status | User-reported verified M24–M27 and A08 Phase 1 | Older Project Status & Roadmap v1.0 (dated M23) and source-snapshot statements in general ADR | **Actual validated progress supersedes stale status claims.** Keep architecture intent, revise progress flags. |

Status precedence principle: (a) explicit approved amendment/specialist spec overrides earlier proposal in its scope, (b) explicit later revision overrides original, (c) user-verified implementation status overrides old snapshot, (d) open/conditional items remain open rather than silently approved.

## 2. Working baseline (reported, not independently audited)

- **M04–M27:** implemented and reported passing locally, including accelerated/analytical collision scheduling, moving-platform riding/crushing, four-way crush gym, strict fault modes, and configurable corner/step/ledge assistance. Playable gym assistance defaults ON, F9 toggles OFF/ON. Preserve as regression baseline.
- **A08 Phase 1:** typed movement requests and deterministic resolver integrated with keyboard and working in gym, tests reported passing. **Interactive priority-conflict test mode (F10) was discussed but not yet implemented.**
- **Unfinished:** A08 conflict-demonstration/extended arbitration; reusable gamepad/action adapters and robust player slots; shared WorldTick/WorldSession independent of player respawns; A09/A10; headless world semantic event fan-out; full scenes/UI, authored levels; presentation subsystems; renderer/lighting/tooling.

## 3. Ordered execution backlog

These are **implementation tasks**, not claims that every subsystem must wait for the previous one to finish. Local integration can overlap when prerequisites exist. Each task requires compile/test, targeted gym or playground proof, full regression, and source control checkpoint before closing.

### Phase A — Protect baseline and finish the input boundary

- [ ] **R00. Capture baseline.** Full solution build, unit tests, Strict/Diagnostic/Resilient gym smoke, four crush directions, 60-second platform boundary, assistance ON default/F9 toggle. Git tag and changelog.
- [ ] **R01. A08 Phase 2: request-conflict gym.** Implement visible F10 scenario mode: forced movement, neutral control lock, equal-priority deterministic ties, jump edge independence, source/priority/winner HUD. Prove identical result irrespective of submission order.
- [ ] **R02. A11 Phase 1: device-independent input.** Keyboard and gamepad adapters; binding definitions; normalized per-tick press/held/release; edge capture across render/fixed ticks; digital left/neutral/right for analog stick initially; explicit keyboard+gamepad conflict/disconnect policy; keep gym debug keys outside gameplay commands. Scripted input remains supported.

**Gate A:** Same single-character behavior for keyboard/gamepad/scripted commands; no duplicated/lost quick press; conflicts predictable; M27 physics unchanged.

### Phase B — World and player ownership (must precede independent respawn)

- [ ] **R03. WorldTick/WorldSession.** Single long-integer fixed-tick world clock, platforms evaluated at shared absolute time; player motors support nonzero-time spawn and maintain correct event-relative timing; separate player respawn from full world reset. Preserve motor compatibility for current tests.
- [ ] **R04. Player identity/slots/character instances.** Stable PlayerId independent of DeviceId, ColliderId, motor; per-player jump buffers and controller state; stable update ordering; first two-player keyboard+gamepad proof; scripted four-slot tests. No player-vs-player collision until separately specified.
- [ ] **R05. Narrow WorldDefinition.** Validated level geometry, moving platform definitions, spawn/entrance data, unique stable collider IDs. Keep the original movement gym as a regression fixture.
- [ ] **R06. Commit-only gameplay results/events.** One WorldStepResult with player snapshots, contacts, finalized semantic events (jump/land/impact/crush/respawn/interact/level completion as actually supported). Event ID, tick, world/session scope, source player, relevant contact/context. No events on rolled-back steps. Contract shared by UI/audio/VFX/camera—no competing buses.

**Gate B:** Player 1 respawns without altering world time, platform trajectory, Player 2 state or other players' input; late-spawn player samples the correct platform phase. Identical per-tick input produces identical snapshots and event signatures.

### Phase C — States, abilities, and a real game-facing level

- [ ] **R07. A09 character state architecture.** Orthogonal locomotion/action/condition states only where useful; explicit guards, transitions, ownership and diagnostics. Keep states out of collision math.
- [ ] **R08. A10 ability coordinator.** Eligibility, atomic activation, request priorities, concurrency/interrupt/cancellation rules, cooldowns/resource costs, reset behavior. Build **one** complete reference ability (dash or wall jump after design selection) and gym regressions; never direct-write player position from an ability.
- [ ] **R09. Scene coordinator Phase 1–2 (SM).** One active major scene; typed deferred navigation; launch/title/gameplay switching; minimal lifecycle and scoped ownership; developer direct-to-test-level entry. No general-purpose ECS.
- [ ] **R10. UI/scene Phase 3.** Semantic menu input, overlay stack, focus navigation, title/pause/settings/controls and confirmation wireframes/functionality. Modal UI consumes inputs; pause stops world tick while UI clock continues; no resume catch-up burst.
- [ ] **R11. Scene Phase 4 + authored level.** Stable LevelId/EntranceId, restart vs player respawn, checkpoint/test exit as required, simple synchronous content loading, ownership/disposal. First short playable graybox level with run-jump-land, platform, collectible/trigger, completion/exit. Movement gym remains standalone.
- [ ] **R12. Scene Phase 5–6.** Safe transition/input gates, truthful load feedback only when needed, failed destination fallback, repeated restart/load/unload/regression and gamepad UI tests.

**Gate C:** Title → gameplay → pause/settings → resume → restart → exit → title works repeatedly; a complete level runs without changing authoritative physics; two players can be independent in tests; one ability works through the request/state pipeline.

### Phase D — Presentation foundations and basic game feel

- [ ] **R13. Camera2D/viewport boundary.** World-to-screen mapping, interpolation, follow/look-ahead/dead zone/bounds, resize, optional presentation-only shake; multi-target policy stays open. Moving camera never alters world results.
- [ ] **R14. Animation/presentation models.** Read-only character snapshots/events, animation state and small visual-only tween runner; no animation-driven physics without a modeled gameplay event.
- [ ] **R15. Audio AS Phase 1–3.** Shared committed event adapter, MonoGame sound backend, validated cue/voice policies, category mixing and single-track music, jump/land/footstep variations; headless policy tests plus Audio Playground.
- [ ] **R16. Particle PS Phase 1–3.** Use authoritative PS-01–PS-30 plan. Headless CPU pool, bounded spawn/timing, generation-safe handles/ownership, priority and quality rules; SpriteBatch visual playground and multi-emitter effect definitions.
- [ ] **R17. Audio/particle Phase 4–5 integration.** Both consume **the same** semantic event envelope; dedup/stale-world protection, scene pause/respawn/ownership cleanup, audio 2D spatial/ambience and material/impact context. Never feed presentation outcomes back into simulation.
- [ ] **R18. Updated Juice P1–P3 vertical slice.** Run/start/stop/pivot feedback, jump takeoff, falling, landing soft/heavy, collectible feedback; restrained shake, dust, sound variation, squash/stretch. A/B all feedback OFF vs ON; no changes in collision positions or hitboxes. Accessibility reductions work.

**Gate D:** Each committed gameplay event maps to correct player-specific presentation once; no effects from rejected state; pause/menu/scene ownership correct; effects toggle does not change movement outcomes.

### Phase E — Rendering, lighting, content authoring

- [ ] **R19. Lighting Phase 1 prototype.** Ambient baseline, radial lights, MonoGame shaders/backend checks, render targets/pass restoration, Lighting Playground, frame/visual evidence.
- [ ] **R20. Lighting Phase 2–4 as justified.** Reusable materials, optional normals/emission, selected occluders and hard shadows, hybrid lit-world/forward transparency; prove camera layers and alpha/viewport behavior. Lighting never determines gameplay visibility unless separate deterministic gameplay rule is modeled.
- [ ] **R21. Lighting Phase 5–6 tuning.** Artistic controls, shadow/flash accessibility, verified reduced quality; measure GPU costs; advanced softness, height maps, bloom only when justified.
- [ ] **R22. Versioned settings and designer tools.** Validated movement/ability/level/effect/camera/light schemas, authoring constraints, trajectory/collision inspectors, asset validation, replay/test entry points. Add editor complexity only when real content authoring needs it.

**Gate E:** A second scene/content set can be authored without direct simulation code edits; hybrid rendering verified on target backend; optional quality reduction preserves gameplay and important visual cues.

### Phase F — Quality engineering throughout, final integration

- [ ] **R23. Continuous test/observability infrastructure (start early, not last).** CI tests, deterministic per-tick scripted input, state/event fingerprints, fixed-time long-run replay, scene ownership and device-disconnect tests; low-overhead timing/allocation/GC/collision counters; repeatable profiling capture.
- [ ] **R24. Evidence-gated optimizations.** Treat OPT-01–OPT-24 as a proposed reference; measure first. Compare collision candidate provider implementations (linear baseline before grids/trees), safe batching/atlas/culling, allocations, render passes and FX caps only where measured. No unproven global data-layout rewrite.
- [ ] **R25. Playtesting, accessibility and reference vertical slice.** Keyboard and gamepad trials, two-player smoke, readable HUD/UI, effects-off and reduced-effects tests, frame pacing, reliable load/unload, no physics regressions; iterate challenge and polish.

**Gate F:** Every deployed subsystem has an observable acceptance test, baseline measurements and clean lifecycle; one playable demo exercises it all.

## 4. Dependency guide

- **R02 before credible local-controller player proof; R03 before R04 independent respawn / late joins.**
- **R03/R04 before robust R06 event ownership, scene lifetime and per-player presentation integration.**
- **R01/R02/R07 before R08 abilities compete for control.** The state architecture can be developed in parallel with world plumbing once ownership rules are stable.
- **R09 before R10/R11 scene switching; R06 before audio/VFX gameplay integration; R13 before world-space VFX and camera-dependent lighting.**
- **R15 and R16 core/playgrounds can proceed in parallel; integrate on one shared AS-25/PS presentation contract.**
- **R23 basic capture starts now; R24 heavy optimization waits for evidence.**

## 5. Explicit deferrals / unresolved policy

- Full networking/rollback/matchmaking; shared vs split-screen; local join/leave UX; player-player collision rules; extensive enemy/combat/progression; one-way/slopes/non-AABB collision; a universal ECS or editor; advanced GPU particles, middleware audio, multiple-song crossfades, geometric shadow softness/height-map tricks.
- The documents approve **architectural directions**, not exact art direction, feature-specific HUD, numerical cost budgets, release platforms, all future game mechanics, or specific backend implementation techniques.

## 6. Mandatory delivery checklist for every task

1. All projects build; all existing tests pass; new headless regression tests cover success, rejection, rollback, ordering and lifecycle where applicable.
2. Dedicated gym/playground demonstrates new behavior interactively with clear toggles/diagnostics when appropriate.
3. Strict diagnostics remain strict; Diagnostic and Resilient modes retain bounded, reported recovery without swallowing unknown faults.
4. Long-running deterministic regression and replay comparison for changed simulation/time ownership.
5. No authoritative gameplay influence from presentation, UI animation, audio/VFX, camera or lighting.
6. Checkpoint source; update this roadmap's task status and known limitations after user validation.

**Immediate next task:** R00 checkpoint and R01 A08 Phase 2 conflict-testing gym, followed by R02 input adapters and R03 shared WorldSession time. This is a revised synthesis, not a source-code patch.
