using PlatformerSandbox.Foundation.Performance;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class PO10FLiveFrameReadoutTests
{
    [Fact] public void SeparatesDrawRateAndSimulationRate()
    {
        var r=new LiveFrameReadout();
        for(int i=0;i<12;i++) r.Add(80,5,2,8,50);
        Assert.False(r.Ready);
        r.Add(80,5,2,8,50);
        Assert.True(r.Ready);
        Assert.Equal(12.5,r.FramesPerSecond,8);
        Assert.Equal(62.5,r.TicksPerSecond,8);
        Assert.Equal(80,r.MeanMilliseconds,8);
        Assert.Equal(20,r.OtherMilliseconds,8);
    }
    [Fact] public void ResetAndNextWindowDoNotRetainOldSamples()
    {
        var r=new LiveFrameReadout();
        r.Add(1000,60,0,0,0);
        Assert.Equal(1,r.FramesPerSecond);
        r.Reset(); Assert.False(r.Ready);
        for(int i=0;i<50;i++) r.Add(20,1,1,2,3);
        Assert.True(r.Ready);
        Assert.Equal(50,r.FramesPerSecond,8);
        Assert.Equal(50,r.TicksPerSecond,8);
        Assert.Equal(14,r.OtherMilliseconds,8);
    }
}
