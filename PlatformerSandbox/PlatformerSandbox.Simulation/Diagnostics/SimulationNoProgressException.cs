namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// The same unresolved event cannot be processed repeatedly without either
/// advancing representable time or changing the constrained motion state.
/// </summary>
public sealed class SimulationNoProgressException : InvalidOperationException
{
    public SimulationNoProgressException(string message) : base(message) { }
}
