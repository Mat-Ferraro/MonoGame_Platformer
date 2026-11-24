using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L20UniversalOcclusionTests
{
    private static string Read(string path)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i=0;i<14 && dir is not null;i++)
        {
            string file=Path.Combine(dir,path);
            if (File.Exists(file)) return File.ReadAllText(file);
            dir=Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException(path);
    }

    private static LightingOccluder Hull(string id, params LightPoint[] v) =>
        new() { Id=id, LayerId="PLAYER", Vertices=v };

    private static bool InTriangle(LightPoint p, LightPoint a, LightPoint b, LightPoint c)
    {
        double Cross(LightPoint x, LightPoint y, LightPoint z) =>
            (y.X-x.X)*(z.Y-x.Y)-(y.Y-x.Y)*(z.X-x.X);
        if (Math.Abs(Cross(a,b,c))<1e-10) return false;
        double x=Cross(a,b,p), y=Cross(b,c,p), z=Cross(c,a,p);
        return (x>=-1e-10 && y>=-1e-10 && z>=-1e-10) ||
            (x<=1e-10 && y<=1e-10 && z<=1e-10);
    }

    [Fact]
    public void ConcaveDirectionalSweepPreservesUnoccludedNotchAndWinding()
    {
        var hull=Hull("U",new(0,0),new(4,0),new(4,4),new(3,4),new(3,1),new(1,1),new(1,4),new(0,4));
        foreach (var h in new[] { hull, hull with { Vertices=hull.Vertices.Reverse().ToArray() } })
        {
            var t=DirectionalShadowProjection.Triangles(h,new LightPoint(0,-1),.5);
            bool Covered(LightPoint p)=>Enumerable.Range(0,t.Count/3).Any(i=>InTriangle(p,t[3*i],t[3*i+1],t[3*i+2]));
            Assert.False(Covered(new(2,3)));
            Assert.True(Covered(new(.5,3)));
            Assert.True(Covered(new(2,-.25)));
            Assert.False(Covered(new(2,-.75)));
        }
    }

    [Fact]
    public void DirectionalCullingKeepsOffscreenCasterWhoseShadowReachesView()
    {
        var hull=Hull("left",new(-4,0),new(-3,0),new(-3,1),new(-4,1));
        var view=new LightingViewport(100,100,new LightPoint(0,0),100);
        Assert.True(DirectionalShadowProjection.IntersectsViewport(hull,new(1,0),4,view));
        Assert.False(DirectionalShadowProjection.IntersectsViewport(hull,new(-1,0),4,view));
    }

    [Fact]
    public void StaticTriangulationCachesButChangedVertexDataCannotReuseStaleTriangles()
    {
        var hull=Hull("cache",new(0,0),new(1,0),new(1,1),new(0,1));
        var first=ShadowGeometry.Triangulate(hull);
        Assert.Same(first,ShadowGeometry.Triangulate(hull));
        hull.Vertices[1]=new LightPoint(2,0);
        Assert.NotSame(first,ShadowGeometry.Triangulate(hull));
    }

    [Fact]
    public void CustomContoursValidateAndPreserveSeparateComponents()
    {
        var rings=ShadowContourAuthoring.Parse("0,0;.4,0;.4,1;0,1|.6,0;1,0;1,1;.6,1");
        Assert.Equal(2,rings.Count);
        Assert.Throws<ArgumentException>(()=>ShadowContourAuthoring.Parse("0,0;1,1;1,0;0,1"));
        Assert.Throws<ArgumentException>(()=>ShadowContourAuthoring.Parse("0,0;1,0;.5,0;1,1;0,1"));
        Assert.Throws<ArgumentException>(()=>ShadowContourAuthoring.Parse("0,0;1,0;1,1;0,1|.2,.2;.4,.2;.3,.4"));
        Assert.Throws<ArgumentException>(()=>ShadowContourAuthoring.Parse("0,0;1,0;1,1;0,1|0,0;.1,.1;0,.2"));
        Assert.Throws<ArgumentException>(()=>ShadowContourAuthoring.Parse("0,0;NaN,0;1,1"));
    }

    [Fact]
    public void CompoundSelfExclusionIsOwnerBasedNotGeometryBased()
    {
        var a=Hull("wall",new(0,0),new(1,0),new(1,1),new(0,1)) with { OwnerId="wall" };
        var b=Hull("wall:contour:1",new(2,0),new(3,0),new(3,1),new(2,1)) with { OwnerId="wall" };
        var unrelated=a with { Id="other", OwnerId=null };
        var surface=new ProductionSurfaceSubmission { InstanceId="wall", CasterId="wall", LayerId="PLAYER", CastsShadows=true };
        var light=new RadialLight { Id="light", CastShadows=true, VirtualZ=-1 };
        Assert.Equal(2,ProductionWallLightRouting.OwnWalls(surface,[a,b,unrelated]).Count);
        Assert.Equal("other",Assert.Single(ProductionWallLightRouting.OtherBlockers(surface,light,[a,b,unrelated])).Id);
        Assert.Throws<InvalidOperationException>(()=>ProductionWallLightRouting.OwnWalls(surface,[a,a]));
        Assert.Throws<InvalidOperationException>(()=>ProductionWallLightRouting.OwnWalls(surface,[a with { LayerId="BG1" }]));
    }

    [Fact]
    public void EveryLabCastingGpuSurfaceResolvesItsCompleteHull()
    {
        var scene=LdtkLightingImporter.FromJson(Read(Path.Combine("Content","Levels","LightTypesLab.ldtk")),"LIGHT_TYPES_LAB");
        int compound=0;
        foreach (var kv in scene.Surfaces.Where(x=>x.Value.CastsShadows && x.Value.MaterialShading!=ProductionSurfaceShading.Legacy))
        {
            var surface=ProductionSurfaceSubmission.From(kv.Key,kv.Value);
            var walls=ProductionWallLightRouting.OwnWalls(surface,scene.Occluders);
            Assert.NotEmpty(walls);
            if(walls.Count>1) compound++;
            Assert.All(walls, w=>Assert.NotEmpty(ShadowGeometry.Triangulate(w)));
        }
        Assert.True(compound>0);
        Assert.Contains(scene.Occluders,x=>x.Vertices.Length==16);
        Assert.Contains(scene.Occluders,x=>x.Vertices.Length==5);
        Assert.NotEmpty(scene.OccluderMotions);
        var presenter=new GameplayLightingPresenter(); presenter.EnterWorld(1,scene);
        presenter.Update(.5,false); var frame=presenter.CurrentScene()!;
        foreach(var hull in scene.Occluders.Where(h=>scene.OccluderMotions.ContainsKey(h.Owner)))
            Assert.False(hull.Vertices.SequenceEqual(frame.Occluders.Single(h=>h.Id==hull.Id).Vertices));
        presenter.Update(.5,true); Assert.Same(frame,presenter.CurrentScene());
    }

    [Fact]
    public void DirectionalOverlapDoesNotDependOnArbitrarySourceMarker()
    {
        var wall=Hull("wall",new(0,0),new(1,0),new(1,1),new(0,1));
        var surface=new ProductionSurfaceSubmission { InstanceId="wall",CasterId="wall",CastsShadows=true, BackRimOnOverlap=true };
        var sun=new RadialLight { Id="sun", Shape=LightShape.Directional, Position=new(.5,.5) };
        Assert.False(ProductionWallLightRouting.Behind(surface,sun,wall));
        Assert.True(ProductionWallLightRouting.Behind(surface,sun with { VirtualZ=-1 },wall));
    }

    [Fact]
    public void LabNeutralPresetDoesNotAlterOriginalGlobalPreset()
    {
        var catalog=LightingGameCatalog.FromJson(Read(Path.Combine("Content","Lighting","L456_GameplayLighting.json")));
        Assert.Equal("LightTypesNeutral",catalog.GetDefaultPresetForScene("LIGHT_TYPES_LAB"));
        Assert.Equal("Illustrated",catalog.GetDefaultPresetForScene("MATERIAL_SHOWCASE_TEST"));
        var neutral=catalog.GetPreset("LightTypesNeutral");
        Assert.Equal(1,neutral.LightMultiplier);
        Assert.Equal(1,neutral.LightExposure);
        Assert.False(neutral.Bloom);
    }
}
