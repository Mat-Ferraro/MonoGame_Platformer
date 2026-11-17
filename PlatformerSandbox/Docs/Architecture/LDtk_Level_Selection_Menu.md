# Visible Level Selection and LDtk Terrain Route Fix

## Purpose

Replace the reliance on F1–F10 startup shortcuts with a navigable, readable
level-selection menu. Preserve developer hotkeys for existing regression workflows
without making them the only entry points.

## User flow

Title → **Select Level** → choose a named level/gym → transition fade → Gameplay.

Gameplay → Pause → **Select Level** → choose a new world → transition fade.

Escape / gamepad B returns from Level Select to Title or Pause, restoring the
parent menu and maintaining the paused simulation state when appropriate.

Level Select has ten entries: four LDtk levels, two route levels (including
Route B's EAST entrance), and four existing gyms. Gamepad/keyboard input routes
through existing semantic UI navigation. The renderer scrolls the list to keep
the current selection visible in smaller windows.

## Architecture

- `GameLevelMenuCatalog` is a headless, immutable, explicit menu catalog.
- A `GameUiMenuItem` can carry an optional typed `SceneDestination`.
- `GameFlowController` resolves a chosen destination and queues a normal
  `SceneTransitionRequest`; it never changes the world within the UI callback.
- `GameUiNavigation` owns the LevelSelect overlay and parent return stack.
- Scene commits and failed-load recovery retain the validated Scene Management
  Phase 5/6 behavior.
- `GameLevelIds.IsStandalonePlayableLevel` centralizes the standalone destination
  whitelist, including `LDTK_TERRAIN_DEMO`, preventing the Phase 4 route rejection.
- Route destinations remain checked by the validated route catalog, because
  entrances have route-specific meanings.
- Existing physics and collision structures are unchanged.

## Quality gates

1. Every catalog item has a unique destination and is accepted by the factory.
2. Opening/backing out of Level Select leaves the scene unchanged.
3. Choosing a level queues a transition, which is committed only at a safe point.
4. A failed preparation preserves the current scene and navigation context.
5. Selecting from Pause does not allow the old world to tick until transition.
6. Old test fixtures and debug shortcuts remain accessible.

## Deferred

Discovering arbitrary external `.ldtk` files at runtime, thumbnails, sorting,
metadata, mouse/touch navigation, and paging by level world are future authoring
features. The first menu deliberately lists only the explicitly supported levels.
