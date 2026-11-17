using System.Reflection;
using System.Runtime.InteropServices;

namespace PlatformerSandbox.Diagnostics;

/// <summary>Safe environment information for the diagnostic report.</summary>
public sealed record DiagnosticBuildInfo(
    string Application,
    string Version,
    string BuildId,
    string Runtime,
    string OperatingSystem,
    string ProcessArchitecture)
{
    public static DiagnosticBuildInfo ForAssembly(Assembly assembly, string application)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        return new DiagnosticBuildInfo(
            application,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString() ?? "unknown",
            assembly.ManifestModule.ModuleVersionId.ToString("D"),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }
}
