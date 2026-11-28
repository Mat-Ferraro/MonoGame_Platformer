using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Physics;

namespace PlatformerSandbox.Gameplay.World;

/// <summary>PB01 observation adapter, not a second physics world. Read at committed tick boundaries.
/// Character identity uses CharacterId (not controller input ID). No motion state is changed.</summary>
public static class WorldPhysicalBodyView
{
    public static PhysicalBodyFrame Capture(WorldSession world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var bodies = new List<PhysicalBodySnapshot>();
        foreach (var collider in world.StaticColliders)
            bodies.Add(new(new(PhysicalBodyKind.Static,collider.Id),collider.Bounds,Vector2D.Zero));
        foreach (var platform in world.MovingPlatforms)
            bodies.Add(new(new(PhysicalBodyKind.KinematicPlatform,platform.PlatformId),platform.Bounds,platform.Velocity));
        var slots = world.PlayerSlots.ToDictionary(slot => slot.PlayerId);
        foreach (var player in world.Snapshot().Players.Where(p=>!p.IsDead))
            bodies.Add(new(new(PhysicalBodyKind.KinematicCharacter,slots[player.PlayerId].CharacterId),player.Bounds,player.Velocity));
        if(world.DynamicBodies is { } dynamics) bodies.AddRange(dynamics.Bodies.Select(b=>b.Snapshot));
        return new(world.WorldInstanceId,world.WorldTick,bodies);
    }
}
