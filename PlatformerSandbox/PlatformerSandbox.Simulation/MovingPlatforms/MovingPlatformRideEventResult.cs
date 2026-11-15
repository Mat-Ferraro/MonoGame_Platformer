using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// One analytically bounded supported ride phase. Contacts at the same impact
/// instant are reported as one ordered manifold; the legacy StaticContact
/// remains available for callers that inspect only the first static contact.
/// </summary>
public sealed record MovingPlatformRideEventResult(
    Aabb Bounds, Vector2D WorldVelocity, double AdvancedSeconds,
    bool IsSupported, CharacterControlContact? StaticContact)
{
    /// <summary>All external contacts, in stable collider-ID order. The existing support is implicit.</summary>
    public IReadOnlyList<CharacterControlContact> Contacts { get; init; } =
        Array.Empty<CharacterControlContact>();

    /// <summary>The authoritative support after the event, if any.</summary>
    public int? SupportPlatformId { get; init; }
}
