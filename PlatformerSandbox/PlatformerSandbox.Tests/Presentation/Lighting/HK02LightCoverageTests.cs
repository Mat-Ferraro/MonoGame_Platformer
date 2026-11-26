using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class HK02LightCoverageTests
{
    private const string Showcase = "AREA_RECT|SHOWCASE|EMISSIVE";
    private static LdtkLightingScene ReadManualScene()
    {
        string? dir=AppContext.BaseDirectory;
        for(int i=0;i<14 && dir is not null;i++,dir=Directory.GetParent(dir)?.FullName)
        {
            string path=Path.Combine(dir,"Content","Levels","LightingQualityLab.ldtk");
            if(File.Exists(path)) return LdtkLightingImporter.FromJson(File.ReadAllText(path),GameLevelIds.LightingPerformanceLab);
        }
        throw new FileNotFoundException("LightingQualityLab.ldtk");
    }

    [Fact]
    public void ReproducesPinkLightDropoutAndPreservesItAtBothReportedCameraPositions()
    {
        var scene=ReadManualScene();
        var presenter=new GameplayLightingPresenter { Quality=LightingQuality.High };
        presenter.EnterWorld(1,scene);
        var pink=Assert.Single(scene.Surfaces.Where(s=>s.Value.EmissiveLightRadius>0 &&
            s.Value.EmissionColor.R>.9 && s.Value.EmissionColor.G<.3 && s.Value.EmissionColor.B>.3 &&
            scene.SurfaceCenters[s.Key].X>40 && scene.SurfaceCenters[s.Key].X<80));
        string id="emissive:"+pink.Key;
        var left=new LightingViewport(1280,720,new LightPoint(71.51,15.04),32);
        var right=left with { Focus=new LightPoint(78.17,15.04) };
        var diagnostics=new List<LightSelectionDiagnostic>();
        Assert.Contains(presenter.BuildVisible(left,filterTag:Showcase),l=>l.Id==id && l.CastShadows);
        Assert.DoesNotContain(presenter.BuildVisible(right,filterTag:Showcase,diagnostics:diagnostics),l=>l.Id==id);
        Assert.Equal(LightSelectionState.ShadowBudget,Assert.Single(diagnostics.Where(d=>d.Light.Id==id)).State);
        var budget=LightingFrameBudget.ForManualCoverage(LightingQuality.High);
        foreach(var view in new[]{left,right,left})
        {
            var selected=presenter.BuildVisible(view,filterTag:Showcase,budgetOverride:budget,diagnostics:diagnostics);
            Assert.Contains(selected,l=>l.Id==id && l.CastShadows);
            Assert.Equal(LightSelectionState.ActiveShadowed,Assert.Single(diagnostics.Where(d=>d.Light.Id==id)).State);
            Assert.DoesNotContain(diagnostics,d=>d.State is LightSelectionState.LightBudget or LightSelectionState.ShadowBudget or LightSelectionState.ActiveShadowLimited);
        }
    }

    [Theory]
    [InlineData(1280,720)]
    [InlineData(1920,1080)]
    public void WideLabSweepKeepsEveryEligibleWorldLightAtSupportedTestResolutions(int width,int height)
    {
        var presenter=new GameplayLightingPresenter { Quality=LightingQuality.High };
        presenter.EnterWorld(1,ReadManualScene());
        var budget=LightingFrameBudget.ForManualCoverage(LightingQuality.High);
        var diagnostics=new List<LightSelectionDiagnostic>();
        // Sample multiple animation phases, and several vertical camera positions.
        for(int phase=0;phase<4;phase++)
        {
            for(int tick=0;tick<20;tick++) presenter.Update(.1,false);
            double halfW=width/64.0,halfH=height/64.0;
            foreach(double y in new[]{halfH,20,40-halfH})
                for(double x=halfW;x<=120-halfW;x+=.5)
                {
                    var view=new LightingViewport(width,height,new LightPoint(x,y),32);
                    var selected=presenter.BuildVisible(view,filterTag:Showcase,budgetOverride:budget,diagnostics:diagnostics);
                    Assert.All(diagnostics.Where(d=>d.IsInView),d=>Assert.True(d.IsActive,$"{d.Light.Id} {d.State} at {x},{y}"));
                    Assert.All(selected,l=>Assert.True(l.CastShadows));
                    Assert.True(selected.Count<=ProductionMaterialLightPlan.MaximumLights);
                }
        }
    }

    [Theory]
    [InlineData(LightingQuality.Low)]
    [InlineData(LightingQuality.Balanced)]
    [InlineData(LightingQuality.High)]
    public void CoverageRetainsSamplingAndParticleQualityAndNeverExceedsMaterialLimit(LightingQuality quality)
    {
        var normal=LightingFrameBudget.For(quality);
        var coverage=LightingFrameBudget.ForManualCoverage(quality);
        Assert.Equal(normal.ShadowRadialSamples,coverage.ShadowRadialSamples);
        Assert.Equal(normal.MaxParticleLights,coverage.MaxParticleLights);
        Assert.Equal(ProductionMaterialLightPlan.MaximumLights,coverage.MaxLights);
        Assert.Equal(coverage.MaxLights,coverage.MaxShadowLights);
    }

    [Fact]
    public void DiagnosticsDifferentiateWorkloadViewportLightAndShadowBudgets()
    {
        RadialLight Light(string id,string owner,int priority,double x=0,bool shadow=true)=>new()
        { Id=id,Owner=owner,Tag="SHOWCASE",Priority=priority,Position=new(x,0),Radius=3,CastShadows=shadow };
        var diagnostics=new List<LightSelectionDiagnostic>();
        var view=new LightingViewport(1280,720,new(0,0),32);
        var lights=new[]{Light("level","level",10),Light("pink","emissive",5),Light("plain","level",4,shadow:false),Light("overflow","level",1,shadow:false),Light("far","level",100,100)};
        var selected=LightingFramePlanner.Select(lights,view,new LightingFrameBudget(3,1,192,0),false,diagnostics);
        Assert.Equal(2,selected.Count);
        var states=diagnostics.ToDictionary(d=>d.Light.Id,d=>d.State);
        Assert.Equal(LightSelectionState.ActiveShadowed,states["level"]);
        Assert.Equal(LightSelectionState.ShadowBudget,states["pink"]);
        Assert.Equal(LightSelectionState.ActiveUnshadowed,states["plain"]);
        Assert.Equal(LightSelectionState.LightBudget,states["overflow"]);
        Assert.Equal(LightSelectionState.OutsideView,states["far"]);
        var presenter=new GameplayLightingPresenter(); presenter.EnterWorld(1,ReadManualScene());
        presenter.BuildVisible(view,filterTag:"AREA_POINT",diagnostics:diagnostics);
        Assert.Contains(diagnostics,d=>d.Light.Tag=="AREA_RECT" && d.State==LightSelectionState.WorkloadFiltered);
    }
}
