using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Simulation.Physics;

public enum MaterialCombineRule { Minimum, Maximum, Average, Multiply, GeometricMean }

/// <summary>Gameplay units: mass per square world unit. Damping is a nonnegative rate.
/// Character movement profiles are independent; these coefficients are for future rigid-body contacts.</summary>
public sealed record PhysicsMaterial
{
    [JsonConstructor]
    public PhysicsMaterial(string id, double staticFriction, double dynamicFriction,
        double restitution, double areaDensity, double linearDamping)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim()) throw new ArgumentException("Invalid material ID.", nameof(id));
        if (!double.IsFinite(staticFriction) || staticFriction < 0 ||
            !double.IsFinite(dynamicFriction) || dynamicFriction < 0 || dynamicFriction > staticFriction)
            throw new ArgumentOutOfRangeException(nameof(dynamicFriction));
        if (!double.IsFinite(restitution) || restitution < 0 || restitution > 1)
            throw new ArgumentOutOfRangeException(nameof(restitution));
        if (!double.IsFinite(areaDensity) || areaDensity <= 0) throw new ArgumentOutOfRangeException(nameof(areaDensity));
        if (!double.IsFinite(linearDamping) || linearDamping < 0) throw new ArgumentOutOfRangeException(nameof(linearDamping));
        Id = id; StaticFriction = staticFriction; DynamicFriction = dynamicFriction;
        Restitution = restitution; AreaDensity = areaDensity; LinearDamping = linearDamping;
    }
    public string Id { get; }
    public double StaticFriction { get; }
    public double DynamicFriction { get; }
    public double Restitution { get; }
    public double AreaDensity { get; }
    public double LinearDamping { get; }
}

public readonly record struct ContactMaterial(double StaticFriction, double DynamicFriction, double Restitution);

public sealed class PhysicsMaterialCatalog
{
    private sealed class MaterialFile
    {
        public MaterialFile() { }
        public required int Version { get; init; }
        public required MaterialCombineRule FrictionCombine { get; init; }
        public required MaterialCombineRule RestitutionCombine { get; init; }
        public required MaterialEntry[] Materials { get; init; }
    }
    private sealed class MaterialEntry
    {
        public MaterialEntry() { }
        public required string Id { get; init; }
        public required double StaticFriction { get; init; }
        public required double DynamicFriction { get; init; }
        public required double Restitution { get; init; }
        public required double AreaDensity { get; init; }
        public required double LinearDamping { get; init; }
        public PhysicsMaterial Build() => new(Id, StaticFriction, DynamicFriction, Restitution, AreaDensity, LinearDamping);
    }
    public PhysicsMaterialCatalog(IEnumerable<PhysicsMaterial> materials,
        MaterialCombineRule frictionCombine = MaterialCombineRule.GeometricMean,
        MaterialCombineRule restitutionCombine = MaterialCombineRule.Maximum)
    {
        ArgumentNullException.ThrowIfNull(materials);
        if (!Enum.IsDefined(frictionCombine) || !Enum.IsDefined(restitutionCombine))
            throw new ArgumentOutOfRangeException(nameof(frictionCombine));
        var ordered = new SortedDictionary<string, PhysicsMaterial>(StringComparer.Ordinal);
        foreach (var material in materials)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (!ordered.TryAdd(material.Id, material)) throw new ArgumentException("Duplicate material: " + material.Id);
        }
        if (!ordered.ContainsKey("Default")) throw new ArgumentException("Default physics material required.");
        Materials = new ReadOnlyDictionary<string, PhysicsMaterial>(ordered);
        FrictionCombine = frictionCombine; RestitutionCombine = restitutionCombine;
    }
    public IReadOnlyDictionary<string, PhysicsMaterial> Materials { get; }
    public MaterialCombineRule FrictionCombine { get; }
    public MaterialCombineRule RestitutionCombine { get; }
    public PhysicsMaterial Require(string id) => Materials.TryGetValue(id, out var material)
        ? material : throw new ArgumentException("Unknown physics material: " + id, nameof(id));
    public ContactMaterial ResolveContact(string first, string second) => ResolveContact(first, second, FrictionCombine, RestitutionCombine);
    public ContactMaterial ResolveContact(string first, string second, MaterialCombineRule friction, MaterialCombineRule restitution)
    {
        var a = Require(first); var b = Require(second);
        return new(Combine(a.StaticFriction,b.StaticFriction,friction),
            Combine(a.DynamicFriction,b.DynamicFriction,friction), Combine(a.Restitution,b.Restitution,restitution));
    }
    private static double Combine(double a, double b, MaterialCombineRule rule)
    {
        double result = rule switch
        {
            MaterialCombineRule.Minimum => Math.Min(a,b), MaterialCombineRule.Maximum => Math.Max(a,b),
            MaterialCombineRule.Average => a/2 + b/2, MaterialCombineRule.Multiply => a*b,
            MaterialCombineRule.GeometricMean => Math.Sqrt(a)*Math.Sqrt(b),
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
        if (!double.IsFinite(result)) throw new InvalidOperationException("Material combination overflow.");
        return result;
    }
    public static PhysicsMaterialCatalog FromJson(string json)
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false));
        var file = JsonSerializer.Deserialize<MaterialFile>(json, options) ?? throw new JsonException("Missing material file.");
        if (file.Version != 1 || file.Materials is null) throw new JsonException("Unsupported material file version or missing materials.");
        return new(file.Materials.Select(entry => entry?.Build() ?? throw new JsonException("Null material entry.")),
            file.FrictionCombine, file.RestitutionCombine);
    }
}
