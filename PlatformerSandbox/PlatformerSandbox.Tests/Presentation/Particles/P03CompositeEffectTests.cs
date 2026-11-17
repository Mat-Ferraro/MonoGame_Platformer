using System;
using System.Linq;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Particles;

namespace PlatformerSandbox.Tests.Presentation.Particles;

/// <summary>Headless acceptance tests for P03 compositing, timing and authoring.</summary>
public sealed class P03CompositeEffectTests
{
    private static ParticleEmitterDefinition Burst(string id = "burst", int count = 4) => new()
    {
        Id = id, BurstCount = count, MaxSpawnsPerUpdate = 100,
        LifetimeSeconds = new ParticleRange(.6, 1.0),
        Speed = new ParticleRange(1, 2),
        DirectionRadians = new ParticleRange(-Math.PI, Math.PI),
        StartSize = new ParticleRange(.1, .2),
        EndSize = new ParticleRange(0, 0)
    };

    private static ParticleEmitterDefinition Continuous(string id = "flow") => new()
    {
        Id = id, RatePerSecond = 10, MaxSpawnsPerUpdate = 100,
        LifetimeSeconds = new ParticleRange(2, 2),
        Speed = new ParticleRange(0, 0),
        StartSize = new ParticleRange(1, 1), EndSize = new ParticleRange(1, 1)
    };

    private static ParticleEffectPartDefinition Part(string emitterId,
        double delay = 0, double duration = 0, ParticleEmitterAnchor anchor = ParticleEmitterAnchor.Fixed,
        ParticleCoordinateSpace space = ParticleCoordinateSpace.World) => new()
    {
        EmitterId = emitterId, DelaySeconds = delay, DurationSeconds = duration,
        Anchor = anchor, Capacity = 32, Space = space,
        Layer = space == ParticleCoordinateSpace.Screen
            ? ParticleVisualLayer.Ui : ParticleVisualLayer.World
    };

    private static ParticleCompositeCatalog Catalog(
        ParticleEmitterDefinition[] emitters, params ParticleEffectPartDefinition[] parts) =>
        new(emitters, new[] { new ParticleCompositeDefinition { Id = "fx.test", Parts = parts } });

    private static ParticleCompositeInstance Play(ParticleCompositeRuntime runtime,
        Vector2D? location = null)
    {
        Assert.True(runtime.TryPlay("fx.test", location ?? Vector2D.Zero, out var instance));
        return Assert.IsType<ParticleCompositeInstance>(instance);
    }

    [Fact]
    public void CompositeSpawnsAllImmediatePartsWithSeparatePools()
    {
        var catalog = Catalog([Burst("a", 4), Burst("b", 6)], Part("a"), Part("b"));
        var runtime = new ParticleCompositeRuntime(catalog);
        ParticleCompositeInstance instance = Play(runtime, new Vector2D(5, 7));
        Assert.Equal(2, instance.Parts.Count);
        Assert.Equal(4, instance.Parts[0].Particles.ActiveCount);
        Assert.Equal(6, instance.Parts[1].Particles.ActiveCount);
        Assert.NotSame(instance.Parts[0].Particles, instance.Parts[1].Particles);
        Assert.True(instance.Parts[0].HasStarted);
    }

    [Fact]
    public void DelayedPartEmitsOnlyWhenItsOwnStartBoundaryIsCrossed()
    {
        var runtime = new ParticleCompositeRuntime(Catalog(
            [Burst("a", 4), Burst("b", 5)], Part("a"), Part("b", delay: .2)));
        var instance = Play(runtime);
        Assert.Equal(4, instance.Parts[0].Particles.ActiveCount);
        Assert.Equal(0, instance.Parts[1].Particles.ActiveCount);
        runtime.Update(.1);
        Assert.Equal(0, instance.Parts[1].Particles.ActiveCount);
        runtime.Update(.1);
        Assert.Equal(5, instance.Parts[1].Particles.ActiveCount);
        runtime.Update(.1);
        Assert.Equal(5, instance.Parts[1].Particles.ActiveCount);
    }

    [Fact]
    public void MovingSourceOnlyAffectsFollowEmitterAndNotExistingParticles()
    {
        var fixedPart = Part("a") with { OffsetX = 2 };
        var following = Part("a", anchor: ParticleEmitterAnchor.Follow) with { OffsetY = 3 };
        var runtime = new ParticleCompositeRuntime(Catalog([Continuous("a")], fixedPart, following));
        var instance = Play(runtime);
        runtime.Update(.1);
        instance.MoveSource(new Vector2D(10, 5));
        runtime.Update(.1);
        var fixedPool = instance.Parts[0].Particles;
        var movingPool = instance.Parts[1].Particles;
        Assert.Equal(2, fixedPool.ActiveCount);
        Assert.Equal(2, movingPool.ActiveCount);
        Assert.Equal(new Vector2D(2, 0), fixedPool.GetAt(0).Position);
        Assert.Equal(new Vector2D(2, 0), fixedPool.GetAt(1).Position);
        Assert.Equal(new Vector2D(0, 3), movingPool.GetAt(0).Position);
        Assert.Equal(new Vector2D(10, 8), movingPool.GetAt(1).Position);
    }

    [Fact]
    public void ContinuousPartsHaveIndependentTimersAndFiniteDurations()
    {
        var runtime = new ParticleCompositeRuntime(Catalog(
            [Continuous()], Part("flow", duration: .2),
            Part("flow", delay: .1, duration: .3)));
        var instance = Play(runtime);
        runtime.Update(.1);
        Assert.Equal(1, instance.Parts[0].Particles.ActiveCount);
        Assert.Equal(0, instance.Parts[1].Particles.ActiveCount);
        runtime.Update(.1);
        Assert.Equal(2, instance.Parts[0].Particles.ActiveCount);
        Assert.Equal(1, instance.Parts[1].Particles.ActiveCount);
        runtime.Update(.1);
        Assert.Equal(2, instance.Parts[0].Particles.ActiveCount);
        Assert.Equal(2, instance.Parts[1].Particles.ActiveCount);
        Assert.False(instance.Parts[0].IsEmitting);
        Assert.True(instance.Parts[1].IsEmitting);
    }

    [Fact]
    public void WorldPauseFreezesWorldClockWhileUiClockAdvances()
    {
        var runtime = new ParticleCompositeRuntime(Catalog([Burst()],
            Part("burst"), Part("burst", space: ParticleCoordinateSpace.Screen)));
        var instance = Play(runtime);
        runtime.Update(.1, worldPaused: true);
        Assert.Equal(0, instance.Parts[0].Particles.GetAt(0).AgeSeconds);
        Assert.Equal(.1, instance.Parts[1].Particles.GetAt(0).AgeSeconds, 10);
        runtime.Update(.1);
        Assert.Equal(.1, instance.Parts[0].Particles.GetAt(0).AgeSeconds, 10);
        Assert.Equal(.2, instance.Parts[1].Particles.GetAt(0).AgeSeconds, 10);
    }

    [Fact]
    public void ReplayWithIdenticalSeedsAndInputYieldsIdenticalParticles()
    {
        var catalog = Catalog([Burst()], Part("burst"));
        var a = new ParticleCompositeRuntime(catalog, seed: 372);
        var b = new ParticleCompositeRuntime(catalog, seed: 372);
        var ia = Play(a, new Vector2D(4, -2));
        var ib = Play(b, new Vector2D(4, -2));
        for (int i = 0; i < 4; i++)
            Assert.Equal(ia.Parts[0].Particles.GetAt(i), ib.Parts[0].Particles.GetAt(i));
        a.Update(.1);
        b.Update(.1);
        Assert.Equal(ia.Parts[0].Particles.GetAt(2), ib.Parts[0].Particles.GetAt(2));
    }

    [Fact]
    public void TwoInstancesUsingSameDefinitionRemainIndependent()
    {
        var runtime = new ParticleCompositeRuntime(Catalog([Burst()], Part("burst")));
        var first = Play(runtime);
        var second = Play(runtime, new Vector2D(100, 0));
        first.Clear();
        Assert.Equal(0, first.Parts[0].Particles.ActiveCount);
        Assert.Equal(4, second.Parts[0].Particles.ActiveCount);
        Assert.NotSame(first.Parts[0].Particles, second.Parts[0].Particles);
        Assert.True(second.Parts[0].Particles.GetAt(0).Position.X > 98);
    }

    [Fact]
    public void InstanceLimitDropsNewEffectAndCountsFailures()
    {
        var runtime = new ParticleCompositeRuntime(Catalog([Continuous()], Part("flow")), maxInstances: 1);
        Play(runtime);
        Assert.False(runtime.TryPlay("fx.test", Vector2D.Zero, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(1L, runtime.DroppedAtCapacity);
        Assert.Single(runtime.Instances);
    }

    [Fact]
    public void StopPreventsDelayedEmissionButExistingParticlesCanAgeOut()
    {
        var runtime = new ParticleCompositeRuntime(Catalog(
            [Burst()], Part("burst"), Part("burst", delay: .3)));
        var instance = Play(runtime);
        instance.Stop();
        for (int i = 0; i < 20; i++) runtime.Update(.1);
        Assert.Equal(0L, instance.Parts[1].Particles.Diagnostics.Spawned);
        Assert.Empty(runtime.Instances);
    }

    [Fact]
    public void ClearDiscardsAllLiveInstancesImmediately()
    {
        var runtime = new ParticleCompositeRuntime(Catalog([Continuous()], Part("flow")));
        var instance = Play(runtime);
        runtime.Update(.1);
        Assert.Equal(1, instance.Parts[0].Particles.ActiveCount);
        runtime.Clear();
        Assert.Empty(runtime.Instances);
        Assert.Equal(0, instance.Parts[0].Particles.ActiveCount);
    }

    [Fact]
    public void RuntimeBoundsTimeAndRejectsInvalidDelta()
    {
        var runtime = new ParticleCompositeRuntime(Catalog([Continuous()], Part("flow")));
        var instance = Play(runtime);
        runtime.Update(100);
        Assert.Equal(1, instance.Parts[0].Particles.ActiveCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Update(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Update(double.NaN));
    }

    [Fact]
    public void AuthoringValidationRejectsMissingEmitterAndDuplicateEffects()
    {
        var broken = new ParticleCompositeDefinition { Id = "fx.invalid", Parts = [Part("missing")] };
        Assert.Throws<ArgumentException>(() => new ParticleCompositeCatalog([Burst()], [broken]));
        var same = new ParticleCompositeDefinition { Id = "fx.same", Parts = [Part("burst")] };
        Assert.Throws<ArgumentException>(() => new ParticleCompositeCatalog([Burst()], [same, same]));
    }

    [Fact]
    public void AuthoringValidationRejectsOutOfRangeTimelineCapacityAndStyles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Part("burst") with { DelaySeconds = -1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Part("burst") with { DurationSeconds = double.NaN }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Part("burst") with { Capacity = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Part("burst") with { OffsetX = double.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentException>(() => (Part("burst") with { Space = ParticleCoordinateSpace.Screen }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Part("burst") with { Blend = (ParticleBlendMode)99 }).Validate());
    }

    [Fact]
    public void AuthoringValidationRejectsMissingOrHugePartLists()
    {
        Assert.Throws<ArgumentException>(() => new ParticleCompositeDefinition { Id = "fx.empty", Parts = [] }.Validate());
        Assert.Throws<ArgumentException>(() => new ParticleCompositeDefinition {
            Id = "fx.toomany", Parts = Enumerable.Repeat(Part("burst"), 9).ToArray() }.Validate());
    }

    [Fact]
    public void ManifestJsonParsesReferencesStylesAndStrictUnknownFields()
    {
        const string json = """
        {"version":1,"emitters":[{"id":"burst","burstCount":4}],
         "effects":[{"id":"fx.test","parts":[{"emitterId":"burst","layer":"Foreground",
         "sprite":"Spark","blend":"Additive","capacity":12}]}]}
        """;
        var catalog = ParticleCompositeCatalog.FromJson(json);
        Assert.Equal("fx.test", Assert.Single(catalog.Ids));
        var style = Assert.Single(catalog.Get("fx.test").Parts!);
        Assert.Equal(ParticleVisualLayer.Foreground, style.Layer);
        Assert.Equal(ParticleBlendMode.Additive, style.Blend);
        Assert.Equal(ParticleSpriteShape.Spark, style.Sprite);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            ParticleCompositeCatalog.FromJson(json.Replace("\"capacity\":12", "\"capacity\":12,\"unknown\":true")));
        Assert.Throws<NotSupportedException>(() =>
            ParticleCompositeCatalog.FromJson(json.Replace("\"version\":1", "\"version\":2")));
    }

    [Fact]
    public void ScreenSpacePartRemainsInPixelCoordinates()
    {
        var runtime = new ParticleCompositeRuntime(Catalog([Continuous()],
            Part("flow", anchor: ParticleEmitterAnchor.Follow,
                space: ParticleCoordinateSpace.Screen) with { OffsetX = 12 }));
        var instance = Play(runtime, new Vector2D(100, 200));
        runtime.Update(.1);
        Assert.Equal(new Vector2D(112, 200), instance.Parts[0].Particles.GetAt(0).Position);
        instance.MoveSource(new Vector2D(250, 100));
        runtime.Update(.1);
        Assert.Equal(new Vector2D(262, 100), instance.Parts[0].Particles.GetAt(1).Position);
    }
}
