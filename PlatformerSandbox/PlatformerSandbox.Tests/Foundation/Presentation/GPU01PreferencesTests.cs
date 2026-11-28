using System;
using System.IO;
using PlatformerSandbox.Foundation.Performance;
using Xunit;

namespace PlatformerSandbox.Tests.Foundation.Presentation;

public sealed class GPU01PreferencesTests
{
    [Fact]
    public void CompatibilityIsDefaultAndEstablishedOptimizationsDoNotEnableExperiment()
    {
        var p = new GraphicsPreferences();
        Assert.False(p.GpuShadowBuffers);
        Assert.False(p.GpuShadowResident);
        Assert.False(p.GpuShadowIndexed);
        Assert.False(p.GpuLocalMasks);
        Assert.False(p.GpuSpecializedMaterials);
        Assert.False(p.GpuFusedLayer);
        Assert.False(p.GpuBatchUnshadowed);
        Assert.False(p.GpuFastBloom);
        Assert.False(p.GpuInstancedParticles);
        Assert.False(p.EnableAllOptimizations().GpuShadowBuffers);
        Assert.True((p with { GpuShadowBuffers = true }).EnableAllOptimizations().GpuShadowBuffers);
    }
    [Fact]
    public void SavedGpuPreferenceRoundTripsAndIsIndependentOfGraphicsPreset()
    {
        string dir=Path.Combine(Path.GetTempPath(),"GPU01-"+Guid.NewGuid());
        try
        {
            string path=Path.Combine(dir,"graphics.json");
            var p=new GraphicsPreferences() with { GpuShadowBuffers=true, GpuShadowResident=true, GpuShadowIndexed=true, GpuLocalMasks=true, GpuSpecializedMaterials=true, GpuFusedLayer=true, GpuBatchUnshadowed=true, GpuFastBloom=true, GpuInstancedParticles=true };
            p.Save(path);
            Assert.Equal(p,GraphicsPreferences.Load(path));
            for(int i=0;i<GraphicsQualityPreset.Count;i++)
            {
                var preset=GraphicsQualityPreset.For(i).Apply(p);
                Assert.True(preset.GpuShadowBuffers);
                Assert.True(preset.GpuShadowResident);
                Assert.True(preset.GpuShadowIndexed);
                Assert.True(preset.GpuLocalMasks);
                Assert.True(preset.GpuSpecializedMaterials);
                Assert.True(preset.GpuFusedLayer);
                Assert.True(preset.GpuBatchUnshadowed);
                Assert.True(preset.GpuFastBloom);
                Assert.True(preset.GpuInstancedParticles);
            }
            File.WriteAllText(path,"{}");
            Assert.False(GraphicsPreferences.Load(path)!.GpuShadowBuffers);
        }
        finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
}
