using System;
using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.World;

public sealed class PO02SnapshotCacheTests
{
    [Fact]
    public void SnapshotsRetainTheirRevisionAndNeverReplayEvents()
    {
        var world = SharedWorldClockGym.Create();
        var first = world.Snapshot();
        var platforms = world.MovingPlatforms;
        if (EnginePerformanceOptions.Snapshots)
        {
            Assert.Same(first, world.Snapshot());
            Assert.Same(platforms, world.MovingPlatforms);
            Assert.Same(world.StaticColliders, world.StaticColliders);
        }
        var result = world.Step(Array.Empty<WorldPlayerCommand>());
        var current = world.Snapshot();
        Assert.Equal(0L, first.WorldTick);
        Assert.Equal(result.WorldTick, current.WorldTick);
        Assert.Equal(result.Players[0], current.Players[0]);
        Assert.Empty(current.Events);
        Assert.NotSame(first, current);
        Assert.NotSame(platforms, world.MovingPlatforms);
        world.RemovePlayer(2);
        Assert.Single(world.Snapshot().Players);
        Assert.Equal(2, current.Players.Count);
        world.AddPlayer(2, SharedWorldClockGym.SpawnBounds(world, 2));
        Assert.Equal(2, world.Snapshot().Players.Count);
        var beforeRespawn = world.Snapshot();
        world.RespawnPlayer(1, SharedWorldClockGym.SpawnBounds(world, 1));
        Assert.NotSame(beforeRespawn, world.Snapshot());
        Assert.Equal(result.WorldTick, world.WorldTick);
    }
}
