using System.Text.Json;
using PlatformerSandbox.Foundation.Presentation.Particles;

namespace PlatformerSandbox.Gameplay.Presentation;

/// <summary>
/// Game-specific semantic event-to-VFX mapping. Definitions and the particle
/// runtime remain reusable; the platformer adapter owns these effect IDs.
/// A null/empty optional binding disables that visual without changing gameplay.
/// </summary>
public sealed record ParticleGameplayBindings
{
    public int Version { get; init; }
    public string JumpStarted { get; init; } = "";
    public string Landed { get; init; } = "";
    public string WallHit { get; init; } = "";
    public string PulseActivated { get; init; } = "";
    public string PlayerRespawned { get; init; } = "";
    public string SceneAmbient { get; init; } = "";
    public string UiConfirm { get; init; } = "";
    public string UiBack { get; init; } = "";
    public string UiNavigate { get; init; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static ParticleGameplayBindings FromJson(string json, ParticleCompositeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("A gameplay VFX binding manifest is required.", nameof(json));
        ParticleGameplayBindings bindings = JsonSerializer.Deserialize<ParticleGameplayBindings>(json, JsonOptions)
            ?? throw new JsonException("Bindings must be a JSON object.");
        bindings.Validate(catalog);
        return bindings;
    }

    public void Validate(ParticleCompositeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (Version != 1)
            throw new NotSupportedException($"Unsupported VFX binding version {Version}.");
        Check(JumpStarted, nameof(JumpStarted), ParticleCoordinateSpace.World);
        Check(Landed, nameof(Landed), ParticleCoordinateSpace.World);
        Check(WallHit, nameof(WallHit), ParticleCoordinateSpace.World);
        Check(PulseActivated, nameof(PulseActivated), ParticleCoordinateSpace.World);
        Check(PlayerRespawned, nameof(PlayerRespawned), ParticleCoordinateSpace.World);
        Check(SceneAmbient, nameof(SceneAmbient), ParticleCoordinateSpace.World);
        Check(UiConfirm, nameof(UiConfirm), ParticleCoordinateSpace.Screen);
        Check(UiBack, nameof(UiBack), ParticleCoordinateSpace.Screen);
        Check(UiNavigate, nameof(UiNavigate), ParticleCoordinateSpace.Screen);

        void Check(string? effectId, string name, ParticleCoordinateSpace requiredSpace)
        {
            if (string.IsNullOrEmpty(effectId)) return; // Individual cues may be omitted.
            if (!catalog.TryGet(effectId, out ParticleCompositeDefinition def))
                throw new ArgumentException($"Binding {name} refers to unknown effect '{effectId}'.", name);
            foreach (ParticleEffectPartDefinition part in def.Parts!)
                if (part.Space != requiredSpace)
                    throw new ArgumentException($"Binding {name} has incorrect coordinate space.", name);
        }
    }
}
