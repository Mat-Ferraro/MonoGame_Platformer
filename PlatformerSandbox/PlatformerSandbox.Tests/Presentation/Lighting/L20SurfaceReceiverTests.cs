using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L20SurfaceReceiverTests
{
    [Fact]
    public void FoldFixesSeamAndCompressesDisplacementWithShear()
    {
        var r = new SurfaceReceiverProjection { Mode = ReceiverProjectionMode.Folded,
            SeamY = 14, Compression = .25, Shear = .5, BandWorld = 1 };
        r.Validate();
        Assert.Equal(new LightPoint(3,14), r.ProjectShadow(new(3,14)));
        Assert.Equal(new LightPoint(5,13), r.ProjectShadow(new(3,10)));
        Assert.Equal(new LightPoint(3,10), r.UnprojectShadow(new(5,13)));
        Assert.True(r.ReceivesProjectedShadow(new(5,13)));
        Assert.False(r.ReceivesProjectedShadow(new(5,12.99)));
        Assert.False(r.ReceivesProjectedShadow(new(5,14.01)));
    }

    [Theory]
    [InlineData(ReceiverProjectionMode.Legacy,true)]
    [InlineData(ReceiverProjectionMode.Flat,true)]
    [InlineData(ReceiverProjectionMode.Clipped,false)]
    public void FlatAndClippedNeverWarp(ReceiverProjectionMode mode, bool receives)
    {
        var r = new SurfaceReceiverProjection { Mode = mode, SeamY = 14 };
        var p = new LightPoint(3,10);
        Assert.Equal(p,r.ProjectShadow(p));
        Assert.Equal(receives,r.ReceivesProjectedShadow(p));
    }

    [Fact]
    public void OrientationSeparatesHorizontalFloorFromVerticalFace()
    {
        var r = new SurfaceReceiverProjection { OrientationStrength = 1,
            PitchDegrees = 90, FacePitchDegrees = 0 };
        Assert.Equal(1,r.Incidence(0,1,0,true),10);
        Assert.Equal(0,r.Incidence(0,1,0,false),10);
        Assert.Equal(0,r.Incidence(0,0,1,true),10);
        Assert.Equal(1,r.Incidence(0,0,1,false),10);
        Assert.Equal(0,r.Incidence(0,-1,0,true),10);
        Assert.Equal(1,(r with { OrientationStrength=0 }).Incidence(0,-1,0,true),10);
    }

    [Fact]
    public void ReceiverSettingsAreBoundedAndCasterMaterialIsRejected()
    {
        var r = new SurfaceReceiverProjection { Mode = ReceiverProjectionMode.Folded };
        Assert.Throws<ArgumentOutOfRangeException>(() => (r with { Compression = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (r with { Shear = double.NaN }).Validate());
        Assert.Throws<NotSupportedException>(() => new LightingSurfaceOptions
            { Receiver = r, MaterialShading = ProductionSurfaceShading.Flat, CastsShadows = true }.Validate());
        Assert.Throws<NotSupportedException>(() => new LightingSurfaceOptions { Receiver = r }.Validate());
        var settings = new LightingSurfaceOptions { Receiver = r, MaterialShading = ProductionSurfaceShading.Flat };
        Assert.Equal(r,ProductionSurfaceSubmission.From("floor",settings).Receiver);
        Assert.False(new LightingSurfaceOptions().Receiver.Active);
    }

    [Theory]
    [InlineData(false,4,true)]
    [InlineData(true,4,false)]
    [InlineData(false,0,false)]
    [InlineData(true,0,false)]
    public void RimVisibilityIsNeverBlurred(bool back, double width, bool expected) =>
        Assert.Equal(expected,ProductionMaterialPassPlanner.FilterVisibility(back,width));

    [Fact]
    public void LabGroundOptsInAtTheActualSeamWithoutChangingCasterGeometry()
    {
        string? dir=AppContext.BaseDirectory;
        string relative=Path.Combine("Content","Levels","LightTypesLab.ldtk");
        for(int i=0;i<14 && dir is not null;i++,dir=Directory.GetParent(dir)?.FullName)
        {
            string path=Path.Combine(dir,relative);
            if(!File.Exists(path)) continue;
            var scene=LdtkLightingImporter.FromJson(File.ReadAllText(path),"LIGHT_TYPES_LAB");
            var ground=Assert.Single(scene.Surfaces.Where(x=>x.Value.Receiver.Active));
            Assert.Equal("551d41b5-a8da-40ab-9c08-991c7437da17",ground.Key);
            Assert.Equal(ReceiverProjectionMode.Clipped,ground.Value.Receiver.Mode);
            Assert.Equal(14,ground.Value.Receiver.SeamY,10);
            Assert.False(ground.Value.CastsShadows);
            Assert.True(ground.Value.ReceivesShadows);
            Assert.All(scene.Lights,l=>Assert.Equal(1,l.ReceiverDirectionalDepth,10));
            Assert.Contains(scene.Occluders,h=>h.Vertices.Length==16);
            return;
        }
        throw new FileNotFoundException(relative);
    }
}
