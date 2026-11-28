using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;
namespace PlatformerSandbox.Tests.Presentation.Lighting;
public sealed class GPU01LocalMaskLayoutTests
{
    [Fact]
    public void WorldFixedMaskTranslatesWithBothCameraAxes()
    {
        Assert.True(GpuLocalMaskLayout.TrySize(10,32,out int size));
        Assert.Equal(658,size);
        var center=new LightPoint(-10,20);
        var before=new LightingViewport(1280,720,new LightPoint(-10,20),32);
        var after=before with {Focus=new LightPoint(-8,23)};
        var a=GpuLocalMaskLayout.Origin(before,center,size);
        var b=GpuLocalMaskLayout.Origin(after,center,size);
        Assert.Equal(a.X-64,b.X);Assert.Equal(a.Y+96,b.Y);
    }
    [Fact]
    public void OversizedAndInvalidMasksAreRejected()
    {
        Assert.False(GpuLocalMaskLayout.TrySize(100,32,out _));
        Assert.False(GpuLocalMaskLayout.TrySize(double.NaN,32,out _));
        Assert.False(GpuLocalMaskLayout.TrySize(1,0,out _));
        Assert.False(GpuLocalMaskLayout.TrySize(-1,-1,out _));
    }
}
