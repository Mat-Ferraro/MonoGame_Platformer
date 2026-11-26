using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class PO10ARenderWorkTests
{
    [Theory]
    [InlineData(100,1280,720)] [InlineData(75,960,540)]
    [InlineData(50,640,360)] [InlineData(25,320,180)]
    public void DimensionsMatchPercent(int percent,int width,int height)
    {
        Assert.Equal(width,LightingRenderWorkPolicy.ScaleDimension(1280,percent));
        Assert.Equal(height,LightingRenderWorkPolicy.ScaleDimension(720,percent));
        Assert.Equal(1,LightingRenderWorkPolicy.ScaleDimension(1,percent));
    }

    [Fact]
    public void MaskCapacityRespectsByteBudgetAndTargetCount()
    {
        const long budget=128L*1024*1024;
        Assert.Equal(36,LightingRenderWorkPolicy.MaskCapacity(1280,720,budget,48));
        Assert.Equal(16,LightingRenderWorkPolicy.MaskCapacity(1920,1080,budget,96));
        Assert.Equal(96,LightingRenderWorkPolicy.MaskCapacity(640,360,budget,96));
        Assert.Equal(48,LightingRenderWorkPolicy.MaskCapacity(640,360,budget,48));
        Assert.Equal(0,LightingRenderWorkPolicy.MaskCapacity(1920,1080,10,96));
    }

    [Fact]
    public void ConservativeRegionClipsAndPadsWithoutLosingPixels()
    {
        Assert.Equal(new ProductionMaterialRegion(0,2,12,10),
            LightingRenderWorkPolicy.MapRegion(-5,9,24,11,1280,720,640,360,2));
        Assert.True(LightingRenderWorkPolicy.MapRegion(2000,0,20,20,1280,720,640,360,2).IsEmpty);
        Assert.Equal(ProductionMaterialRegion.Full(640,360),
            LightingRenderWorkPolicy.MapRegion(0,0,1280,720,1280,720,640,360,4));
        Assert.True(LightingRenderWorkPolicy.MapRegion(int.MaxValue,0,int.MaxValue,20,1280,720,640,360).IsEmpty);
    }

    [Fact]
    public void MatchingFieldsRequireSameRoutingAndShadowReception()
    {
        var a=new SceneLightingLayer {Id="BG1",Group=RenderLayerGroup.Background};
        var b=a with {Id="BG2",Order=1};
        var lights=new[]{new RadialLight {Id="shared",Channels=LightChannel.Backdrop}};
        Assert.True(LightingRenderWorkPolicy.SameRouting(lights,a,b));
        Assert.False(LightingRenderWorkPolicy.SameRouting(lights,a,b with {ReceivesShadows=false}));
        Assert.False(LightingRenderWorkPolicy.SameRouting(lights,a,b with {Group=RenderLayerGroup.Foreground}));
        var routed=new[]{lights[0] with {AffectsLayers=new[]{"BG1"}}};
        Assert.False(LightingRenderWorkPolicy.SameRouting(routed,a,b));
    }
}
