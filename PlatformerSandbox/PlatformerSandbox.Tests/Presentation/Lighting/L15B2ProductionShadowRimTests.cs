using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L15B2ProductionShadowRimTests
{
    private static LightingOccluder Rect(string id, double left, double bottom,
        double right, double top, string layer = "PLAYER") => new()
    {
        Id = id, LayerId = layer,
        Vertices = [new(left,bottom),new(right,bottom),new(right,top),new(left,top)]
    };
    private static RadialLight Light(double x, double y, double z = 0,
        bool shadows = true) => new()
    {
        Id = "source", Position = new(x,y), VirtualZ = z,
        Radius = 20, Intensity = 1, Owner = "test", CastShadows = shadows
    };
    private static ProductionSurfaceSubmission Wall(bool overlap = false) => new()
    {
        InstanceId="wall", LayerId="PLAYER", Channel=LightChannel.Terrain,
        CasterId="wall", CastsShadows=true, ReceivesShadows=true,
        Shading=ProductionSurfaceShading.Hybrid, BackRimOnOverlap=overlap
    };

    [Fact]
    public void LowerVirtualDepthSuppressesWallFaceButEnablesBackRim()
    {
        var wall = Rect("wall", -1,-1,1,1);
        Assert.True(ProductionWallLightRouting.Behind(Wall(), Light(2,0,z:-1), wall));
        Assert.False(ProductionWallLightRouting.Behind(Wall(), Light(2,0,z:0), wall));
        Assert.False(ProductionWallLightRouting.Behind(Wall(), Light(2,0,z:1), wall));
        Assert.False(ProductionWallLightRouting.Behind(new ProductionSurfaceSubmission
        {
            InstanceId="panel", LayerId="PLAYER", Shading=ProductionSurfaceShading.Normal,
            ReceivesShadows=true
        }, Light(0,0,z:-1), null));
    }

    [Fact]
    public void SameDepthOriginOverlapNeedsExplicitBehindRule()
    {
        var wall = Rect("wall", -1,-1,1,1);
        Assert.False(ProductionWallLightRouting.Behind(Wall(), Light(0,0), wall));
        Assert.True(ProductionWallLightRouting.Behind(Wall(overlap:true), Light(0,0), wall));
        Assert.False(ProductionWallLightRouting.Behind(Wall(overlap:true), Light(3,0), wall));
    }

    [Fact]
    public void OwnCasterIsExcludedByExactIdNotOverlappingCoordinates()
    {
        var own=Rect("wall", 0,0,1,1);
        var identical=Rect("other",0,0,1,1);
        var distant=Rect("far",2,0,3,1);
        var surface=Wall();
        var selected=ProductionWallLightRouting.OtherBlockers(surface, Light(-2,.5),
            [own, identical, distant]);
        Assert.Equal(new[] { "other", "far" }, selected.Select(x => x.Id));
        // An identical different wall DOES cast a shadow on the receiver.
        Assert.False(ShadowVisibility.CanIlluminate(Light(-2,.5),new(.5,.5),selected));
        Assert.True(ShadowVisibility.CanIlluminate(Light(-2,.5),new(.5,.5),
            ProductionWallLightRouting.OtherBlockers(surface, Light(-2,.5), [own])));
    }

    [Fact]
    public void OtherCastersBlockRimAndHonorPerLightLayerRouting()
    {
        var own=Rect("wall",4,-1,6,1);
        var cover=Rect("cover",1,-1,2,1);
        var source=Light(0,0,-1);
        var blockers=ProductionWallLightRouting.OtherBlockers(Wall(),source,[own,cover]);
        Assert.False(ShadowVisibility.CanIlluminate(source,new(4.2,0),blockers));
        Assert.True(ShadowVisibility.CanIlluminate(source,new(4.2,0),
            ProductionWallLightRouting.OtherBlockers(Wall(),source,[own])));
        var filtered=source with { OccluderLayers = ["FG1"] };
        Assert.Empty(ProductionWallLightRouting.OtherBlockers(Wall(),filtered,[own,cover]));
    }

    [Fact]
    public void RealFixtureHasOneShadowReceivingNormalAndOneCastingRimWall()
    {
        string? folder=AppContext.BaseDirectory;
        string? fixture=null;
        for(int i=0;i<12 && folder is not null;i++)
        {
            string path=Path.Combine(folder,"Content","Levels","LightingIntegrationTest.ldtk");
            if(File.Exists(path)){fixture=path;break;}
            folder=Directory.GetParent(folder)?.FullName;
        }
        Assert.NotNull(fixture);
        var scene=LdtkLightingImporter.FromJson(File.ReadAllText(fixture!),
            "LIGHTING_INTEGRATION_TEST");
        var opted=scene.Surfaces.Where(x=>x.Value.MaterialShading!=ProductionSurfaceShading.Legacy).ToArray();
        Assert.Equal(2,opted.Length);
        Assert.All(opted,x=>Assert.True(x.Value.ReceivesShadows));
        var wall=opted.Single(x=>x.Value.CastsShadows);
        var own=ProductionWallLightRouting.OwnWall(ProductionSurfaceSubmission.From(wall.Key,wall.Value),scene.Occluders);
        Assert.NotNull(own);
        Assert.Equal(4,own!.Vertices.Length);
        var green=scene.Lights.Single(x=>x.Id.StartsWith("796b1f00",StringComparison.Ordinal));
        Assert.Equal(-1d,green.VirtualZ);
        Assert.True(green.CastShadows);
        Assert.True(ProductionWallLightRouting.Behind(ProductionSurfaceSubmission.From(wall.Key,wall.Value),green,own));
    }

    [Fact]
    public void RimFixtureHullIsInsetToMatchVisibleArtwork()
    {
        // The authored pillar PNG has a small transparent outer margin.
        // Without this inset, its shadow fan stops before the visible artwork,
        // while the 4.5px interior rim falls entirely outside visible alpha.
        string? folder = AppContext.BaseDirectory;
        string? fixture = null;
        for (int i = 0; i < 12 && folder is not null; i++)
        {
            string path = Path.Combine(folder, "Content", "Levels", "LightingIntegrationTest.ldtk");
            if (File.Exists(path)) { fixture = path; break; }
            folder = Directory.GetParent(folder)?.FullName;
        }
        Assert.NotNull(fixture);
        var scene = LdtkLightingImporter.FromJson(File.ReadAllText(fixture!),
            "LIGHTING_INTEGRATION_TEST");
        var wall = scene.Occluders.Single(o =>
            o.Id == "85a99230-df9a-4c4b-a23e-02499548608b");
        // LDtk coordinates are screen pixels (Y down); importer uses 32px/world unit.
        double left = (2350.0 + 6.0) / 32.0;
        double right = (2350.0 + 112.0 - 6.0) / 32.0;
        Assert.Equal(left, wall.Vertices.Min(v => v.X), 8);
        Assert.Equal(right, wall.Vertices.Max(v => v.X), 8);
        var green = scene.Lights.Single(l =>
            l.Id == "796b1f00-8d03-41fa-ab92-0fb1d8b68e63");
        Assert.True(ShadowVisibility.CanIlluminate(green,
            new LightPoint(left, (1280 - 690) / 32.0),
            ProductionWallLightRouting.OtherBlockers(
                ProductionSurfaceSubmission.From(wall.Id, scene.Surfaces[wall.Id]),
                green, scene.Occluders)));
    }

    [Fact]
    public void InvalidOverlapModeOnNonCasterCannotBeAuthorised()
    {
        Assert.Throws<NotSupportedException>(()=>new LightingSurfaceOptions
        {
            LayerId="PLAYER", MaterialShading=ProductionSurfaceShading.Hybrid,
            BackRimOnOverlap=true, CastsShadows=false
        }.Validate());
        Assert.Throws<ArgumentException>(()=>new ProductionSurfaceSubmission
        {
            InstanceId="wall", LayerId="PLAYER", CastsShadows=true,
            Shading=ProductionSurfaceShading.Hybrid
        }.Validate());
    }
}
