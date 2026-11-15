using System;
using System.Collections.Generic;

using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Gameplay.Input.Requests;

/// <summary>One immutable set of resolved input intentions for one fixed step.</summary>
public readonly record struct MovementFrameIntent(
    HorizontalDirection Direction, bool JumpPressed, bool JumpReleased);

/// <summary>
/// Observational diagnostics from the exact arbitration that supplied gameplay
/// input. Null source/priority means no horizontal request was present.
/// </summary>
public readonly record struct MovementResolution(
    MovementFrameIntent Intent, int? WinningSourceId, int? WinningPriority,
    int HorizontalRequestCount, int TotalRequestCount);

/// <summary>
/// Pure, deterministic request arbitration. It owns no simulation state,
/// executes no abilities, and never edits physics values.
///
/// For horizontal requests: greatest priority wins; an equal-priority tie
/// selects the lowest stable SourceId. For jump edges: independent OR; the
/// existing BufferedJumpInput decides if and when a press becomes a jump.
/// Duplicate (SourceId, Kind) pairs are rejected rather than depending on
/// collection iteration order. Request ordering never affects the result.
/// </summary>
public static class MovementRequestResolver
{
    public static MovementFrameIntent Resolve(IReadOnlyList<MovementRequest> requests) =>
        ResolveDetailed(requests).Intent;

    /// <summary>
    /// Resolve once, with stable selection metadata for testing and HUD.
    /// No state is mutated; request enumeration order never determines ties.
    /// </summary>
    public static MovementResolution ResolveDetailed(IReadOnlyList<MovementRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var usedKeys = new HashSet<(int SourceId, MovementRequestKind Kind)>();
        HorizontalDirection selected = HorizontalDirection.Neutral;
        int selectedPriority = 0;
        int selectedSourceId = int.MaxValue;
        bool hasHorizontal = false;
        int horizontalCount = 0;
        bool pressed = false;
        bool released = false;

        foreach (MovementRequest request in requests)
        {
            if (!Enum.IsDefined(request.Kind))
                throw new ArgumentOutOfRangeException(nameof(requests),
                    $"Unknown movement request kind: {request.Kind}.");
            if (request.SourceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(requests),
                    "Movement request source IDs must be positive.");
            if (request.Priority < 0)
                throw new ArgumentOutOfRangeException(nameof(requests),
                    "Movement request priorities must be nonnegative.");
            if (!Enum.IsDefined(request.Direction))
                throw new ArgumentOutOfRangeException(nameof(requests),
                    $"Invalid horizontal direction: {request.Direction}.");
            if (request.Kind != MovementRequestKind.Horizontal &&
                request.Direction != HorizontalDirection.Neutral)
                throw new ArgumentException(
                    "Jump requests cannot carry horizontal direction.", nameof(requests));
            if (!usedKeys.Add((request.SourceId, request.Kind)))
                throw new ArgumentException(
                    "Duplicate request from the same source for the same action.",
                    nameof(requests));

            switch (request.Kind)
            {
                case MovementRequestKind.Horizontal:
                    horizontalCount++;
                    if (!hasHorizontal || request.Priority > selectedPriority ||
                        (request.Priority == selectedPriority &&
                         request.SourceId < selectedSourceId))
                    {
                        selected = request.Direction;
                        selectedPriority = request.Priority;
                        selectedSourceId = request.SourceId;
                        hasHorizontal = true;
                    }
                    break;
                case MovementRequestKind.JumpPress:
                    pressed = true;
                    break;
                case MovementRequestKind.JumpRelease:
                    released = true;
                    break;
            }
        }

        return new MovementResolution(
            new MovementFrameIntent(selected, pressed, released),
            hasHorizontal ? selectedSourceId : null,
            hasHorizontal ? selectedPriority : null,
            horizontalCount, requests.Count);
    }
}
