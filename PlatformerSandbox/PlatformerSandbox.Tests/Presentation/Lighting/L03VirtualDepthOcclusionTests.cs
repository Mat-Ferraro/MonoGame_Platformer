using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L03VirtualDepthOcclusionTests
{
    private static LightingOccluder Box() => new()
    {
        Id="crate", LayerId="PLAYER",
        Vertices = new[]
        {
            new LightPoint(-1,-1),new LightPoint(1,-1),
            new LightPoint(1,1),new LightPoint(-1,1)
        }
    };

    [Fact] public void InBetweenFrontCrateBlocksLightOnBackWall()
    {
        Assert.True(VirtualDepthOcclusion.Blocks(
            new LightingDepthPoint(new LightPoint(0,0),2),
            new LightingDepthPoint(new LightPoint(0,0),-2),Box(),0));
    }

    [Fact] public void ReceiverOnFrontFaceDoesNotSelfShadow()
    {
        Assert.False(VirtualDepthOcclusion.Blocks(
            new LightingDepthPoint(new LightPoint(0,0),2),
            new LightingDepthPoint(new LightPoint(0,0),0),Box(),0));
    }

    [Fact] public void CasterNotBetweenLightAndReceiverDoesNotBlock()
    {
        Assert.False(VirtualDepthOcclusion.Blocks(
            new LightingDepthPoint(new LightPoint(0,0),-2),
            new LightingDepthPoint(new LightPoint(0,0),-3),Box(),0));
    }

    [Fact] public void MissedPolygonDoesNotCastShadow()
    {
        Assert.False(VirtualDepthOcclusion.Blocks(
            new LightingDepthPoint(new LightPoint(5,4),2),
            new LightingDepthPoint(new LightPoint(5,4),-2),Box(),0));
    }

    [Fact] public void CoPlanarOcclusionMatchesExistingGeometry()
    {
        Assert.True(VirtualDepthOcclusion.Blocks(
            new LightingDepthPoint(new LightPoint(-2,0),0),
            new LightingDepthPoint(new LightPoint(2,0),0),Box(),0));
        Assert.False(VirtualDepthOcclusion.Blocks(
            new LightingDepthPoint(new LightPoint(-2,0),0),
            new LightingDepthPoint(new LightPoint(2,0),0),Box(),1));
    }

    [Fact] public void InvalidDepthIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>VirtualDepthOcclusion.Blocks(
            new LightingDepthPoint(new LightPoint(0,0),double.NaN),
            new LightingDepthPoint(new LightPoint(1,0),0),Box(),0));
    }
}
