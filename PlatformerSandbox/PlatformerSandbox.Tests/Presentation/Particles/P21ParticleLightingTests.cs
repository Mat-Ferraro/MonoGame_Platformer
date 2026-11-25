using System;
using PlatformerSandbox.Foundation.Presentation.Particles;

namespace PlatformerSandbox.Tests.Presentation.Particles;

public sealed class P21ParticleLightingTests
{
    [Theory]
    [InlineData(ParticleCoordinateSpace.World, ParticleLightingMode.Scene, true)]
    [InlineData(ParticleCoordinateSpace.World, ParticleLightingMode.Unlit, false)]
    [InlineData(ParticleCoordinateSpace.Screen, ParticleLightingMode.Scene, false)]
    [InlineData(ParticleCoordinateSpace.Screen, ParticleLightingMode.Unlit, false)]
    public void SceneLightingIsWorldOnlyAndExplicitlyOptional(ParticleCoordinateSpace space,
        ParticleLightingMode mode, bool expected) =>
        Assert.Equal(expected, ParticleLightingPolicy.ReceivesSceneLight(space, mode));

    [Theory]
    [InlineData(ParticleVisualLayer.Background, ParticleBlendMode.Alpha)]
    [InlineData(ParticleVisualLayer.World, ParticleBlendMode.Alpha)]
    [InlineData(ParticleVisualLayer.Foreground, ParticleBlendMode.Additive)]
    [InlineData(ParticleVisualLayer.World, ParticleBlendMode.Additive)]
    public void ExistingWorldEffectsReceiveSceneLightRegardlessOfDrawOrderOrBlend(
        ParticleVisualLayer layer, ParticleBlendMode blend)
    {
        var part = new ParticleEffectPartDefinition { EmitterId = "dust", Layer = layer, Blend = blend };
        part.Validate();
        Assert.Equal(ParticleLightingMode.Scene, part.Lighting);
        Assert.True(ParticleLightingPolicy.ReceivesSceneLight(part.Space, part.Lighting));
        Assert.False(ParticleLightingPolicy.ReceivesSceneLight(part.Space,
            (part with { Lighting = ParticleLightingMode.Unlit }).Lighting));
    }

    [Fact]
    public void OldAndExplicitOptOutRecipesDeserialize()
    {
        const string json = """
        {"version":1,"emitters":[{"id":"burst","burstCount":4}],
         "effects":[{"id":"fx.test","parts":[{"emitterId":"burst","blend":"Additive"},
         {"emitterId":"burst","lighting":"Unlit"}]}]}
        """;
        var parts = ParticleCompositeCatalog.FromJson(json).Get("fx.test").Parts!;
        Assert.Equal(ParticleLightingMode.Scene, parts[0].Lighting);
        Assert.Equal(ParticleLightingMode.Unlit, parts[1].Lighting);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            ParticleCompositeCatalog.FromJson(json.Replace("Unlit", "NotARealMode")));
    }

    [Fact]
    public void InvalidLightingModeIsRejected()
    {
        var part = new ParticleEffectPartDefinition { EmitterId = "dust", Lighting = (ParticleLightingMode)99 };
        Assert.Throws<ArgumentOutOfRangeException>(() => part.Validate());
    }
}
