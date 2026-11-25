using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L21ReceiverWallOverlapTests
{
    private static SceneLightingLayer Layer => SceneLightingLayerSet.Standard11().Gameplay;
    private static LightingOccluder Wall(bool reverse = false) => new()
    {
        Id = "wall", LayerId = Layer.Id, LitFaceLayer = Layer,
        Vertices = reverse ? [new(0,2),new(2,2),new(2,0),new(0,0)] :
            [new(0,0),new(2,0),new(2,2),new(0,2)]
    };
    private static RadialLight Lamp(double depth = 0) => new()
    {
        Id = "lamp", Position = new(-3,1), Radius = 12, VirtualZ = depth,
        CastShadows = true, Color = new(0,1,1)
    };
    private static bool Inside(LightPoint p, IReadOnlyList<LightPoint> polygon)
    {
        bool inside = false;
        for (int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
        {
            var a=polygon[i];var b=polygon[j];
            if ((a.Y>p.Y)!=(b.Y>p.Y) && p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X)
                inside=!inside;
        }
        return inside;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LitWallStopsAtExitAndDarkWallStopsAtEntry(bool reverse)
    {
        var wall=Wall(reverse);
        foreach (double depth in new[]{0.0,-1.0})
        {
            var light=Lamp(depth);
            var mask=ShadowVisibility.BuildPolygon(light,[wall],192,respectLitFaces:true);
            foreach (var sample in new[]{(new LightPoint(-1,1),true),
                (new LightPoint(1,1),depth==0),(new LightPoint(3,1),false)})
            {
                Assert.Equal(sample.Item2, ShadowVisibility.CanIlluminate(light,sample.Item1,[wall],true));
                Assert.Equal(sample.Item2, Inside(sample.Item1,mask));
            }
        }
    }
    [Fact]
    public void OtherBlockersAndRimMasksRemainStrict()
    {
        var light=Lamp(); var wall=Wall();
        var invisible=wall with { Id="other", LitFaceLayer=null,
            Vertices=wall.Vertices.Select(p=>new LightPoint(p.X-4,p.Y)).ToArray() };
        // Place the second blocker between the source and the lit face.
        invisible=invisible with { Vertices=invisible.Vertices.Select(p=>new LightPoint(p.X+1.5,p.Y)).ToArray() };
        Assert.True(ShadowVisibility.CanIlluminate(light,new(1,1),[wall],true));
        Assert.False(ShadowVisibility.CanIlluminate(light,new(1,1),[wall,invisible],true));
        Assert.False(ShadowVisibility.CanIlluminate(light,new(1,1),[wall],false));
        Assert.False(ShadowVisibility.CanIlluminate(light,new(1,1),[wall with { LitFaceLayer=null }],true));
        Assert.False(ReceiverWallShadowPolicy.StartsAtExit(light with { AffectsLayers=["BG1"] },wall));
    }
    [Fact]
    public void ReverseLightAndMovedRotatedWallFollowCurrentExitEdge()
    {
        var wall=Wall(); var reverse=Lamp() with { Position=new(5,1) };
        Assert.True(ShadowVisibility.CanIlluminate(reverse,new(1,1),[wall],true));
        Assert.False(ShadowVisibility.CanIlluminate(reverse,new(-1,1),[wall],true));
        var rotated=wall with { Vertices=wall.Vertices.Select(p=>new LightPoint(4-p.Y,p.X+2)).ToArray() };
        var light=Lamp() with { Position=new(3,-1) };
        Assert.True(ShadowVisibility.CanIlluminate(light,new(3,3),[rotated],true));
        Assert.False(ShadowVisibility.CanIlluminate(light,new(3,5),[rotated],true));
    }
    [Fact]
    public void ConcaveWallUsesFirstExitNotFarthestExit()
    {
        var wall=Wall() with { Vertices=[new(0,0),new(4,0),new(4,4),new(3,4),new(3,1),new(1,1),new(1,4),new(0,4)] };
        var light=Lamp() with { Position=new(-3,2) };
        Assert.True(ShadowVisibility.CanIlluminate(light,new(.5,2),[wall],true));
        Assert.False(ShadowVisibility.CanIlluminate(light,new(2,2),[wall],true));
        Assert.False(ShadowVisibility.CanIlluminate(light,new(3.5,2),[wall],true));
    }
    [Fact]
    public void DirectionalProjectionAndCpuSamplingRespectFaceDepth()
    {
        var wall=Wall(); var light=Lamp() with { Shape=LightShape.Directional, Direction=new(1,0), DirectionalShadowLength=3 };
        var exit=DirectionalShadowProjection.Triangles(wall,light.Direction,3,true);
        Assert.Equal(2,exit.Min(p=>p.X),6); Assert.Equal(5,exit.Max(p=>p.X),6);
        var strict=DirectionalShadowProjection.Triangles(wall,light.Direction,3);
        Assert.Equal(0,strict.Min(p=>p.X),6);
        var art=new LightingArtPreset { Id="overlap", Ambient=new(.1,.1,.1) };
        LightRgb At(double x, RadialLight l)=>LightingWorldSampler.Sample(new(x,1),LightChannel.Hero,true,null,art,[l],[wall],Layer);
        Assert.True(At(1,light).B>.5);
        Assert.True(At(3,light).B<.2);
        Assert.True(At(1,light with {VirtualZ=-1}).B<.2);
        Assert.True(At(6,light).B>.5);
    }
    [Fact]
    public void DedicatedFixtureImportsLitFaceAndSupportsBothDepthModes()
    {
        string? dir=AppContext.BaseDirectory; string? file=null;
        for(int i=0;i<14 && dir is not null;i++,dir=Directory.GetParent(dir)?.FullName)
        {
            string candidate=Path.Combine(dir,"Content","Levels","EmissiveLightingLab.ldtk");
            if(File.Exists(candidate)){file=candidate;break;}
        }
        var scene=LdtkLightingImporter.FromJson(File.ReadAllText(file ?? throw new FileNotFoundException("Emissive lab")),GameLevelIds.EmissiveLightingLab);
        Assert.Equal(2, scene.Occluders.Count);
        var walls = scene.Occluders.OrderBy(w => w.Vertices.Min(p => p.X)).ToArray();
        var left = walls[0]; var right = walls[1];
        Assert.All(walls, w => Assert.NotNull(w.LitFaceLayer));
        Assert.Equal(0.0, left.LitFaceDepthZ);
        Assert.Equal(1.0, right.LitFaceDepthZ);
        RadialLight Emitter(bool cyan)
        {
            var id = Assert.Single(scene.Surfaces.Where(s => s.Value.EmissiveLightRadius > 0 &&
                (s.Value.EmissionColor.R < .1) == cyan)).Key;
            return EmissiveWorldLighting.Create(id, scene.SurfaceCenters[id], scene.Surfaces[id])!;
        }
        var light = Emitter(true); var magenta = Emitter(false);
        Assert.Equal(0.0, light.VirtualZ);
        Assert.Equal(0.0, magenta.VirtualZ);
        var leftMaterial = ProductionSurfaceSubmission.From(left.Owner, scene.Surfaces[left.Owner]);
        var rightMaterial = ProductionSurfaceSubmission.From(right.Owner, scene.Surfaces[right.Owner]);
        Assert.False(ProductionWallLightRouting.Behind(leftMaterial, light, left));
        Assert.True(ProductionWallLightRouting.Behind(rightMaterial, magenta, right));
        Assert.Equal(0.0, rightMaterial.BackRimIntensity);
        LightPoint P(double x,double y)=>new(x/32,(1280-y)/32);
        Assert.True(ShadowVisibility.CanIlluminate(light,P(442,810),walls,true));
        Assert.False(ShadowVisibility.CanIlluminate(light,P(442,810),walls,false));
        Assert.False(ShadowVisibility.CanIlluminate(light with {VirtualZ=-1},P(442,810),walls,true));
        Assert.False(ShadowVisibility.CanIlluminate(light,P(470,825),walls,true));
        Assert.True(ShadowVisibility.CanIlluminate(magenta,P(860,800),walls,true));
        Assert.False(ShadowVisibility.CanIlluminate(magenta,P(832,810),walls,true));
        Assert.False(ShadowVisibility.CanIlluminate(magenta,P(800,825),walls,true));
        // At the same XY position, raising only light depth restores the lit-face rule.
        Assert.True(ShadowVisibility.CanIlluminate(magenta with {VirtualZ=1},P(832,810),walls,true));
    }
}
