using System;
using System.Collections.Generic;
using System.Globalization;
using PlatformerSandbox.Diagnostics;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Motion.Profiles;
using Xunit;

namespace PlatformerSandbox.Tests.Diagnostics;

/// <summary>
/// D01 acceptance regression: a genuine validation failure inside the world
/// step must not publish a new authoritative tick. The crash context retains
/// the previous commit and the attempted, explicitly uncommitted operation.
/// </summary>
public sealed class D01FailedWorldStepTests
{
    [Fact]
    public void DuplicateCommandFailsInsideWorldStepWithoutAdvancingCommittedTick()
    {
        WorldSession world = SharedWorldClockGym.Create();
        var recorder = new DiagnosticContextRecorder();
        var command = new WorldPlayerCommand(1, HorizontalDirection.Right);

        WorldStepResult committed = world.Step(new[] { command });
        Assert.Equal(1L, committed.WorldTick);

        recorder.UpdateCommittedState(new Dictionary<string, string>
        {
            ["scene.levelId"] = "MOTION_LAB",
            ["world.committedTick"] = committed.WorldTick.ToString(CultureInfo.InvariantCulture),
            ["world.instanceId"] = world.WorldInstanceId.ToString(CultureInfo.InvariantCulture)
        });
        var duplicateCommands = new[] { command, command };
        recorder.BeginOperation("world.step", "P1 Right twice (intentional duplicate)",
            world.WorldTick + 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => world.Step(duplicateCommands));
        Assert.Contains("Duplicate player command", exception.Message);

        DiagnosticContextSnapshot context = recorder.Snapshot();
        Assert.Equal(1L, world.WorldTick);
        Assert.Equal("1", context.LastCommittedState["world.committedTick"]);
        Assert.Equal(world.WorldInstanceId.ToString(CultureInfo.InvariantCulture),
            context.LastCommittedState["world.instanceId"]);
        Assert.Equal("world.step", context.PendingOperation?.Kind);
        Assert.Equal(2L, context.PendingOperation?.WorldTick);
        Assert.Contains("intentional duplicate", context.PendingOperation!.Detail);
        Assert.Empty(world.Snapshot().Events);
        Assert.DoesNotContain(context.RecentActivity, e => e.Kind == "tick.committed");
    }
}
