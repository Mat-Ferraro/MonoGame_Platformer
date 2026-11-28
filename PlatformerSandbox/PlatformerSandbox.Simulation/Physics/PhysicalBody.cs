using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Physics;

// Body identity is scoped to a world instance. Existing ID namespaces are not
// merged: a character ID can equal a terrain collider ID without aliasing.
public enum PhysicalBodyKind { Static = 1, KinematicPlatform, KinematicCharacter, Dynamic }
public enum MotionAuthority { Fixed, AuthoredTrajectory, CharacterController, DynamicIntegrator }

public readonly record struct PhysicalBodyId
{
    public PhysicalBodyId(PhysicalBodyKind kind, int localId)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (localId < 0 || ((kind is PhysicalBodyKind.KinematicCharacter or PhysicalBodyKind.Dynamic) && localId == 0))
            throw new ArgumentOutOfRangeException(nameof(localId));
        Kind = kind; LocalId = localId;
    }
    public PhysicalBodyKind Kind { get; }
    public int LocalId { get; }
    public bool IsValid => Enum.IsDefined(Kind) && LocalId >= 0 &&
        (Kind is not (PhysicalBodyKind.KinematicCharacter or PhysicalBodyKind.Dynamic) || LocalId > 0);
    public MotionAuthority Authority => Kind switch
    {
        PhysicalBodyKind.Static => MotionAuthority.Fixed,
        PhysicalBodyKind.KinematicPlatform => MotionAuthority.AuthoredTrajectory,
        PhysicalBodyKind.KinematicCharacter => MotionAuthority.CharacterController,
        PhysicalBodyKind.Dynamic => MotionAuthority.DynamicIntegrator,
        _ => throw new InvalidOperationException("Invalid body identity.")
    };
}

/// <summary>Authored translational AABB definition. No dynamic integration in PB01A-C.
/// Mass belongs only to dynamic bodies; a character is never assigned a mass by this contract.</summary>
public sealed record PhysicalBodyDefinition
{
    public PhysicalBodyDefinition(PhysicalBodyId id, Vector2D halfExtents, string materialId, double? massOverride = null)
    {
        if (!id.IsValid) throw new ArgumentException("Invalid body identity.", nameof(id));
        double area = 4 * halfExtents.X * halfExtents.Y;
        if (halfExtents.X <= 0 || halfExtents.Y <= 0 || !double.IsFinite(area) || area <= 0)
            throw new ArgumentOutOfRangeException(nameof(halfExtents));
        if (string.IsNullOrWhiteSpace(materialId) || materialId != materialId.Trim())
            throw new ArgumentException("Invalid material ID.", nameof(materialId));
        if (massOverride is double mass && (id.Kind != PhysicalBodyKind.Dynamic || !double.IsFinite(mass) || mass <= 0))
            throw new ArgumentOutOfRangeException(nameof(massOverride));
        Id = id; HalfExtents = halfExtents; MaterialId = materialId; MassOverride = massOverride;
    }
    public PhysicalBodyId Id { get; }
    public Vector2D HalfExtents { get; }
    public string MaterialId { get; }
    public double? MassOverride { get; }
    public MotionAuthority Authority => Id.Authority;
    public double? ResolveMass(PhysicsMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        PhysicsMaterial material = materials.Require(MaterialId);
        if (Id.Kind != PhysicalBodyKind.Dynamic) return null;
        double mass = MassOverride ?? (4 * HalfExtents.X * HalfExtents.Y * material.AreaDensity);
        if (!double.IsFinite(mass) || mass <= 0) throw new InvalidOperationException("Invalid derived mass.");
        return mass;
    }
}

/// <summary>Detached committed body data; never a writable transform or motion request.</summary>
public readonly record struct PhysicalBodySnapshot
{
    public PhysicalBodySnapshot(PhysicalBodyId id, Aabb bounds, Vector2D velocity)
    {
        if (!id.IsValid || !bounds.IsValid) throw new ArgumentException("Invalid body snapshot.");
        if (id.Kind == PhysicalBodyKind.Static && velocity != Vector2D.Zero)
            throw new ArgumentException("Static bodies must be stationary.", nameof(velocity));
        Id = id; Bounds = bounds; Velocity = velocity;
    }
    public PhysicalBodyId Id { get; }
    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public MotionAuthority Authority => Id.Authority;
}

/// <summary>Stable ordering and unique IDs within one committed world. Caller owns capture timing.</summary>
public sealed class PhysicalBodyFrame
{
    public PhysicalBodyFrame(long worldInstanceId, long tick, IEnumerable<PhysicalBodySnapshot> bodies)
    {
        if (worldInstanceId <= 0 || tick < 0) throw new ArgumentOutOfRangeException(nameof(worldInstanceId));
        ArgumentNullException.ThrowIfNull(bodies);
        var ordered = bodies.OrderBy(b => b.Id.Kind).ThenBy(b => b.Id.LocalId).ToArray();
        var seen = new HashSet<PhysicalBodyId>();
        foreach (var body in ordered)
            if (!body.Id.IsValid || !body.Bounds.IsValid || !seen.Add(body.Id))
                throw new ArgumentException("Invalid or duplicate body in frame.", nameof(bodies));
        WorldInstanceId = worldInstanceId; Tick = tick; Bodies = Array.AsReadOnly(ordered);
    }
    public long WorldInstanceId { get; }
    public long Tick { get; }
    public IReadOnlyList<PhysicalBodySnapshot> Bodies { get; }
}
