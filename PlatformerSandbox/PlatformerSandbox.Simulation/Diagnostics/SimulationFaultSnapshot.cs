using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// Immutable pre-update state captured on a recognized simulation failure.
/// Keeping the BEFORE state allows deterministic reproduction and rollback.
/// </summary>
public sealed record SimulationFaultSnapshot(
    double WorldTimeSeconds,
    Aabb BodyBounds,
    Vector2D Velocity,
    bool WasGrounded,
    int? MovingSupportId,
    string FaultType,
    string Description,
    IReadOnlyList<SimulationContactTrace> Contacts);
