# A08 Phase 2 — Interactive Request Resolver Gym

## Scope
This patch builds on the validated **A08 Phase 1** package. It does not modify
Simulation physics, the obstacle layout, ability execution, fault policy, or
collision-assistance values. The playable gym keeps assistance enabled at launch.

## Controls

- **F10** enables/disables the Request Test overlay and synthetic request sources.
- **F11** advances to the next scenario *while F10 is enabled*.
- **F12** reverses submission order *while F10 is enabled*.
- **A/D** or arrows set keyboard direction; **Space** still requests jump.
- **F9** still toggles collision assistance; **F5/F6/F7** still select fault modes.

The new overlay is in the top right; the original fault/crush HUD remains top left.
Changing F10/F11/F12 resets the player/world to a known gym starting point for
repeatable comparisons. F12 should not change the winning source/direction for
an equivalent input sequence. It changes **enumeration order only**.

## F11 scenario order

1. `LOCK NEUTRAL`: fake source 20, priority 100, Neutral overrides keyboard direction.
   Character decelerates; jump press is independent and remains usable.
2. `FORCE LEFT`: source 21, priority 100, Left overrides keyboard.
3. `FORCE RIGHT`: source 21, priority 100, Right overrides keyboard.
4. `TIE ID30 RIGHT`: source 30, priority 80, Right versus source 31,
   priority 80, Left. ID 30 wins irrespective of request enumeration order.
5. `KEYBOARD ONLY`: input from source 1; behaves like F10 disabled.

All synthetic commands are pure typed movement requests. They do **not** mutate
velocity or position, and do not represent permanent game mechanics.

## Diagnostics
`MovementRequestResolver.ResolveDetailed` produces the *same* resolved
`MovementFrameIntent` as the original `Resolve`, plus observational winning
source ID, winning priority, number of horizontal requests and total requests.
`MovementGymSession.Step(..., out MovementResolution)` reports exactly what
was consumed **after** a successful step; exceptions are not reported as
successful commits. The old Step overloads are preserved.

## Validation

- Run the full Visual Studio xUnit suite; the new `MovementRequestGymHarnessTests`
  includes scenario, order-independence, jump-edge, invalid-input, and real
  gym-step tests.
- On first launch, confirm assistance shows ON and request test is OFF.
- F10 ON: hold D under lock; movement does not accelerate right, but Space jumps.
- F11 FORCE LEFT: hold D; the character moves left instead.
- F11 FORCE RIGHT: hold A; the character moves right instead.
- F11 TIE: source 30 always wins; F12 should switch `ORDER` only.
- F11 KEYBOARD ONLY: A/D and Space work as before.
- F10 OFF: compare with normal gym; verify four crush scenarios and F9/F5-F7.

## Known boundaries

The request system currently resolves horizontal *priority* and ORs jump edge
requests. It does not implement ability activation, action priorities, dynamic
control locks, player slots, gamepad mapping, or global input contexts. Those
belong to later A09/A10/A11 tasks. A high-priority Neutral request is a movement
*intent* lock; a character with existing velocity still decelerates naturally.
The HUD intentionally displays winning request, not a promise of immediate
velocity reversal or braking.
