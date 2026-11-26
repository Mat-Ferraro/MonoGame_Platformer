using System;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class PO04BPreparationTests
{
    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(5)] [InlineData(7)] [InlineData(9)]
    public void SampleCountAndEnergyAreExactForEveryFootprint(int count)
    {
        foreach (var footprint in Enum.GetValues<AreaEmitterFootprint>())
        {
            var source = new RadialLight { Id="source", Shape=LightShape.Tube, Intensity=2.4,
                Position=new(3,-2), Direction=new(1,1), EmitterFootprint=footprint };
            var samples=AreaEmitterSampling.Samples(source,count);
            Assert.Equal(count,samples.Count);
            Assert.Equal(source.Intensity,samples.Sum(s=>s.Intensity),10);
            Assert.Equal(source.Position.X,samples.Average(s=>s.Position.X),10);
            Assert.Equal(source.Position.Y,samples.Average(s=>s.Position.Y),10);
            Assert.All(samples,s=> { Assert.Equal(source.Id,s.Id); Assert.Equal(LightShape.Point,s.Shape); });
        }
    }
    [Fact]
    public void PreparedCacheReusesOnlyMatchingSourceValuesAndCount()
    {
        var cache=new PreparedAreaLightCache();
        var light=new RadialLight { Id="tube", Shape=LightShape.Tube };
        var samples=cache.Get(light,7);
        Assert.Same(samples,cache.Get(light with {},7));
        Assert.NotSame(samples,cache.Get(light with { Position=new(1,0) },7));
        Assert.NotSame(samples,cache.Get(light with { Intensity=.5 },7));
        Assert.NotSame(samples,cache.Get(light,9));
        for(int i=0;i<300;i++) cache.Get(light with { Position=new(i,0) },7);
        Assert.InRange(cache.Count,1,256);
        cache.Clear(); Assert.Equal(0,cache.Count);
    }
    [Fact]
    public void PreparedAndReferenceSamplerMatchAtEverySampleCount()
    {
        var cache=new PreparedAreaLightCache();
        var light=new RadialLight {Id="tube",Shape=LightShape.Tube,Radius=8,CastShadows=true};
        var lights=new[]{light};
        var art=new LightingArtPreset {Id="test"};
        var wall=new LightingOccluder {Id="wall",Vertices=new LightPoint[]{new(1,-1),new(2,-1),new(2,1),new(1,1)}};
        foreach(int count in new[]{1,3,5,7,9})
        {
            var prepared=LightingWorldSampler.Prepare(lights,art,count,cache);
            for(int x=-5;x<=5;x++)
            {
                var point=new LightPoint(x,0);
                var expected=LightingWorldSampler.Sample(point,LightChannel.Hero,true,null,art,lights,new[]{wall},sampleCount:count);
                Assert.Equal(expected,LightingWorldSampler.Sample(point,LightChannel.Hero,true,null,art,lights,new[]{wall},prepared:prepared,sampleCount:count));
            }
        }
    }
    [Fact]
    public void FingerprintChangesOnMutationAndIgnoresLightColor()
    {
        var view=new LightingViewport(1280,720,new(0,0),32);
        var light=new RadialLight {Id="light",Radius=10};
        var wall=new LightingOccluder {Id="wall",Vertices=new LightPoint[]{new(1,-1),new(2,-1),new(2,1),new(1,1)}};
        var receiver=new SurfaceReceiverProjection();
        int Hash(RadialLight l)=>SharedVisibilityKey.Fingerprint(view,l,receiver,128,false,0,false,0,new[]{wall});
        var key=new SharedVisibilityKey(view,light,receiver,128,false,0,false,0,new[]{wall});
        Assert.Equal(Hash(light),Hash(light with {Color=new LightRgb(1,0,0)}));
        wall.Vertices[0]=new(.5,-1);
        // Never assert hash inequality: collisions are legal, exact matching is authoritative.
        Assert.False(key.Matches(view,light,receiver,128,false,0,false,0,new[]{wall}));
    }
}
