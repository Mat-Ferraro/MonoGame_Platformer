using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Presentation.UI;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Foundation.Presentation;

public sealed class PO10EUiAndGraphicsTests
{
    [Theory]
    [InlineData(1280,720)]
    [InlineData(1920,1080)]
    [InlineData(800,400)]
    public void PanelPagesExposeEveryControlAndHitTheDrawnRow(int width,int height)
    {
        const int total=43;
        var first=ControlPanelLayout.Create(width,height,total,0,true);
        int count=0;
        for(int page=0;page<first.Pages;page++)
        {
            var l=ControlPanelLayout.Create(width,height,total,page,true);
            Assert.Equal(count,l.First);
            Assert.True(l.X+l.Width<=width && l.Y+l.Height<=height);
            for(int i=0;i<l.Count;i++)
            {
                int y=l.Y+ControlPanelLayout.Header+i*ControlPanelLayout.Row;
                Assert.Equal(l.First+i,l.HitRow(l.X+9,y));
                Assert.Equal(-1,l.HitRow(l.X+9,y+ControlPanelLayout.Row-1));
            }
            count+=l.Count;
        }
        Assert.Equal(total,count);
    }
    [Fact]
    public void CollapsedPanelCannotActivateInvisibleRows()
    {
        var l=ControlPanelLayout.Create(1280,720,40,0,false);
        Assert.Equal(0,l.Count);
        Assert.Equal(-1,l.HitRow(l.X+9,l.Y+ControlPanelLayout.Header));
    }
    [Fact]
    public void GraphicsPreferencesRoundTripAndRejectInvalidData()
    {
        string dir=Path.Combine(Path.GetTempPath(),"PO10E-"+Guid.NewGuid());
        string path=Path.Combine(dir,"graphics.json");
        try
        {
            Assert.Null(GraphicsPreferences.Load(path));
            var p=new GraphicsPreferences().WithProfile(4) with { Width=1920,Height=1080,MaskScale=75,FpsLimit=60,VSync=false };
            Assert.True(p.IsValid);
            p.Save(path);
            Assert.Equal(p,GraphicsPreferences.Load(path));
            File.WriteAllText(path,"not valid json");
            Assert.Null(GraphicsPreferences.Load(path));
            Assert.False((p with { MaskScale=123 }).IsValid);
            Assert.False((p with { AreaSamples=3,Adaptive=true }).IsValid);
        }
        finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
    [Fact]
    public void IndependentMaskAndLightScaleAndFlagsRemainIndependent()
    {
        var p=LightingQualityLabProfile.For(4) with { LightScaleOverride=25,MaskScaleOverride=75,OptimizationMask=5 };
        Assert.Equal(25,p.LightScaleOverride); Assert.Equal(75,p.MaskScaleOverride);
        Assert.True(p.Enabled(1)); Assert.False(p.Enabled(2)); Assert.True(p.Enabled(4));
        Assert.False(p.Enabled(8)); Assert.False(p.Enabled(16));
        Assert.Equal(50,p.Scale);
    }
    [Theory]
    [InlineData(MajorSceneKind.Title)]
    [InlineData(MajorSceneKind.Gameplay)]
    public void GraphicsMenuReturnsToSettingsAndItsOriginalCaller(MajorSceneKind scene)
    {
        var nav=new GameUiNavigation();
        if(scene==MajorSceneKind.Gameplay) nav.Push(GameUiScreen.Pause,scene);
        nav.Push(GameUiScreen.Settings,scene);
        nav.Push(GameUiScreen.Graphics,scene);
        Assert.Equal(GameUiScreen.Graphics,nav.CurrentScreen(scene));
        Assert.True(nav.Back()); Assert.Equal(GameUiScreen.Settings,nav.CurrentScreen(scene));
        Assert.True(nav.Back()); Assert.Equal(scene==MajorSceneKind.Title?GameUiScreen.Title:GameUiScreen.Pause,nav.CurrentScreen(scene));
    }
}
