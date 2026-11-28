using System;
using PlatformerSandbox.Foundation.Performance;
using Xunit;
namespace PlatformerSandbox.Tests.Foundation.Presentation;
public sealed class GPU01ShadowFanTests
{
    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(256)] [InlineData(16384)]
    public void CompactFanExpandsToExactOriginal(int triangles)
    {
        int[] input=new int[triangles*3], compact=new int[triangles+2];
        for(int i=0;i<triangles;i++) { input[3*i]=0;input[3*i+1]=i+1;input[3*i+2]=i+2; }
        Assert.True(ShadowFanIndexPlan.TryCompact<int>(input,compact,triangles));
        var indices=ShadowFanIndexPlan.BuildIndices(triangles);
        for(int i=0;i<input.Length;i++) Assert.Equal(input[i],compact[indices[i]]);
    }
    [Fact]
    public void NonFanAndShortBuffersAreRejected()
    {
        Assert.False(ShadowFanIndexPlan.TryCompact<int>(new[]{0,1,2,8,2,3},new int[4],2));
        Assert.False(ShadowFanIndexPlan.TryCompact<int>(new[]{0,1,7,0,2,3},new int[4],2));
        Assert.False(ShadowFanIndexPlan.TryCompact<int>(new[]{0,1,2},new int[2],1));
        Assert.False(ShadowFanIndexPlan.TryCompact<int>(Array.Empty<int>(),Array.Empty<int>(),0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ShadowFanIndexPlan.BuildIndices(16385));
    }
    [Fact]
    public void SignedZeroIsNotSilentlyMerged()
    {
        float negativeZero=BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        Assert.False(ShadowFanIndexPlan.TryCompact<float>(new[]{0f,1f,2f,negativeZero,2f,3f},new float[4],2));
    }
}
