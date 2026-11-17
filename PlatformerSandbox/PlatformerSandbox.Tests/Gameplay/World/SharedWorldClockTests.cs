using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Gameplay.World;

public sealed class SharedWorldClockTests
{
    private const double Step = MovementGymSession.FixedStepSeconds;

    [Fact]
    public void EveryPlayerAndPlatformUseTheSameIntegerDerivedWorldTime()
    {
        WorldSession world = SharedWorldClockGym.Create();
        for (int i = 0; i < 240; i++)
        {
            WorldStepResult result = world.Step(Array.Empty<WorldPlayerCommand>());
            Assert.Equal((long)i + 1, result.WorldTick);
            Assert.Equal((i + 1) * Step, result.WorldTimeSeconds);
        }
        Assert.Equal(240L, world.WorldTick);
        var platform = Assert.Single(world.MovingPlatforms);
        Assert.Equal(world.WorldTimeSeconds, platform.TimeSeconds);
        Assert.Equal(2, world.PlayerCount);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(1).MovingSupportId);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(2).MovingSupportId);
    }

    [Fact]
    public void TwoRidersCrossSixtySecondWorldClockWithoutLosingSharedSupport()
    {
        WorldSession world = SharedWorldClockGym.Create();
        for (int i = 0; i < 3605; i++)
            world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Equal(3605L, world.WorldTick);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(1).MovingSupportId);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(2).MovingSupportId);
        Assert.Equal(world.WorldTimeSeconds, Assert.Single(world.MovingPlatforms).TimeSeconds);
    }

    [Fact]
    public void RespawningOneCharacterMidTrajectoryPreservesOtherPlayerAndWorld()
    {
        WorldSession world = SharedWorldClockGym.Create();
        for (int i = 0; i < 73; i++)
            world.Step(Array.Empty<WorldPlayerCommand>());
        long tick = world.WorldTick;
        double time = world.WorldTimeSeconds;
        WorldPlayerSnapshot other = world.GetPlayer(2);
        Aabb platform = Assert.Single(world.MovingPlatforms).Bounds;
        world.RespawnPlayer(1, SharedWorldClockGym.SpawnBounds(world, 1));

        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(time, world.WorldTimeSeconds);
        Assert.Equal(platform, Assert.Single(world.MovingPlatforms).Bounds);
        Assert.Equal(other, world.GetPlayer(2));
        Assert.True(world.GetPlayer(1).IsGrounded);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(1).MovingSupportId);
        world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Equal(tick + 1, world.WorldTick);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(1).MovingSupportId);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(2).MovingSupportId);
    }

    [Fact]
    public void LateJoiningPlayerStartsOnCurrentPlatformNotAtTimeZero()
    {
        WorldSession world = SharedWorldClockGym.Create();
        for (int i = 0; i < 77; i++)
            world.Step(Array.Empty<WorldPlayerCommand>());
        MovingPlatformSample sample = Assert.Single(world.MovingPlatforms);
        Aabb spawn = new(
            new Vector2D(sample.Bounds.Left + 3.5, sample.Bounds.Top),
            new Vector2D(sample.Bounds.Left + 4.3, sample.Bounds.Top + 1.6));
        world.AddPlayer(3, spawn);
        Assert.Equal(spawn, world.GetPlayer(3).Bounds);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(3).MovingSupportId);
        world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Equal(3, world.PlayerCount);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(3).MovingSupportId);
    }

    [Fact]
    public void IndependentCommandsDoNotSharePlayerMovementState()
    {
        WorldSession world = SharedWorldClockGym.Create();
        double leftBefore = world.GetPlayer(1).Bounds.Left;
        double rightBefore = world.GetPlayer(2).Bounds.Left;
        double platformBefore = Assert.Single(world.MovingPlatforms).Bounds.Left;
        for (int i = 0; i < 9; i++)
            world.Step([
                new WorldPlayerCommand(1, HorizontalDirection.Left),
                new WorldPlayerCommand(2, HorizontalDirection.Right)
            ]);
        double shift = Assert.Single(world.MovingPlatforms).Bounds.Left - platformBefore;
        Assert.True(world.GetPlayer(1).Bounds.Left - leftBefore < shift,
            "P1 left input must reduce movement relative to the moving platform.");
        Assert.True(world.GetPlayer(2).Bounds.Left - rightBefore > shift,
            "P2 right input must increase movement relative to the moving platform.");
    }

    [Fact]
    public void CommandEnumerationOrderNeverChangesPlayerSimulationOrder()
    {
        WorldSession a = SharedWorldClockGym.Create();
        WorldSession b = SharedWorldClockGym.Create();
        for (int i = 0; i < 12; i++)
        {
            var p1 = new WorldPlayerCommand(1, HorizontalDirection.Right);
            var p2 = new WorldPlayerCommand(2, HorizontalDirection.Left);
            a.Step([p1, p2]);
            b.Step([p2, p1]);
        }
        Assert.Equal(a.WorldTick, b.WorldTick);
        Assert.Equal(a.GetPlayer(1), b.GetPlayer(1));
        Assert.Equal(a.GetPlayer(2), b.GetPlayer(2));
    }

    [Fact]
    public void InvalidAndDuplicateCommandsCannotAdvanceAnyCharacterOrWorldTime()
    {
        WorldSession world = SharedWorldClockGym.Create();
        WorldStepResult before = world.Snapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([
            new WorldPlayerCommand(99, HorizontalDirection.Right)
        ]));
        Assert.Throws<ArgumentException>(() => world.Step([
            new WorldPlayerCommand(1, HorizontalDirection.Left),
            new WorldPlayerCommand(1, HorizontalDirection.Right)
        ]));
        Assert.Equal(before.WorldTick, world.WorldTick);
        Assert.Equal(before.WorldTimeSeconds, world.WorldTimeSeconds);
        Assert.Equal(before.Players[0], world.GetPlayer(1));
        Assert.Equal(before.Players[1], world.GetPlayer(2));
    }

    [Fact]
    public void InvalidRespawnDoesNotReplacePlayerOrMoveWorldClock()
    {
        WorldSession world = SharedWorldClockGym.Create();
        for (int i = 0; i < 20; i++)
            world.Step(Array.Empty<WorldPlayerCommand>());
        WorldPlayerSnapshot previous = world.GetPlayer(1);
        long tick = world.WorldTick;
        Assert.Throws<InvalidOperationException>(() => world.RespawnPlayer(1,
            new Aabb(new Vector2D(-2, -1.9), new Vector2D(-1.2, -0.3))));
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(previous, world.GetPlayer(1));
    }

    [Fact]
    public void RemovingAPlayerDoesNotAffectWorldProgressForOthers()
    {
        WorldSession world = SharedWorldClockGym.Create();
        world.RemovePlayer(1);
        Assert.False(world.HasPlayer(1));
        Assert.True(world.HasPlayer(2));
        for (int i = 0; i < 10; i++)
            world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Equal(10L, world.WorldTick);
        Assert.Equal(SharedWorldClockGym.PlatformId, world.GetPlayer(2).MovingSupportId);
    }
}
