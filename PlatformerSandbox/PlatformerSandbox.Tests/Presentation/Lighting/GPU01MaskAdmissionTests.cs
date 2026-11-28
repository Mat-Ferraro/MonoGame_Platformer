using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;
namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class GPU01MaskAdmissionTests
{
    [Fact]
    public void RequiresConsecutiveFramesNotMultipleDrawsInOneFrame()
    {
        var policy=new GpuMaskAdmission(4,3);
        policy.BeginFrame();
        for(int i=0;i<20;i++) Assert.False(policy.Observe(1));
        policy.BeginFrame(); Assert.False(policy.Observe(1));
        policy.BeginFrame(); Assert.True(policy.Observe(1));
    }
    [Fact]
    public void ChangingGeometryAndGapsResetStreak()
    {
        var policy=new GpuMaskAdmission(4,3);
        policy.BeginFrame(); Assert.False(policy.Observe(1));
        policy.BeginFrame(); Assert.False(policy.Observe(2));
        policy.BeginFrame(); Assert.False(policy.Observe(1));
        policy.BeginFrame(); Assert.False(policy.Observe(1));
        policy.BeginFrame(); Assert.True(policy.Observe(1));
        policy.Clear(); Assert.False(policy.Observe(1));
    }
    [Fact]
    public void AdmissionHistoryIsBounded()
    {
        var policy=new GpuMaskAdmission(1,2);
        policy.BeginFrame(); Assert.False(policy.Observe(1)); Assert.False(policy.Observe(2));
        policy.BeginFrame(); Assert.True(policy.Observe(1)); Assert.False(policy.Observe(2));
    }
}
