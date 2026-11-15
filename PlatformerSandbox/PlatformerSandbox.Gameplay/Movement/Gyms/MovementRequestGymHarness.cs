using System;
using System.Collections.Generic;
using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>Interactive A08 test cases, not ability or production input policy.</summary>
public enum MovementRequestGymScenario
{
    ControlLock = 0,
    ForceLeft = 1,
    ForceRight = 2,
    EqualPriorityTie = 3,
    KeyboardOnly = 4
}

/// <summary>
/// Builds the same typed requests for both the playable gym and headless tests.
/// Reversing submission order changes only enumeration order, not semantics.
/// Synthetic sources are gym fixtures, not player/device identities.
/// </summary>
public static class MovementRequestGymHarness
{
    public const int DemoPriority = 100;
    public const int TiePriority = 80;
    public const int LockSourceId = 20;
    public const int ForceSourceId = 21;
    public const int TiePreferredSourceId = 30;
    public const int TieOtherSourceId = 31;

    public static MovementRequestGymScenario Next(MovementRequestGymScenario scenario)
    {
        if (!Enum.IsDefined(scenario))
            throw new ArgumentOutOfRangeException(nameof(scenario));
        return (MovementRequestGymScenario)(((int)scenario + 1) % 5);
    }

    /// <summary>
    /// Caller-owned scratch buffer reused each fixed tick; no artificial
    /// commands are submitted when KeyboardOnly is selected.
    /// </summary>
    public static void Populate(
        List<MovementRequest> destination,
        MovementRequestGymScenario scenario,
        HorizontalDirection keyboardDirection,
        bool jumpPressed, bool jumpReleased,
        bool reverseSubmissionOrder = false,
        int primarySourceId = MovementRequestSources.Keyboard)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(scenario))
            throw new ArgumentOutOfRangeException(nameof(scenario));
        if (!Enum.IsDefined(keyboardDirection))
            throw new ArgumentOutOfRangeException(nameof(keyboardDirection));

        if (primarySourceId <= 0 || primarySourceId == LockSourceId ||
            primarySourceId == ForceSourceId || primarySourceId == TiePreferredSourceId ||
            primarySourceId == TieOtherSourceId)
            throw new ArgumentOutOfRangeException(nameof(primarySourceId));

        destination.Clear();
        destination.Add(MovementRequest.Horizontal(
            primarySourceId, keyboardDirection));
        if (jumpPressed)
            destination.Add(MovementRequest.JumpPress(primarySourceId));
        if (jumpReleased)
            destination.Add(MovementRequest.JumpRelease(primarySourceId));

        switch (scenario)
        {
            case MovementRequestGymScenario.ControlLock:
                destination.Add(MovementRequest.Horizontal(
                    LockSourceId, HorizontalDirection.Neutral, DemoPriority));
                break;
            case MovementRequestGymScenario.ForceLeft:
                destination.Add(MovementRequest.Horizontal(
                    ForceSourceId, HorizontalDirection.Left, DemoPriority));
                break;
            case MovementRequestGymScenario.ForceRight:
                destination.Add(MovementRequest.Horizontal(
                    ForceSourceId, HorizontalDirection.Right, DemoPriority));
                break;
            case MovementRequestGymScenario.EqualPriorityTie:
                // Both tie candidates outrank the keyboard. The lower stable
                // source ID wins regardless of their insertion order.
                destination.Add(MovementRequest.Horizontal(
                    TieOtherSourceId, HorizontalDirection.Left, TiePriority));
                destination.Add(MovementRequest.Horizontal(
                    TiePreferredSourceId, HorizontalDirection.Right, TiePriority));
                break;
            case MovementRequestGymScenario.KeyboardOnly:
                break;
        }

        if (reverseSubmissionOrder)
            destination.Reverse();
    }
}
