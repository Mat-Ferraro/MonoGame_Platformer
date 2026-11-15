namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// A collision/support-event budget is a safety boundary, not a hint to keep
/// retrying. Callers may roll back to a checkpoint in Resilient mode.
/// </summary>
public sealed class SimulationEventLimitException : InvalidOperationException
{
    public SimulationEventLimitException(string message,
        IReadOnlyList<SimulationContactTrace>? contacts = null) : base(message)
    {
        Contacts = Array.AsReadOnly((contacts ??
            Array.Empty<SimulationContactTrace>()).ToArray());
    }

    public IReadOnlyList<SimulationContactTrace> Contacts { get; }
}
