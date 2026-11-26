using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Foundation.Presentation;

public sealed class GraphicsQualityPresetTests
{
    [Theory]
    [InlineData(0,LightingQuality.High,100,100,100,false)]
    [InlineData(1,LightingQuality.High,75,100,100,false)]
    [InlineData(2,LightingQuality.Balanced,50,50,75,true)]
    [InlineData(3,LightingQuality.Low,25,50,50,true)]
    public void PresetsSetOnlyQualityFields(int index,LightingQuality quality,int light,int mask,int bloom,bool adaptive)
    {
        var before=new GraphicsPreferences { Width=1920,Height=1080,Fullscreen=true,VSync=false,FpsLimit=144,
            Profile=2,StaticFieldsOverride=true,StaticShadowFields=true,AreaSamples=9 };
        var preset=GraphicsQualityPreset.For(index);
        var after=preset.Apply(before);
        Assert.Equal(before with { Quality=quality,LightScale=light,MaskScale=mask,BloomScale=bloom,Adaptive=adaptive,AreaSamples=0 },after);
        Assert.True(after.IsValid);
        Assert.Equal(preset.Label,GraphicsQualityPreset.CurrentLabel(after.Quality,after.LightScale,after.MaskScale,after.BloomScale,after.Adaptive,after.AreaSamples));
        Assert.Equal("CUSTOM",GraphicsQualityPreset.CurrentLabel(after.Quality,after.LightScale,after.MaskScale,after.BloomScale,false,3));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void LabPresetsPreserveBothEnabledAndDisabledOptimizationFlags(int index)
    {
        foreach(var before in new[]{LightingQualityLabProfile.For(1),LightingQualityLabProfile.For(2).EnableAllOptimizations(),
            LightingQualityLabProfile.For(2) with { OptimizationMask=5, StaticFields=true }})
        {
            var after=GraphicsQualityPreset.For(index).Apply(before);
            for(int bit=1;bit<=16;bit*=2) Assert.Equal(before.Enabled(bit),after.Enabled(bit));
            Assert.Equal(before.StaticFields,after.StaticFields);
            Assert.Equal(before.StaticShadowFields,after.StaticShadowFields);
            Assert.Equal(before.AllRuntimeOptimizations,after.AllRuntimeOptimizations);
            Assert.Equal(0,after.AreaSamples);
        }
    }
    [Fact]
    public void EnableAllAndQualitySelectionCommute()
    {
        var preset=GraphicsQualityPreset.For(2);
        var p=new GraphicsPreferences();
        Assert.Equal(preset.Apply(p.EnableAllOptimizations()),preset.Apply(p).EnableAllOptimizations());
        var lab=LightingQualityLabProfile.For(1);
        var a=preset.Apply(lab.EnableAllOptimizations());
        var b=preset.Apply(lab).EnableAllOptimizations();
        Assert.Equal(a with { Label=b.Label },b);
    }
}
