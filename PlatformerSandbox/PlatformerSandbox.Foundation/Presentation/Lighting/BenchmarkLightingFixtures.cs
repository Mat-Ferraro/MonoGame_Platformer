using System;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Developer-only deterministic emitters, not gameplay content.
/// The viewport argument describes a fixed reference layout, NOT the current camera.</summary>
public static class BenchmarkLightingFixtures
{
    public static IReadOnlyList<RadialLight> Create(string fixture, LightingViewport anchor)
    {
        anchor.Validate();
        if (fixture is not ("adaptive" or "static" or "static_shadow")) throw new ArgumentException(nameof(fixture));
        double[] sizes = { 8, 20, 48, 96, 160 };
        var lights = new RadialLight[5];
        for (int i=0; i<lights.Length; i++)
        {
            var l = new RadialLight { Id = "po_fixture_"+i, Owner="benchmark", Shape = fixture == "adaptive" ? LightShape.Tube : LightShape.Point,
                Position = new LightPoint(anchor.Focus.X + (i-2)*anchor.Width/(6.0*anchor.PixelsPerUnit), anchor.Focus.Y),
                Radius = (fixture=="static_shadow"?320:100)/anchor.PixelsPerUnit, Intensity = .8, Direction = new LightPoint(1,0),
                TubeLength = Math.Clamp(sizes[i]/anchor.PixelsPerUnit,.1,40), CastShadows = fixture=="static_shadow", Channels = LightChannel.All };
            l.Validate(); lights[i] = l;
        }
        return Array.AsReadOnly(lights);
    }
}

/// <summary>Scene-owned fixture layout. All lights are fixed in world space from
/// the level's initial player position and a canonical 720p design view. Camera
/// movement, viewport resizing, profile changes and fixture switching cannot
/// reposition them. Only scene activation creates a new layout.</summary>
public sealed class BenchmarkLightingFixtureSet
{
    public const string CoordinateVersion = "world_fixed_v1";
    public const int ReferenceWidth = 1280, ReferenceHeight = 720;
    private readonly IReadOnlyList<RadialLight> _adaptive, _static, _staticShadow;
    public LightingViewport Anchor { get; }

    public BenchmarkLightingFixtureSet(LightPoint initialPlayerCenter,
        double levelWidth, double levelHeight, double pixelsPerUnit)
    {
        var view = Camera2DView.FromFocus(new Vector2D(initialPlayerCenter.X, initialPlayerCenter.Y),
            levelWidth, levelHeight, ReferenceWidth, ReferenceHeight, pixelsPerUnit);
        Anchor = new LightingViewport(ReferenceWidth, ReferenceHeight,
            new LightPoint(view.Left + view.VisibleWidth * .5, view.Bottom + view.VisibleHeight * .5), pixelsPerUnit);
        _adaptive = BenchmarkLightingFixtures.Create("adaptive", Anchor);
        _static = BenchmarkLightingFixtures.Create("static", Anchor);
        _staticShadow = BenchmarkLightingFixtures.Create("static_shadow", Anchor);
    }

    public IReadOnlyList<RadialLight> Get(string fixture) => fixture switch
    {
        "adaptive" => _adaptive,
        "static" => _static,
        "static_shadow" => _staticShadow,
        _ => throw new ArgumentException("Unknown benchmark lighting fixture.", nameof(fixture))
    };
}
