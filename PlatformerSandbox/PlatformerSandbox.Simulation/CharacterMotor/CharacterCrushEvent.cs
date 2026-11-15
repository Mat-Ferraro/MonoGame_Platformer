using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// A classified physical constraint, not a numerical-recovery request.
/// The body is a PRE-COMMIT snapshot: no intersecting bounds are published.
/// PushingColliderId identifies the moving surface closing the gap.
/// OpposingColliderId identifies the surface preventing displacement.
/// </summary>
public sealed record CharacterCrushEvent
{
    public CharacterCrushEvent(int pushingColliderId, int opposingColliderId,
        CharacterCrushAxis axis, double worldTimeSeconds, Aabb bodyBounds)
    {
        if (pushingColliderId < 0) throw new ArgumentOutOfRangeException(nameof(pushingColliderId));
        if (opposingColliderId < 0 || opposingColliderId == pushingColliderId)
            throw new ArgumentOutOfRangeException(nameof(opposingColliderId));
        if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
        if (!double.IsFinite(worldTimeSeconds) || worldTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(worldTimeSeconds));
        if (!bodyBounds.IsValid) throw new ArgumentException("Invalid body.", nameof(bodyBounds));
        PushingColliderId = pushingColliderId;
        OpposingColliderId = opposingColliderId;
        Axis = axis;
        WorldTimeSeconds = worldTimeSeconds;
        BodyBounds = bodyBounds;
    }

    public int PushingColliderId { get; }
    public int OpposingColliderId { get; }
    public CharacterCrushAxis Axis { get; }
    public double WorldTimeSeconds { get; }
    public Aabb BodyBounds { get; }
}
