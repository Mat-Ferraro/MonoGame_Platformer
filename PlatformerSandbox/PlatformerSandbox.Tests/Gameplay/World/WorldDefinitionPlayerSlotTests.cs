using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Gameplay.World;

public sealed class WorldDefinitionPlayerSlotTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));

    [Fact]
    public void DefinitionCopiesInputArraysAndExposesStableIds()
    {
        StaticAabbCollider[] floors = [new(4, Box(-5, -2, 5, -1))];
        MovingPlatformTrajectory[] platforms = [new(5,
            Box(-4, 0, -2, .2), new Vector2D(1, 0), 2)];
        WorldSpawnPoint[] spawns = [new("P1", Box(-3.5, .2, -3, 1.2), 5)];
        var def = new WorldDefinition("EXAMPLE", floors, platforms, spawns);
        floors[0] = new StaticAabbCollider(99, Box(1, 3, 3, 4));
        spawns[0] = new WorldSpawnPoint("REPLACED", Box(0, 0, 1, 1));
        Assert.Equal("EXAMPLE", def.LevelId);
        Assert.Equal(4, Assert.Single(def.StaticColliders).Id);
        Assert.Equal("P1", Assert.Single(def.SpawnPoints).Id);
        Assert.False(def.HasSpawn("REPLACED"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<WorldSpawnPoint>)def.SpawnPoints)[0] = spawns[0]);
    }

    [Fact]
    public void WorldDefinitionRejectsDuplicateStaticAndMovingColliderIds()
    {
        var box = Box(-2, 0, 2, .2);
        Assert.Throws<ArgumentException>(() => new WorldDefinition("DUP",
            [new StaticAabbCollider(5, Box(-8, -2, 8, -1))],
            [new MovingPlatformTrajectory(5, box, new Vector2D(1, 0), 2)],
            [new WorldSpawnPoint("S", Box(4, 0, 4.8, 1.6))]));
    }

    [Fact]
    public void WorldDefinitionRejectsDuplicateSpawnIdsAndUnknownAnchors()
    {
        Assert.Throws<ArgumentException>(() => new WorldDefinition("DUP",
            Array.Empty<StaticAabbCollider>(), Array.Empty<MovingPlatformTrajectory>(),
            [new WorldSpawnPoint("S", Box(0, 0, 1, 1)),
             new WorldSpawnPoint("S", Box(2, 0, 3, 1))]));
        Assert.Throws<ArgumentException>(() => new WorldDefinition("NO_ANCHOR",
            Array.Empty<StaticAabbCollider>(), Array.Empty<MovingPlatformTrajectory>(),
            [new WorldSpawnPoint("S", Box(0, 0, 1, 1), 55)]));
    }

    [Fact]
    public void WorldDefinitionRejectsInvalidLevelIdsSpawnsAndSpawnPenetration()
    {
        Assert.Throws<ArgumentException>(() => new WorldDefinition(" ",
            Array.Empty<StaticAabbCollider>(), Array.Empty<MovingPlatformTrajectory>(),
            [new WorldSpawnPoint("S", Box(0, 0, 1, 1))]));
        Assert.Throws<ArgumentException>(() => new WorldSpawnPoint("S", default));
        Assert.Throws<ArgumentException>(() => new WorldDefinition("BLOCKED",
            [new StaticAabbCollider(4, Box(0, 0, 2, 2))],
            Array.Empty<MovingPlatformTrajectory>(),
            [new WorldSpawnPoint("S", Box(1, 1, 1.8, 2.6))]));
        Assert.Throws<ArgumentException>(() => new WorldDefinition("BLOCKED_MOVING",
            Array.Empty<StaticAabbCollider>(),
            [new MovingPlatformTrajectory(4, Box(0, 0, 2, .5), new Vector2D(1, 0), 2)],
            [new WorldSpawnPoint("S", Box(1, .25, 1.8, 1.4), 4)]));
        // Mere boundary contact on a platform is a valid spawn.
        Assert.True(AuthoredWorldGym.CreateDefinition().HasSpawn(AuthoredWorldGym.LeftSpawn));
    }

    [Fact]
    public void AuthoredAnchoredSpawnMovesWithItsPlatformButStaticSpawnDoesNot()
    {
        WorldDefinition def = AuthoredWorldGym.CreateDefinition();
        Aabb initial = def.ResolveSpawnBounds(AuthoredWorldGym.LeftSpawn, 0);
        double elapsed = 1.0;
        MovingPlatformTrajectory path = def.MovingPlatformTrajectories.Single(
            p => p.Id == AuthoredWorldGym.LeftPlatformId);
        Vector2D delta = path.Evaluate(elapsed).Bounds.Min - path.InitialBounds.Min;
        Assert.Equal(initial.Translated(delta),
            def.ResolveSpawnBounds(AuthoredWorldGym.LeftSpawn, elapsed));
        Assert.Equal(def.ResolveSpawnBounds(AuthoredWorldGym.GroundSpawn, 0),
            def.ResolveSpawnBounds(AuthoredWorldGym.GroundSpawn, elapsed));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            def.ResolveSpawnBounds("MISSING", elapsed));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            def.ResolveSpawnBounds(AuthoredWorldGym.LeftSpawn, double.NaN));
    }

    [Fact]
    public void AuthoredTwoPlayerWorldRegistersIndependentIdsAndPlatformSupport()
    {
        WorldSession world = AuthoredWorldGym.Create();
        Assert.Equal("GYM_TWO_PLATFORMS", world.Definition!.LevelId);
        Assert.Equal(2, world.PlayerCount);
        Assert.Equal(1001, world.GetPlayerSlot(1).CharacterId);
        Assert.Equal(1002, world.GetPlayerSlot(2).CharacterId);
        Assert.Equal(1, world.GetPlayerSlot(1).InputSourceId);
        Assert.Equal(22, world.GetPlayerSlot(2).InputSourceId);
        Assert.Equal(AuthoredWorldGym.LeftPlatformId, world.GetPlayer(1).MovingSupportId);
        Assert.Equal(AuthoredWorldGym.RightPlatformId, world.GetPlayer(2).MovingSupportId);
        Assert.Equal(new[] { 1, 2 }, world.PlayerSlots.Select(s => s.PlayerId).ToArray());
    }

    [Fact]
    public void LateJoinUsesCurrentPlatformPositionWithoutResettingExistingPlayers()
    {
        WorldSession world = AuthoredWorldGym.Create();
        for (int i = 0; i < 87; i++)
            world.Step(Array.Empty<WorldPlayerCommand>());
        var before = world.Snapshot();
        Aabb expected = world.Definition!.ResolveSpawnBounds(
            AuthoredWorldGym.GuestSpawn, world.WorldTimeSeconds);
        AuthoredWorldGym.JoinGuest(world);
        Assert.Equal(before.WorldTick, world.WorldTick);
        Assert.Equal(before.Players[0], world.GetPlayer(1));
        Assert.Equal(before.Players[1], world.GetPlayer(2));
        Assert.Equal(expected, world.GetPlayer(3).Bounds);
        Assert.Equal(AuthoredWorldGym.RightPlatformId, world.GetPlayer(3).MovingSupportId);
        Assert.Null(world.GetPlayerSlot(3).InputSourceId);
        world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Equal(3, world.PlayerCount);
        Assert.Equal(AuthoredWorldGym.RightPlatformId, world.GetPlayer(3).MovingSupportId);
    }

    [Fact]
    public void InvalidSlotRegistrationRejectsAtomically()
    {
        WorldSession world = AuthoredWorldGym.Create();
        WorldStepResult before = world.Snapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddPlayer(
            new WorldPlayerSlotRegistration(1, 1003, AuthoredWorldGym.GuestSpawn)));
        Assert.Throws<ArgumentException>(() => world.AddPlayer(
            new WorldPlayerSlotRegistration(3, 1001, AuthoredWorldGym.GuestSpawn)));
        Assert.Throws<ArgumentException>(() => world.AddPlayer(
            new WorldPlayerSlotRegistration(3, 1003, AuthoredWorldGym.GuestSpawn, 22)));
        Assert.Throws<ArgumentException>(() => world.AddPlayer(
            new WorldPlayerSlotRegistration(3, 1003, AuthoredWorldGym.GuestSpawn, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddPlayer(
            new WorldPlayerSlotRegistration(3, 1003, "UNKNOWN")));
        Assert.Equal(before.WorldTick, world.WorldTick);
        Assert.Equal(before.Players[0], world.GetPlayer(1));
        Assert.Equal(before.Players[1], world.GetPlayer(2));
        Assert.False(world.HasPlayer(3));
    }

    [Fact]
    public void DeviceReassignmentPreservesMotorAndRefusesDoubleOwnership()
    {
        WorldSession world = AuthoredWorldGym.Create();
        var before = world.Snapshot();
        world.AssignInputSource(1, 2);
        Assert.Equal(2, world.GetPlayerSlot(1).InputSourceId);
        Assert.Throws<ArgumentException>(() => world.AssignInputSource(2, 2));
        Assert.Throws<ArgumentException>(() => world.AssignInputSource(2, 0));
        Assert.Equal(22, world.GetPlayerSlot(2).InputSourceId);
        world.AssignInputSource(1, null);
        world.AssignInputSource(2, 1);
        Assert.Null(world.GetPlayerSlot(1).InputSourceId);
        Assert.Equal(1, world.GetPlayerSlot(2).InputSourceId);
        Assert.Equal(before.WorldTick, world.WorldTick);
        Assert.Equal(before.Players[0], world.GetPlayer(1));
        Assert.Equal(before.Players[1], world.GetPlayer(2));
    }

    [Fact]
    public void ChangingSpawnOnlyAffectsNextRespawnAndDoesNotRestartWorld()
    {
        WorldSession world = AuthoredWorldGym.Create();
        for (int i = 0; i < 27; i++)
            world.Step(Array.Empty<WorldPlayerCommand>());
        long tick = world.WorldTick;
        WorldPlayerSnapshot p1 = world.GetPlayer(1);
        WorldPlayerSnapshot p2 = world.GetPlayer(2);
        world.SetPlayerSpawn(1, AuthoredWorldGym.GroundSpawn);
        Assert.Equal(p1, world.GetPlayer(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetPlayerSpawn(1, "BAD"));
        Assert.Equal(AuthoredWorldGym.GroundSpawn, world.GetPlayerSlot(1).SpawnId);
        world.RespawnPlayer(1);
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(p2, world.GetPlayer(2));
        Assert.Equal(world.Definition!.ResolveSpawnBounds(AuthoredWorldGym.GroundSpawn,
            world.WorldTimeSeconds), world.GetPlayer(1).Bounds);
        Assert.True(world.GetPlayer(1).IsGrounded);
        Assert.Null(world.GetPlayer(1).MovingSupportId);
        Assert.Equal(1001, world.GetPlayerSlot(1).CharacterId);
    }

    [Fact]
    public void NamedRespawnOnMovingPlatformUsesCurrentWorldTime()
    {
        WorldSession world = AuthoredWorldGym.Create();
        for (int i = 0; i < 77; i++) world.Step(Array.Empty<WorldPlayerCommand>());
        long tick = world.WorldTick;
        WorldPlayerSnapshot p2 = world.GetPlayer(2);
        Aabb expected = world.Definition!.ResolveSpawnBounds(AuthoredWorldGym.LeftSpawn,
            world.WorldTimeSeconds);
        world.RespawnPlayer(1);
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(p2, world.GetPlayer(2));
        Assert.Equal(expected, world.GetPlayer(1).Bounds);
        Assert.Equal(AuthoredWorldGym.LeftPlatformId, world.GetPlayer(1).MovingSupportId);
    }

    [Fact]
    public void GuestJoinLeaveDoesNotResetWorldAndFormerIdCanBeReused()
    {
        WorldSession world = AuthoredWorldGym.Create();
        AuthoredWorldGym.JoinGuest(world);
        for (int i = 0; i < 12; i++) world.Step(Array.Empty<WorldPlayerCommand>());
        var p1 = world.GetPlayer(1);
        var p2 = world.GetPlayer(2);
        world.RemovePlayer(3);
        Assert.Equal(12L, world.WorldTick);
        Assert.Equal(p1, world.GetPlayer(1));
        Assert.Equal(p2, world.GetPlayer(2));
        AuthoredWorldGym.JoinGuest(world);
        Assert.Equal(3, world.PlayerCount);
        Assert.Equal(12L, world.WorldTick);
    }

    [Fact]
    public void DifferentCommandOrderProducesSameResultWithThreePlayers()
    {
        WorldSession a = AuthoredWorldGym.Create();
        WorldSession b = AuthoredWorldGym.Create();
        AuthoredWorldGym.JoinGuest(a);
        AuthoredWorldGym.JoinGuest(b);
        for (int i = 0; i < 80; i++)
        {
            var first = new WorldPlayerCommand(1, HorizontalDirection.Right);
            var second = new WorldPlayerCommand(2, HorizontalDirection.Left);
            a.Step([first, second]);
            b.Step([second, first]);
        }
        Assert.Equal(a.WorldTick, b.WorldTick);
        Assert.Equal(a.Snapshot().Players.ToArray(), b.Snapshot().Players.ToArray());
    }

    [Fact]
    public void InitialTickAndCurrentTimeAnchorsRemainAlignedOnWorldCreation()
    {
        WorldSession world = AuthoredWorldGym.Create(initialWorldTick: 91);
        Assert.Equal(91L, world.WorldTick);
        Assert.Equal(world.Definition!.ResolveSpawnBounds(AuthoredWorldGym.LeftSpawn,
            world.WorldTimeSeconds), world.GetPlayer(1).Bounds);
        Assert.Equal(AuthoredWorldGym.LeftPlatformId, world.GetPlayer(1).MovingSupportId);
        Assert.Equal(AuthoredWorldGym.RightPlatformId, world.GetPlayer(2).MovingSupportId);
    }
}
