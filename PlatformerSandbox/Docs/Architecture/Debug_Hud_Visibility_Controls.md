# Debug Overlay Visibility — gameplay host

## Intent
Large developer panels in LDtk scenes obscure artwork and foreground/background
parallax. Developer test fixtures still require full diagnostics. This patch
keeps those needs separate with **presentation-only** visibility state.

## Modes

| Mode | Display | Required behavior |
|---|---|---|
| Full | All preexisting HUD panels, including the renderer's own diagnostics | Preserve full regression visibility |
| Compact | One small top-left status strip (scene/tick/players/control reminder) | Remove oversized panels |
| Hidden | No debug panels/strips | Reveal scene art and parallax completely |

The next mode cycles Full -> Compact -> Hidden -> Full. `~`/backtick is
available in gameplay globally; F11 is an LDtk-only alias because other
fixtures (A08 request resolver) already use F11. These inputs never become
movement, action, or world events.

LDtk-authored scenes begin in Compact mode; existing gyms begin in Full mode.
Once a user changes visibility manually, that choice persists across levels,
restarts and returning to the title until the application exits.

## Non-negotiable behavior

- Menus, pause overlays, transition curtains and failure/recovery notices stay
  visible even in Hidden mode. The visibility selection only hides debug UI.
- F9 hidden-collision outlines are a separate explicit debug option and are
  not silently disabled by the HUD toggle.
- Drawing still uses identical geometry/camera/visual layers. No change to
  authoritative simulation or gameplay timing.
- Older test fixtures still provide their diagnostic HUD in Full mode.

## Manual acceptance

1. Title -> F8 Parallax Demo. Verify a 25px compact strip, rather than the
   previous huge top-left and side HUDs.
2. F11: Hidden (no HUD). Move horizontally and jump, verifying parallax.
3. F11: Full (all existing diagnostics). F11: Compact again.
4. F9 on/off works independently of HUD. F11 affects neither collision state
   nor movement.
5. Hidden -> Pause: navigation appears, simulation is paused, Resume works.
6. Hidden -> T: reload does not return the HUD unexpectedly.
7. Title -> F4, F4 -> F2 -> N, F4 -> F1, and Route F5 -> F9 remain usable.
8. In the original movement gym, F10/F11/F12 still cycle request scenarios;
   backtick cycles only the HUD.
9. Run GameDebugHudModeTests, then the full xUnit suite.
