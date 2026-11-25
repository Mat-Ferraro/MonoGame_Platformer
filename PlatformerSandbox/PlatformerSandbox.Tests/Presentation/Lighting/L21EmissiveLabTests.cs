using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L21EmissiveLabTests
{
    private static string Read(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 14 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void DedicatedLabSelectsExactlyTwoWorldLightsAndNoAuthoredLights()
    {
        var scene = LdtkLightingImporter.FromJson(Read(Path.Combine("Content", "Levels", "EmissiveLightingLab.ldtk")), GameLevelIds.EmissiveLightingLab);
        Assert.Empty(scene.Lights);
        Assert.Equal(2, scene.Occluders.Count);
        var walls = scene.Occluders.OrderBy(w => w.Vertices.Min(p => p.X)).ToArray();
        Assert.Equal(0.0, walls[0].LitFaceDepthZ);
        Assert.Equal(1.0, walls[1].LitFaceDepthZ);
        Assert.Equal(2, scene.Surfaces.Values.Count(s => s.EmissiveLightRadius > 0));
        Assert.Single(scene.Surfaces.Values.Where(s => s.EmissionIntensity > 0 && s.EmissiveLightRadius == 0));
        var presenter = new GameplayLightingPresenter { Quality = LightingQuality.High };
        presenter.EnterWorld(1, scene);
        var lights = presenter.BuildVisible(new LightingViewport(1280, 720, new LightPoint(20, 15), 32), filterTag: "EMISSIVE");
        Assert.Equal(2, lights.Count);
        Assert.All(lights, light => { Assert.True(light.CastShadows); Assert.Equal("emissive", light.Owner); });
        var cyan = Assert.Single(lights.Where(l => l.Color.R < .1));
        var magenta = Assert.Single(lights.Where(l => l.Color.R > .9));
        Assert.All(lights, l => Assert.Equal(0.0, l.VirtualZ));
        Assert.True(ReceiverWallShadowPolicy.StartsAtExit(cyan, walls[0]));
        Assert.False(ReceiverWallShadowPolicy.StartsAtExit(magenta, walls[1]));
        var art = new LightingArtPreset { Id = "test", Ambient = new LightRgb(.14,.14,.14) };
        LightPoint P(double x, double y) => new(x/32, (1280-y)/32);
        LightRgb Sample(double x, double y, RadialLight[] sources) => LightingWorldSampler.Sample(P(x,y), LightChannel.Hero, true, null, art, sources, scene.Occluders, scene.Layers.Gameplay);
        var left = Sample(340,740,lights.ToArray());
        var right = Sample(940,740,lights.ToArray());
        Assert.True(left.B > left.R && left.G > left.R);
        Assert.True(right.R > right.G && right.B > right.G);
        var upper = Sample(550,755,new[]{cyan});
        var lower = Sample(550,825,new[]{cyan});
        Assert.True(upper.B > lower.B + .3);
        // Both walls are in the same world-color preset. Sample their interior
        // with both emitters present, rather than changing the light depths.
        var litFace = Sample(442,810,lights.ToArray());
        var darkFace = Sample(832,810,lights.ToArray());
        Assert.True(litFace.B > art.Ambient.B + .3);
        Assert.Equal(art.Ambient.R, darkFace.R, 6);
        Assert.Equal(art.Ambient.B, darkFace.B, 6);
        var overlap = Sample(640,740,new[]{cyan,magenta});
        Assert.True(overlap.R > .2 && overlap.G > .2 && overlap.B > .3);
    }

    [Fact]
    public void Phase20LabContainsNoPhase21WorldEmitters()
    {
        var scene = LdtkLightingImporter.FromJson(Read(Path.Combine("Content", "Levels", "LightTypesLab.ldtk")), GameLevelIds.LightTypesLab);
        Assert.Equal(5,scene.Lights.Count);
        Assert.DoesNotContain(scene.Surfaces.Values,s => s.EmissiveLightRadius > 0);
    }
}
