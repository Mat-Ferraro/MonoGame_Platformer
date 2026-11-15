using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>Minimal stable diagnostic contact record (not a physics command).</summary>
public readonly record struct SimulationContactTrace(
    int ColliderId, double TimeWithinStepSeconds, Vector2D Normal);
