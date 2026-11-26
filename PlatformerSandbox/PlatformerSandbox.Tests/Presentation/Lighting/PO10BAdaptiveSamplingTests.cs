using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class PO10BAdaptiveSamplingTests
{
    [Theory]
    [InlineData(1,0,9,1)] [InlineData(20,0,9,3)] [InlineData(50,0,9,5)]
    [InlineData(100,0,9,7)] [InlineData(200,0,9,9)] [InlineData(200,0,5,5)]
    [InlineData(13,1,9,1)] [InlineData(14,1,9,3)] [InlineData(11,3,9,3)] [InlineData(10,3,9,1)]
    public void SelectsBoundedSampleCountWithHysteresis(double pixels,int previous,int max,int expected) =>
        Assert.Equal(expected,AdaptiveAreaSamplePolicy.Select(pixels,previous,max));

    [Fact]
    public void PreservesAuthoredOverridesAndTotalEnergy()
    {
        var policy=new AdaptiveAreaSamplePolicy();
        var automatic=new RadialLight { Id="tube", Owner="test", Shape=LightShape.Tube,TubeLength=2,Intensity=3 };
        var authored=automatic with { Id="exact", AreaSampleCount=9 };
        var point=automatic with { Id="point", Shape=LightShape.Point };
        var selected=policy.Apply(new[]{automatic,authored,point},20,9);
        Assert.Equal(5,selected[0].AreaSampleCount);
        Assert.Same(authored,selected[1]); Assert.Same(point,selected[2]);
        Assert.Equal(3,AreaEmitterSampling.Samples(selected[0],selected[0].AreaSampleCount).Sum(l=>l.Intensity),10);
        Assert.Equal(0,automatic.AreaSampleCount);
        policy.Clear();
    }
}
