using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Lighting;
using PlatformerSandbox.Presentation;

namespace PlatformerSandbox;

public sealed partial class Game1
{
    private readonly InteractiveControlPanel _labControls = new(), _graphicsControls = new();
    private readonly string _graphicsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlatformerSandbox","Settings","graphics.json");
    private GraphicsPreferences? _graphicsDraft;
    private string _graphicsNotice = "EDIT THEN APPLY AND SAVE";
    private bool _labNormalMaps = true;
    private bool _labPreserveSceneLights = true;
    private readonly List<LightSelectionDiagnostic> _labLightSelection = new();
    private string? _labInspectedLightId;
    private bool ManualCoverageAvailable => IsQualityLab &&
        (AutomatedLaunch ? PoFixture : _qualityLabFixture) == "normal" && !_lightingStress;
    private bool UseManualLightCoverage => ManualCoverageAvailable && _labPreserveSceneLights;
    private LightingFrameBudget EffectiveFrameLightBudget => UseManualLightCoverage
        ? LightingFrameBudget.ForManualCoverage(_lightingQuality) : LightingFrameBudget.For(_lightingQuality);

    private int _labAmbient = 100, _labCap;
    private int _manualWarmup=60, _manualRecorded=120;
    private static readonly int[] WarmupOptions={30,60,90,120,240};
    private static readonly int[] RecordedOptions={60,120,180,300,600};
    private bool _labInputWasCaptured;
    private long _labUiRefresh;
    private Action? _restoreLabState;
    private void SaveLabState()
    {
        if(AutomatedLaunch) return;
        EnginePerformanceOptions.ManualGpuShadowResident = _processOverridesGraphics && EnginePerformanceOptions.GpuShadowResident;
        EnginePerformanceOptions.ManualGpuShadowIndexed = _processOverridesGraphics && EnginePerformanceOptions.GpuShadowIndexed;
        EnginePerformanceOptions.ManualGpuShadowBuffers = _processOverridesGraphics && EnginePerformanceOptions.GpuShadowBuffers;
        EnginePerformanceOptions.ManualGpuLocalMasks = _processOverridesGraphics && EnginePerformanceOptions.GpuLocalMasks;
        EnginePerformanceOptions.ManualGpuSpecializedMaterials = _processOverridesGraphics && EnginePerformanceOptions.GpuSpecializedMaterials;
        EnginePerformanceOptions.ManualGpuFusedLayer = _processOverridesGraphics && EnginePerformanceOptions.GpuFusedLayer;
        EnginePerformanceOptions.ManualGpuBatchUnshadowed = _processOverridesGraphics && EnginePerformanceOptions.GpuBatchUnshadowed;
        EnginePerformanceOptions.ManualGpuFastBloom = _processOverridesGraphics && EnginePerformanceOptions.GpuFastBloom;
        EnginePerformanceOptions.ManualGpuInstancedParticles = _processOverridesGraphics && EnginePerformanceOptions.GpuInstancedParticles;
        EnginePerformanceOptions.ManualGpuMaterialClear = _processOverridesGraphics && EnginePerformanceOptions.GpuMaterialClear;
        EnginePerformanceOptions.ManualGpuScopedBinds = _processOverridesGraphics && EnginePerformanceOptions.GpuScopedBinds;
        var lighting=(_lightingEnabled,_lightingQuality,_lightingPresetId,_materialLightMode,_lightingStress,_lightingInspectionLight,_l19dWorldLightsEnabled,_perfOverlay);
        var material=(_l19bMaterialPreset,_l19ShowcaseFocus,_l19Hdr,_l19Emission,_l19Bloom,_l19Exposure,_l19BloomThreshold,_l19BloomStrength);
        var shadow=(_shadowEdgePreset,_shadowFilterMode,_shadowQualityPolicy,_l20BloomScatter,_l20BloomOnly,_l20LightFilter,_l20ReceiverNormals,_l20PolygonRims);
        var debug=(_ldtkShowCollisionDebug,_ldtkDrawCollisionBackdrop,_reducedLightingFlashes);
        var emitterOverrides=new System.Collections.Generic.Dictionary<string,float>(_shadowEmitterOverrides,StringComparer.Ordinal);
        _restoreLabState=() =>
        {
            (_lightingEnabled,_lightingQuality,_lightingPresetId,_materialLightMode,_lightingStress,_lightingInspectionLight,_l19dWorldLightsEnabled,_perfOverlay)=lighting;
            (_l19bMaterialPreset,_l19ShowcaseFocus,_l19Hdr,_l19Emission,_l19Bloom,_l19Exposure,_l19BloomThreshold,_l19BloomStrength)=material;
            (_shadowEdgePreset,_shadowFilterMode,_shadowQualityPolicy,_l20BloomScatter,_l20BloomOnly,_l20LightFilter,_l20ReceiverNormals,_l20PolygonRims)=shadow;
            (_ldtkShowCollisionDebug,_ldtkDrawCollisionBackdrop,_reducedLightingFlashes)=debug;
            _shadowEmitterOverrides.Clear(); foreach(var kv in emitterOverrides) _shadowEmitterOverrides.Add(kv.Key,kv.Value);
        };
    }
    private void RestoreLabState() { _restoreLabState?.Invoke(); _restoreLabState=null; _labUiRefresh=0; }
    private bool HasBenchmarkEnvironment
    {
        get
        {
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                if (entry.Key is string key && key.StartsWith("PS_PO_",StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
    private bool _processOverridesGraphics;
    private bool AutomatedLaunch => !string.IsNullOrWhiteSpace(_poAutoOutput) || !string.IsNullOrWhiteSpace(_poVisualOutput);
    private bool InteractiveLab => (IsQualityLab || IsPerformanceLab) && !AutomatedLaunch;
    private bool EffectiveVSync => EnginePerformanceOptions.SavedGraphics?.VSync ?? EnginePerformanceOptions.InitialVSync;
    private int EffectiveRenderCap => InteractiveLab ? _labCap : EnginePerformanceOptions.RenderCap;
    private static string On(bool v) => v?"ON":"OFF";
    private static string Samples(int count,bool adaptive) => count==0?(adaptive?"ADAPTIVE":"AUTOMATIC"):count.ToString();
    private LightingQualityLabProfile LabProfile => EnginePerformanceOptions.ManualLightingLab ?? LightingQualityLabProfile.For(1);
    private static readonly string[] ScaleLabels={"100%","75%","50%","25%"};
    private static readonly int[] ScaleValues={100,75,50,25};
    private static readonly string[] SampleLabels={"AUTOMATIC","ADAPTIVE","1","3","5","7","9"};
    private static readonly string[] CapLabels={"UNCAPPED","30","60","120","144"};
    private static readonly int[] Caps={0,30,60,120,144};
    private static readonly string[] GraphicsPresetLabels=Enumerable.Range(0,GraphicsQualityPreset.Count)
        .Select(i=>GraphicsQualityPreset.For(i).Label).ToArray();
    private string CurrentLabGraphicsPreset => GraphicsQualityPreset.CurrentLabel(_lightingQuality,
        EnginePerformanceOptions.Po10LightScale,EnginePerformanceOptions.Po10MaskScale,
        EnginePerformanceOptions.Po10bBloomScale,EnginePerformanceOptions.Po10bAdaptiveSamples,EnginePerformanceOptions.AreaSamples);
    private void ApplyLabGraphicsPreset(int index)
    {
        var preset=GraphicsQualityPreset.For(index);
        SetPerfQuality(preset.Quality);
        ChangeLabProfile(preset.Apply(LabProfile));
    }
    private void StartManualPerformanceProfile()
    {
        // Preserve explicit process profiles for manual A/B launches; ordinary lab defaults are independent of saved settings.
        if(!_processOverridesGraphics) { SetQualityLabProfile(1); return; }
        int bits=(EnginePerformanceOptions.Po10MaterialRegions?1:0)|(EnginePerformanceOptions.Po10LightRegions?2:0)|
            (EnginePerformanceOptions.Po10SharedFields?4:0)|(EnginePerformanceOptions.Po10SkipClears?8:0)|(EnginePerformanceOptions.Po10CacheCapacity?16:0);
        _labCap=EnginePerformanceOptions.RenderCap;
        ChangeLabProfile(LightingQualityLabProfile.For(1) with { Number=0,Label="PROCESS OVERRIDES",LightScaleOverride=EnginePerformanceOptions.Po10LightScale,
            MaskScaleOverride=EnginePerformanceOptions.Po10MaskScale,BloomScale=EnginePerformanceOptions.Po10bBloomScale,OptimizationMask=bits,
            Adaptive=EnginePerformanceOptions.Po10bAdaptiveSamples,StaticFields=EnginePerformanceOptions.Po10bStaticFields,StaticShadowFields=EnginePerformanceOptions.Po10fStaticShadowFields,AreaSamples=EnginePerformanceOptions.AreaSamples });
    }
    private void ChangeLabProfile(LightingQualityLabProfile profile)
    {
        CancelPerfCapture();
        EnginePerformanceOptions.ManualLightingLab=profile;
        _adaptiveAreaSamples.Clear();
        _gameLighting?.ResetManualLightingProfile();
        ResetPerfWindow();
    }
    private void CustomLab(LightingQualityLabProfile profile) => ChangeLabProfile(profile with { Number=0, Label="CUSTOM" });
    private void LabChange(Action action)
    {
        CancelPerfCapture(); action(); _adaptiveAreaSamples.Clear(); _gameLighting?.InvalidateSharedVisibility(); ResetPerfWindow();
    }
    private void ResetLabControls()
    {
        LabChange(() =>
        {
            ApplyPhase21LabPreset(5);
            _perfMask=IsQualityLab?127:0;
            _perfFreeze=!IsQualityLab;
            _qualityLabFixture="normal"; _qualityLabPan=false; _qualityLabSweepAxis=3; _phase21HullOverlay=false;
            _labNormalMaps=true; _labAmbient=100; _labCap=0; _manualWarmup=60; _manualRecorded=120;
            _labPreserveSceneLights=true; _gpuStressCount=0; _gpuStressMotion=false; _gpuLabTiming=false; _labLightSelection.Clear(); _labInspectedLightId=null;
            _perfUncappedRequested=!EffectiveVSync;
            _perfOverlay=true;
        });
        EnginePerformanceOptions.ManualGpuShadowBuffers=false; EnginePerformanceOptions.ManualGpuShadowResident=EnginePerformanceOptions.ManualGpuShadowIndexed=false; EnginePerformanceOptions.ManualGpuLocalMasks=false; EnginePerformanceOptions.ManualGpuSpecializedMaterials=false; EnginePerformanceOptions.ManualGpuFusedLayer=false; EnginePerformanceOptions.ManualGpuBatchUnshadowed=false; EnginePerformanceOptions.ManualGpuFastBloom=false; EnginePerformanceOptions.ManualGpuInstancedParticles=false; EnginePerformanceOptions.ManualGpuMaterialClear=false; EnginePerformanceOptions.ManualGpuScopedBinds=false;
        ChangeLabProfile(LightingQualityLabProfile.For(IsQualityLab?2:1));
    }
    private void EnableAllLabOptimizations()
    {
        SetAllPerfOptimizations(true);
        ChangeLabProfile(LabProfile.EnableAllOptimizations());
    }
    private static string LabBuildMode
    {
        get
        {
#if DEBUG
            return "DEBUG";
#else
            return "RELEASE";
#endif
        }
    }
    private string LiveLabStatus => _liveFrameReadout.Ready
        ? $"DRAW {_liveFrameReadout.FramesPerSecond:F1} FPS / SIM {_liveFrameReadout.TicksPerSecond:F1} TPS / {_liveFrameReadout.MeanMilliseconds:F2} MS"
        : "DRAW / SIM RATES - SAMPLING 1 SECOND";
    private string LabTiming(double value) => _liveFrameReadout.Ready ? $"{value:F2} MS" : "SAMPLING";
    private bool _labGpuPage;
    private void ApplyRecommendedGpu()
    {
        EnginePerformanceOptions.ManualGpuShadowBuffers=false;
        EnginePerformanceOptions.ManualGpuShadowResident=EnginePerformanceOptions.ManualGpuShadowIndexed=false;
        EnginePerformanceOptions.ManualGpuLocalMasks=false;
        EnginePerformanceOptions.ManualGpuSpecializedMaterials=true;
        EnginePerformanceOptions.ManualGpuFusedLayer=true;
        EnginePerformanceOptions.ManualGpuBatchUnshadowed=true;
        EnginePerformanceOptions.ManualGpuFastBloom=false;
        EnginePerformanceOptions.ManualGpuInstancedParticles=false; EnginePerformanceOptions.ManualGpuMaterialClear=false; EnginePerformanceOptions.ManualGpuScopedBinds=false;
        _gpuStressMotion=false;
    }
    private void BuildLabControls()
    {
        var ui=_labControls; ui.Clear();
        if(IsSpatialLab) { BuildSpatialControls(ui); return; }
        ui.CompactAction=PerfCaptureActive?CancelPerfCapture:null;
        if(_ldtkLevel?.World.LevelId==GameLevelIds.EngineIntegrationLab)
        {
            ui.Add(_pbIntegrationPage?"BACK TO LIGHTING":"PHYSICS BODY CONTROLS",_pbIntegrationPage?"BACK":"OPEN",()=>{_pbIntegrationPage=!_pbIntegrationPage;ui.GoToFirstPage();});
            if(_pbIntegrationPage) {BuildBodyFoundationControls(ui);return;}
        }
        if (_labGpuPage)
        {
            ui.Add("BACK TO LIGHTING CONTROLS","BACK",()=> { _labGpuPage=false; ui.GoToFirstPage(); });
            ui.Add("GPU RECOMMENDED (SHADER CANDIDATE)","APPLY",()=>LabChange(ApplyRecommendedGpu));
        ui.Add("GPU ADAPTER",GraphicsDevice.Adapter.Description,null);
        ui.Add("NATIVE GL RENDERER",_gpuTimer.Adapter,null);
        ui.Add("GPU BACKEND",GpuBackendName,null);
        ui.Add("GRAPHICS PROFILE",GraphicsDevice.GraphicsProfile.ToString(),null);
        ui.Choice("GPU SHADOW SUBMISSION",EnginePerformanceOptions.GpuShadowBuffers?"BUFFERED REQUESTED":"COMPATIBILITY",
            new[]{"COMPATIBILITY","BUFFERED (EXPERIMENTAL)"},i=>LabChange(()=>EnginePerformanceOptions.ManualGpuShadowBuffers=i==1));
        ui.Add("ENABLE ALL GPU EXPERIMENTS", "ENABLE", () => LabChange(()=> { EnginePerformanceOptions.ManualGpuShadowBuffers=true; EnginePerformanceOptions.ManualGpuShadowResident=true; EnginePerformanceOptions.ManualGpuShadowIndexed=true; EnginePerformanceOptions.ManualGpuLocalMasks=true; EnginePerformanceOptions.ManualGpuSpecializedMaterials=true; EnginePerformanceOptions.ManualGpuFusedLayer=true; EnginePerformanceOptions.ManualGpuBatchUnshadowed=true; EnginePerformanceOptions.ManualGpuInstancedParticles=true; EnginePerformanceOptions.ManualGpuMaterialClear=true; EnginePerformanceOptions.ManualGpuScopedBinds=true; }));
        ui.Add("GPU COMPATIBILITY MODE", "DISABLE GPU EXPERIMENTS", () => LabChange(()=> { _gpuStressMotion=false; EnginePerformanceOptions.ManualGpuShadowBuffers=false; EnginePerformanceOptions.ManualGpuShadowResident=EnginePerformanceOptions.ManualGpuShadowIndexed=false; EnginePerformanceOptions.ManualGpuLocalMasks=false; EnginePerformanceOptions.ManualGpuSpecializedMaterials=false; EnginePerformanceOptions.ManualGpuFusedLayer=false; EnginePerformanceOptions.ManualGpuBatchUnshadowed=false; EnginePerformanceOptions.ManualGpuFastBloom=false; EnginePerformanceOptions.ManualGpuInstancedParticles=false; EnginePerformanceOptions.ManualGpuMaterialClear=false; EnginePerformanceOptions.ManualGpuScopedBinds=false;  }));
        ui.Add("GPU RESIDENT SHADOW VERTICES",On(EnginePerformanceOptions.GpuShadowResident), EnginePerformanceOptions.GpuShadowBuffers?()=>LabChange(()=>EnginePerformanceOptions.ManualGpuShadowResident=!EnginePerformanceOptions.GpuShadowResident):null);
        ui.Add("GPU INDEXED SHADOW FANS",On(EnginePerformanceOptions.GpuShadowIndexed), EnginePerformanceOptions.GpuShadowBuffers?()=>LabChange(()=>EnginePerformanceOptions.ManualGpuShadowIndexed=!EnginePerformanceOptions.GpuShadowIndexed):null);
        ui.Add("RESIDENT HITS / MISSES / BYPASS",$"{_gameLighting?.LastGpuResidentHits??0} / {_gameLighting?.LastGpuResidentMisses??0} / {_gameLighting?.LastGpuResidentBypasses??0}",null);
        ui.Add("INDEXED DRAWS / BYPASS",$"{_gameLighting?.LastGpuIndexedDraws??0} / {_gameLighting?.LastGpuIndexBypasses??0}",null);
        ui.Add("GPU RESIDENT / INDEX STATUS",$"{_gameLighting?.GpuResidentStatus??"NOT EXERCISED"} / {_gameLighting?.GpuIndexStatus??"NOT EXERCISED"}",null);
        ui.Add("SKIP REDUNDANT TARGET BINDS",On(EnginePerformanceOptions.GpuScopedBinds),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuScopedBinds=!EnginePerformanceOptions.GpuScopedBinds));
        ui.Add("SKIP OVERWRITTEN MATERIAL CLEAR",On(EnginePerformanceOptions.GpuMaterialClear),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuMaterialClear=!EnginePerformanceOptions.GpuMaterialClear));
        ui.Add("GPU BUFFER STATUS",_gameLighting?.GpuBufferStatus??"NOT EXERCISED",null);
        ui.Add("BUFFERED / REFERENCE / FALLBACK",$"{_gameLighting?.LastGpuBufferedDraws??0} / {_gameLighting?.LastGpuReferenceDraws??0} / {_gameLighting?.LastGpuFallbackDraws??0}",null);
        ui.Add("SHADOW UPLOAD / BUFFER BYTES",$"{_gameLighting?.LastGpuUploadBytes??0} / {_gameLighting?.GpuBufferBytes??0}",null);
        ui.Add("LIGHT-LOCAL HARD MASKS",On(EnginePerformanceOptions.GpuLocalMasks),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuLocalMasks=!EnginePerformanceOptions.GpuLocalMasks));
        ui.Add("SPECIALIZED MATERIAL SHADERS",On(EnginePerformanceOptions.GpuSpecializedMaterials),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuSpecializedMaterials=!EnginePerformanceOptions.GpuSpecializedMaterials));
        ui.Add("FUSED LAYER COMPOSITION",On(EnginePerformanceOptions.GpuFusedLayer),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuFusedLayer=!EnginePerformanceOptions.GpuFusedLayer));
        ui.Add("BATCH UNSHADOWED LIGHTS",On(EnginePerformanceOptions.GpuBatchUnshadowed),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuBatchUnshadowed=!EnginePerformanceOptions.GpuBatchUnshadowed));
        ui.Add("FAST BLOOM (QUALITY EXPERIMENT)",On(EnginePerformanceOptions.GpuFastBloom),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuFastBloom=!EnginePerformanceOptions.GpuFastBloom));
        ui.Add("INSTANCED PARTICLE DRAW",On(EnginePerformanceOptions.GpuInstancedParticles),()=>LabChange(()=>EnginePerformanceOptions.ManualGpuInstancedParticles=!EnginePerformanceOptions.GpuInstancedParticles));
        ui.Add("LOCAL MASK BUILDS / HITS / BYPASS",$"{_gameLighting?.LastGpuLocalBuilds??0} / {_gameLighting?.LastGpuLocalHits??0} / {_gameLighting?.LastGpuLocalBypasses??0}",null);
        ui.Add("LOCAL MASK BYTES",$"{_gameLighting?.GpuLocalBytes??0}",null);
        ui.Add("MATERIAL SPECIALIZED / LAYER FUSED",$"{_gameLighting?.LastGpuSpecializedPasses??0} / {_gameLighting?.LastGpuFusedLayers??0}",null);
        ui.Add("DIRECT LIGHT DRAWS / FAST BLOOM",$"{_gameLighting?.LastGpuDirectLights??0} / {_gameLighting?.LastGpuFastBloom??0}",null);
        ui.Add("PARTICLE INSTANCES / DRAWS",$"{_particleVfx?.LastGpuInstances??0} / {_particleVfx?.LastGpuDraws??0}",null);
        ui.Add("GPU PARTICLE STATUS",_particleVfx?.GpuStatus??"UNAVAILABLE",null);
        ui.Choice("PARTICLE STRESS COUNT",GpuStressCount.ToString(),new[]{"0","1000","5000","10000","25000","50000"},i=>LabChange(()=>_gpuStressCount=new[]{0,1000,5000,10000,25000,50000}[i]));
        ui.Add("GPU ANALYTIC STRESS MOTION",EnginePerformanceOptions.GpuInstancedParticles?On(_gpuStressMotion):"BYPASSED",EnginePerformanceOptions.GpuInstancedParticles?()=>LabChange(()=>_gpuStressMotion=!_gpuStressMotion):null);
        ui.Add("GPU TIMESTAMP PROFILING",On(_gpuLabTiming),()=>LabChange(()=>_gpuLabTiming=!_gpuLabTiming));
        ui.Add("GPU FRAME TIMESTAMP",_gpuTimer.Readout,null);
        ui.Add("GPU PASS TIMES","EXTERNAL PROFILER",null);
        ui.Add("SKIPPED TARGET BINDS / CLEAR PX",$"{_gameLighting?.LastGpuSkippedBinds??0} / {_gameLighting?.LastGpuSkippedClearPixels??0}",null);
        ui.Add("SHADOW PREP / DRAW WALL MS (PROFILE RUN)",$"{(_gameLighting?.LastGpuShadowPrepareMs??0):F2} / {(_gameLighting?.LastGpuShadowDrawMs??0):F2}",null);
            return;
        }
        bool all = _perfMask==AllPerfOptimizations && LabProfile.AllRuntimeOptimizations;
        ui.Add("ENABLE ALL OPTIMIZATIONS",all?"ALL 14 ON":"ENABLE (QUALITY UNCHANGED)",EnableAllLabOptimizations,all);
        ui.Add("DISABLE ALL LIVE OPTIMIZATIONS","DISABLE",() => { EnginePerformanceOptions.ManualGpuShadowBuffers=false; EnginePerformanceOptions.ManualGpuShadowResident=EnginePerformanceOptions.ManualGpuShadowIndexed=false; EnginePerformanceOptions.ManualGpuLocalMasks=false; EnginePerformanceOptions.ManualGpuSpecializedMaterials=false; EnginePerformanceOptions.ManualGpuFusedLayer=false; EnginePerformanceOptions.ManualGpuBatchUnshadowed=false; EnginePerformanceOptions.ManualGpuFastBloom=false; EnginePerformanceOptions.ManualGpuInstancedParticles=false; EnginePerformanceOptions.ManualGpuMaterialClear=false; EnginePerformanceOptions.ManualGpuScopedBinds=false;  SetAllPerfOptimizations(false); CustomLab(LabProfile with { OptimizationMask=0, StaticFields=false, StaticShadowFields=false }); });
        ui.Choice("GRAPHICS PRESET",CurrentLabGraphicsPreset,GraphicsPresetLabels,ApplyLabGraphicsPreset);
        ui.Add("BUDGET / BLOOM / SAMPLES",$"{_lightingQuality} / {EnginePerformanceOptions.Po10bBloomScale}% / {Samples(EnginePerformanceOptions.AreaSamples,EnginePerformanceOptions.Po10bAdaptiveSamples)}",null);
        ui.Add("GPU RECOMMENDED (SHADER CANDIDATE)","APPLY",()=>LabChange(ApplyRecommendedGpu));
        ui.Add("GPU ADAPTER",GraphicsDevice.Adapter.Description,null);
        ui.Add("GPU CONTROLS", "OPEN",()=> { _labGpuPage=true; ui.GoToFirstPage(); });
        ui.Add("BUILD / DEBUGGER",LabBuildMode+" / "+(System.Diagnostics.Debugger.IsAttached?"ATTACHED":"DETACHED"),null);
        bool processAll=EnginePerformanceOptions.Snapshots && EnginePerformanceOptions.HudAtlas && EnginePerformanceOptions.ArtworkPreparation && EnginePerformanceOptions.DiagnosticRecords && EnginePerformanceOptions.LightingValidation && EnginePerformanceOptions.LightingBuffers && EnginePerformanceOptions.LightingParameters && EnginePerformanceOptions.LightingSamples && EnginePerformanceOptions.SharedSamples && EnginePerformanceOptions.HashedVisibility && EnginePerformanceOptions.VisibleLayers && EnginePerformanceOptions.PreparedMaterials && EnginePerformanceOptions.CameraConvergence && EnginePerformanceOptions.Po10fCasterBuffers;
        ui.Add("PROCESS-ONLY OPTIMIZATIONS",processAll?"ALL ON / STARTUP":"SOME OFF / RESTART",null);
        if (IsPerformanceLab)
        {
            ui.Add("CAPTURE CURRENT",PerfCaptureActive?"RUNNING":"START",PerfCaptureActive?null:() => { BeginPerfCapture(); ui.Release(); ui.Expanded=false; });
            ui.Add("CANCEL CAPTURE",PerfCaptureActive?"CANCEL":"IDLE",PerfCaptureActive?CancelPerfCapture:null);
            ui.Choice("CAPTURE LENGTH",""+_manualWarmup+" WARMUP / "+_manualRecorded+" RECORD",new[]{"QUICK 60 / 120","STANDARD 90 / 180","FULL 120 / 300"},PerfCaptureActive?null:i=> { _manualWarmup=new[]{60,90,120}[i]; _manualRecorded=new[]{120,180,300}[i]; });
            ui.Choice("CUSTOM WARMUP",_manualWarmup.ToString(),WarmupOptions.Select(n=>n.ToString()).ToArray(),PerfCaptureActive?null:i=>_manualWarmup=WarmupOptions[i]);
            ui.Choice("CUSTOM RECORDED FRAMES",_manualRecorded.ToString(),RecordedOptions.Select(n=>n.ToString()).ToArray(),PerfCaptureActive?null:i=>_manualRecorded=RecordedOptions[i]);
            ui.Add("AUTOMATED MULTI-CASE TIERS","USE PYTHON RUNNER",null);
        }
        ui.Add("RESET LAB DEFAULTS","RESET",ResetLabControls);
        string[] fixtures={"normal","adaptive","static","static_shadow"};
        ui.Choice("FIXTURE",_qualityLabFixture,fixtures,i=>LabChange(()=>_qualityLabFixture=fixtures[i]));
        if (_qualityLabFixture != "normal") ui.Add("FIXTURE LIGHT POSITIONS","WORLD FIXED",null);
        ui.Choice("LIGHT WORKLOAD",PerfWorkloads[Math.Clamp(_phase21LabMode-1,0,5)],PerfWorkloads,i=>LabChange(()=> { _phase21LabMode=i+1; _lightingStress=i==5; _qualityLabFixture="normal"; }));
        if (IsQualityLab)
        {
            string[] coverage={"PRESERVE SCENE LIGHTS", "QUALITY BUDGET"};
            ui.Choice("LIGHT COVERAGE",ManualCoverageAvailable ? coverage[_labPreserveSceneLights?0:1] : "QUALITY BUDGET (FIXTURE/STRESS)",
                coverage,ManualCoverageAvailable ? i=>LabChange(()=>_labPreserveSceneLights=i==0) : null);
            var budget=EffectiveFrameLightBudget;
            ui.Add("LIGHT / SHADOW LIMIT",$"{budget.MaxLights} / {budget.MaxShadowLights}",null);
            if (_qualityLabFixture == "normal" && _lightingEnabled && _l19dWorldLightsEnabled)
            {
                int active=_labLightSelection.Count(d=>d.IsActive), visible=_labLightSelection.Count(d=>d.IsInView);
                int shadowed=_labLightSelection.Count(d=>d.State==LightSelectionState.ActiveShadowed);
                int dropped=_labLightSelection.Count(d=>d.State is LightSelectionState.LightBudget or LightSelectionState.ShadowBudget);
                ui.Add("LIGHTS ACTIVE / VISIBLE / SHADOWED",$"{active} / {visible} / {shadowed}",null);
                ui.Add("LIGHTS DROPPED BY BUDGET",dropped==0?"0":$"{dropped} - SEE LIGHT INSPECTION",null);
            }
        }
        string[] sweepModes={"FOLLOW PLAYER","HORIZONTAL","VERTICAL","BOTH AXES"};
        int travelX=Math.Max(0,(_ldtkLevel?.WidthPx??0)-GraphicsDevice.Viewport.Width);
        int travelY=Math.Max(0,(_ldtkLevel?.HeightPx??0)-GraphicsDevice.Viewport.Height);
        int[] available=Enumerable.Range(0,4).Where(i=>((i&1)==0||travelX>0)&&((i&2)==0||travelY>0)).ToArray();
        int activeAxis=_qualityLabPan?_qualityLabSweepAxis:0;
        ui.Choice("CAMERA MODE",sweepModes[activeAxis]+(available.Contains(activeAxis)?"":" (CLAMPED)"),available.Select(i=>sweepModes[i]).ToArray(),i=>LabChange(()=> { int mode=available[i]; _qualityLabPan=mode!=0; _qualityLabSweepAxis=mode==0?3:mode; }));
        if (_lastLightingView is LightingViewport cameraView)
            ui.Add("CAMERA WORLD CENTER",$"{cameraView.Focus.X:F2}, {cameraView.Focus.Y:F2}",null);
        ui.Add("CAMERA TRAVEL X / Y (PX)",$"{travelX} / {travelY}",null);
        ui.Toggle("LIGHTING",_lightingEnabled,() => LabChange(() => _lightingEnabled=!_lightingEnabled));
        ui.Toggle("LIGHT / SCENE ANIMATION",!_perfFreeze,TogglePerfFreeze);
        ui.Add("WALL ANIMATION",On(_lightingEvents?.WallMotionEnabled??false),_lightingEvents is null?null:() => LabChange(()=> { if(_lightingEvents is not null) _lightingEvents.WallMotionEnabled=!_lightingEvents.WallMotionEnabled; }));
        ui.Choice("SHADOW EDGE",(_shadowQualityPolicy==ShadowQualityPolicy.FollowLightingQuality?ShadowContactControls.QualityPreset(_lightingQuality):_shadowEdgePreset).ToString(),Enum.GetNames<ShadowEdgePreset>(),i=>LabChange(()=> { _shadowQualityPolicy=ShadowQualityPolicy.Manual; _shadowEdgePreset=Enum.GetValues<ShadowEdgePreset>()[i]; }));
        ui.Toggle("BLOOM",_l19Bloom,() => LabChange(() => _l19Bloom=!_l19Bloom));
        ui.Toggle("EMISSION",_l19Emission,() => LabChange(() => _l19Emission=!_l19Emission));
        ui.Toggle("PRODUCTION NORMAL MAPS",_labNormalMaps,() => LabChange(() => _labNormalMaps=!_labNormalMaps));
        ui.Toggle("HULL OVERLAY",_phase21HullOverlay,TogglePerfHulls);
        ui.Toggle("VSYNC",!_perfUncappedRequested,RequestPerfPacing);
        ui.Choice("FPS LIMIT",_labCap==0?"UNCAPPED":_labCap.ToString(),CapLabels,i=>LabChange(()=>_labCap=Caps[i]));
        ui.Toggle("FPS SUMMARY",_perfOverlay,() => LabChange(() => _perfOverlay=!_perfOverlay));
        ui.Add("RENDERING QUALITY","OPTIONS BELOW",null);
        ui.Toggle("HDR",_l19Hdr,() => LabChange(()=>ApplyLightingToolAction(LightingToolAction.ToggleHdr)));
        ui.Toggle("WORLD LIGHTS",_l19dWorldLightsEnabled,() => LabChange(()=>ApplyLightingToolAction(LightingToolAction.ToggleShowcaseWorldLights)));
        ui.Choice("MATERIAL LIGHTING",_materialLightMode.ToString(),Enum.GetNames<ProductionMaterialLightMode>(),i=>LabChange(()=>_materialLightMode=Enum.GetValues<ProductionMaterialLightMode>()[i]));
        ui.Add("EXPOSURE",_l19Exposure.ToString("F2"),()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.CycleExposure)));
        ui.Add("BLOOM THRESHOLD",_l19BloomThreshold.ToString("F2"),()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.CycleBloomThreshold)));
        ui.Add("BLOOM STRENGTH",_l19BloomStrength.ToString("F2"),()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.CycleBloomStrength)));
        ui.Add("BLOOM SCATTER",_l20BloomScatter.ToString("F2"),()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.CycleBloomScatter)));
        ui.Toggle("BLOOM ONLY",_l20BloomOnly,()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.ToggleBloomOnly)));
        ui.Choice("SHADOW FILTER",_shadowFilterMode.ToString(),Enum.GetNames<ShadowFilterMode>(),i=>LabChange(()=>_shadowFilterMode=Enum.GetValues<ShadowFilterMode>()[i]));
        ui.Choice("SHADOW QUALITY POLICY",_shadowQualityPolicy.ToString(),Enum.GetNames<ShadowQualityPolicy>(),i=>LabChange(()=>_shadowQualityPolicy=Enum.GetValues<ShadowQualityPolicy>()[i]));
        var authoredLights=_lightingEvents?.CurrentAuthoredLights();
        if (_qualityLabFixture=="normal" && authoredLights is not null && authoredLights.Count>0)
        {
            string[] lightNames=authoredLights.Select((light,index)=>$"{index+1}: {light.Tag}").ToArray();
            ui.Choice("AUTHORED SHADOW LIGHT",lightNames[_shadowSelectedLightIndex%lightNames.Length],lightNames,i=>LabChange(()=>_shadowSelectedLightIndex=i));
            var selected=authoredLights[_shadowSelectedLightIndex%authoredLights.Count];
            float emitter=_shadowEmitterOverrides.TryGetValue(selected.Id,out var current)?current:8f;
            float[] radii={2f,4f,8f,12f};
            ui.Choice("SHADOW EMITTER RADIUS",$"{emitter:F0} PX",radii.Select(v=>$"{v:F0} PX").ToArray(),i=>LabChange(()=>_shadowEmitterOverrides[selected.Id]=radii[i]));
        }
        ui.Choice("QUALITY BUDGET",_lightingQuality.ToString(),PerfQualities.Select(q=>q.ToString()).ToArray(),i=>SetPerfQuality(PerfQualities[i]));
        ui.Choice("LIGHTING RESOLUTION",EnginePerformanceOptions.Po10LightScale+"%",ScaleLabels,i=>CustomLab(LabProfile with { LightScaleOverride=ScaleValues[i] }));
        ui.Choice("SHADOW RESOLUTION",EnginePerformanceOptions.Po10MaskScale+"%",ScaleLabels,i=>CustomLab(LabProfile with { MaskScaleOverride=ScaleValues[i] }));
        ui.Choice("BLOOM RESOLUTION",EnginePerformanceOptions.Po10bBloomScale+"%",ScaleLabels,i=>CustomLab(LabProfile with { BloomScale=ScaleValues[i] }));
        ui.Choice("AREA SAMPLES",Samples(EnginePerformanceOptions.AreaSamples,EnginePerformanceOptions.Po10bAdaptiveSamples),SampleLabels,
            i=>CustomLab(LabProfile with { Adaptive=i==1,AreaSamples=i<2?0:(i-2)*2+1 }));
        ui.Choice("AMBIENT (AUTHORED MULTIPLIER)",_labAmbient+"%",new[]{"100%","75%","50%","25%","0%"},i=>LabChange(()=>_labAmbient=100-i*25));
        ui.Toggle("STATIC FIELD CACHE",LabProfile.StaticFields,() => CustomLab(LabProfile with { StaticFields=!LabProfile.StaticFields, StaticShadowFields=!LabProfile.StaticFields && LabProfile.StaticShadowFields }));
        ui.Toggle("STATIC HARD SHADOW CACHE",LabProfile.StaticShadowFields,() => CustomLab(LabProfile with { StaticShadowFields=!LabProfile.StaticShadowFields, StaticFields=LabProfile.StaticShadowFields?LabProfile.StaticFields:true }));
        ui.Add("ADVANCED OPTIMIZATIONS","INDEPENDENT FLAGS",null);
        for(int i=0;i<PerfOptimizationNames.Length;i++) { int bit=1<<i; ui.Toggle(PerfOptimizationNames[i],PerfEnabled(bit),() => TogglePerfOptimization(bit)); }
        string[] names={"MATERIAL REGIONS","LIGHT REGIONS","SHARED FIELDS","SKIP CLEARS","MASK CAPACITY"};
        for(int i=0;i<names.Length;i++) { int bit=1<<i; ui.Toggle(names[i],LabProfile.Enabled(bit),() => CustomLab(LabProfile with { OptimizationMask=(LabProfile.OptimizationMask ?? (LabProfile.Optimized?31:0)) ^ bit })); }
        if(IsPerformanceLab) ui.Choice("DIAGNOSTIC ISOLATION",PerfDiagnostics[_perfIsolation],PerfDiagnostics,SetPerfIsolation);
        ui.Add("SYSTEM AND DIAGNOSTICS","LAB TOOLS",null);
        ui.Toggle("COLLISION OVERLAY",_ldtkShowCollisionDebug,()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.ToggleCollision)));
        ui.Toggle("COLLISION BACKDROP",_ldtkDrawCollisionBackdrop,()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.ToggleBackdrop)));
        ui.Toggle("REDUCED FLASHES",_reducedLightingFlashes,()=>LabChange(()=>ApplyLightingToolAction(LightingToolAction.ToggleReducedFlashes)));
        ui.Add("AUDIO MUTE",On(_audio?.IsMuted??false),_audio is null?null:()=>ApplyLightingToolAction(LightingToolAction.ToggleAudioMute));
        ui.Add("VOLUME DOWN",_audio is null?"UNAVAILABLE":$"{_audio.Mixer.MasterVolume:P0}",_audio is null?null:()=>ApplyLightingToolAction(LightingToolAction.VolumeDown));
        ui.Add("VOLUME UP",_audio is null?"UNAVAILABLE":$"{_audio.Mixer.MasterVolume:P0}",_audio is null?null:()=>ApplyLightingToolAction(LightingToolAction.VolumeUp));
        ui.Add("EXPORT DIAGNOSTIC REPORT",_diagnostics is null?"UNAVAILABLE":"EXPORT ZIP",_diagnostics is null?null:()=>ApplyLightingToolAction(LightingToolAction.ExportReport));
        if (_diagnostics is not null) ui.Add("REPORT STATUS",_diagnostics.StatusMessage,null);
        ui.Add("PROCESS-ONLY FLAGS","RESTART / PYTHON RUNNER",null);
        ui.Add("SNAPSHOT REUSE",On(EnginePerformanceOptions.Snapshots),null);
        ui.Add("HUD ATLAS",On(EnginePerformanceOptions.HudAtlas),null);
        ui.Add("ARTWORK PREPARATION",On(EnginePerformanceOptions.ArtworkPreparation),null);
        ui.Add("STRUCTURED DIAGNOSTICS",On(EnginePerformanceOptions.DiagnosticRecords),null);
        ui.Add("LIGHTING VALIDATION CACHE",On(EnginePerformanceOptions.LightingValidation),null);
        ui.Add("LIGHTING BUFFER REUSE",On(EnginePerformanceOptions.LightingBuffers),null);
        ui.Add("LIGHTING PARAMETER CACHE",On(EnginePerformanceOptions.LightingParameters),null);
        ui.Add("PREPARED LIGHT SAMPLES",On(EnginePerformanceOptions.LightingSamples),null);
        ui.Add("CAMERA CONVERGENCE",On(EnginePerformanceOptions.CameraConvergence),null);
        ui.Add("CASTER BUFFER REUSE",On(EnginePerformanceOptions.Po10fCasterBuffers),null);
        ui.Add("SHARED SAMPLE PREPARATION",On(EnginePerformanceOptions.SharedSamples),null);
        ui.Add("HASHED VISIBILITY",On(EnginePerformanceOptions.HashedVisibility),null);
        ui.Add("VISIBLE LAYER CULLING",On(EnginePerformanceOptions.VisibleLayers),null);
        ui.Add("PREPARED MATERIALS",On(EnginePerformanceOptions.PreparedMaterials),null);
        if(IsQualityLab)
        {
            ui.Add("LIGHT INSPECTION","ACTUAL FRAME PLANNER",null);
            if (_qualityLabFixture != "normal") ui.Add("SELECTION DETAILS","FIXTURE OVERRIDES PLANNER",null);
            else if (!_lightingEnabled || !_l19dWorldLightsEnabled) ui.Add("SELECTION DETAILS","LIGHTING DISABLED",null);
            else
            {
                // A selector rather than dozens of duplicated controls. Stress proxies
                // are summarized in the totals, not added to this scene-light menu.
                var entries=_labLightSelection.Where(d=>d.Light.Owner is "level" or "emissive")
                    .OrderBy(d=>d.Light.Position.X).ThenBy(d=>d.Light.Id,StringComparer.Ordinal).ToArray();
                if (entries.Length > 0)
                {
                    int selectedIndex=Array.FindIndex(entries,d=>d.Light.Id==_labInspectedLightId);
                    if (selectedIndex<0) selectedIndex=Array.FindIndex(entries,d=>d.IsInView);
                    if (selectedIndex<0) selectedIndex=0;
                    _labInspectedLightId=entries[selectedIndex].Light.Id;
                    string Label(LightSelectionDiagnostic item)
                    {
                        var light=item.Light;
                        string name=light.Owner=="emissive" ? (light.Color.R>.9 && light.Color.G<.3 && light.Color.B>.3 ? "PINK EMITTER" :
                            light.Color.R<.2 && light.Color.G>.7 && light.Color.B>.7 ? "CYAN EMITTER" : "EMISSIVE PROXY") : light.Tag;
                        if (string.IsNullOrEmpty(name)) name=light.Owner;
                        return $"{name} X {light.Position.X:F1}";
                    }
                    var entry=entries[selectedIndex];
                    string state=entry.State switch
                    {
                        LightSelectionState.ActiveShadowed=>"ACTIVE / SHADOWED",
                        LightSelectionState.ActiveUnshadowed=>"ACTIVE / NO SHADOW AUTHORED",
                        LightSelectionState.ActiveShadowLimited=>"ACTIVE / SHADOW BUDGET",
                        LightSelectionState.ShadowBudget=>"DROPPED / SHADOW BUDGET",
                        LightSelectionState.LightBudget=>"DROPPED / LIGHT BUDGET",
                        LightSelectionState.WorkloadFiltered=>"FILTERED / WORKLOAD",
                        _=>"OUTSIDE VIEW"
                    };
                    ui.Choice("INSPECT SCENE LIGHT",Label(entry),entries.Select(Label).ToArray(),i=>_labInspectedLightId=entries[i].Light.Id);
                    ui.Add("SELECTED LIGHT STATUS",state,null);
                    ui.Add("SELECTED LIGHT WORLD X / Y",$"{entry.Light.Position.X:F2} / {entry.Light.Position.Y:F2}",null);
                }
                else ui.Add("SELECTION DETAILS","WAITING FOR FRAME",null);
            }
        }
        ui.Add("RENDERER METRICS","LIVE STATUS",null);
        ui.Add("DRAW FPS (RECENT WALL TIME)",_liveFrameReadout.Ready?$"{_liveFrameReadout.FramesPerSecond:F1}":"SAMPLING",null);
        ui.Add("SIM TICKS / SECOND",_liveFrameReadout.Ready?$"{_liveFrameReadout.TicksPerSecond:F1}":"SAMPLING",null);
        ui.Add("RECENT SAMPLE DURATION",LabTiming(_liveFrameReadout.WindowMilliseconds),null);
        ui.Add("FRAME MEAN / P95 (120 FRAMES)",_frameIntervals.Count>0?$"{_frameIntervals.Mean:F2} / {_frameIntervals.P95:F2} MS":"SAMPLING",null);
        ui.Add("UPDATE CPU / FRAME",LabTiming(_liveFrameReadout.UpdateMilliseconds),null);
        ui.Add("DRAW CPU / FRAME",LabTiming(_liveFrameReadout.DrawMilliseconds),null);
        ui.Add("PRESENT / DRIVER WAIT",LabTiming(_liveFrameReadout.PresentMilliseconds),null);
        ui.Add("OTHER / PACING WALL TIME",LabTiming(_liveFrameReadout.OtherMilliseconds),null);
        ui.Add("WINDOW ACTIVE",On(IsActive),null);
        ui.Add("ACTUAL VSYNC",On(_graphics.SynchronizeWithVerticalRetrace),null);
        ui.Add("ACTUAL LIGHT TARGET",$"{_gameLighting?.LightWidth??0} X {_gameLighting?.LightHeight??0}",null);
        ui.Add("ACTUAL MASK TARGET",$"{_gameLighting?.MaskWidth??0} X {_gameLighting?.MaskHeight??0}",null);
        ui.Add("PERSISTENT MASK HITS",(_gameLighting?.LastPersistentMaskHits??0).ToString(),null);
        ui.Add("STATIC HARD FIELD HITS",(_gameLighting?.LastStaticShadowFieldHits??0).ToString(),null);
        ui.Add("CAPACITY FALLBACKS",(_gameLighting?.LastFallbackCapacity??0).ToString(),null);
        ui.Add("DRAW CALLS / TARGET CHANGES",$"{_perfDrawCalls} / {_perfTargetChanges}",null);
        ui.Add("ALLOCATION / FRAME",$"{_perfFrameAllocatedBytes/1024} KIB",null);
        if(IsPerformanceLab) ui.Add("CAPTURE STATUS",_phase21BenchmarkStatus,null);
    }
    private bool HandlePanel(InteractiveControlPanel ui,KeyboardState keys,GamePadState pad,bool click,Point point,bool modal=false) =>
        ui.Handle(GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height,point,click,
            JustPressed(keys,Keys.Tab)||JustPressed(pad,Buttons.RightShoulder),
            JustPressed(keys,Keys.Escape)||JustPressed(pad,Buttons.B),
            JustPressed(keys,Keys.Up)||JustPressed(pad,Buttons.DPadUp),JustPressed(keys,Keys.Down)||JustPressed(pad,Buttons.DPadDown),
            JustPressed(keys,Keys.Left)||JustPressed(pad,Buttons.DPadLeft),JustPressed(keys,Keys.Right)||JustPressed(pad,Buttons.DPadRight),
            JustPressed(keys,Keys.Enter)||JustPressed(pad,Buttons.A),modal);
    private bool HandleManualLab(KeyboardState keys,GamePadState pad,bool click,Point point)
    {
        if((!InteractiveLab && (!IsSpatialLab || AutomatedLaunch)) || _flow.IsMenuOpen || _flow.IsTransitionActive) { _labControls.Release(); return false; }
        bool input=click || JustPressed(keys,Keys.Tab)||JustPressed(pad,Buttons.RightShoulder)||
            JustPressed(keys,Keys.Escape)||JustPressed(pad,Buttons.B)||JustPressed(keys,Keys.Enter)||JustPressed(pad,Buttons.A)||
            JustPressed(keys,Keys.Up)||JustPressed(keys,Keys.Down)||JustPressed(keys,Keys.Left)||JustPressed(keys,Keys.Right)||
            JustPressed(pad,Buttons.DPadUp)||JustPressed(pad,Buttons.DPadDown)||JustPressed(pad,Buttons.DPadLeft)||JustPressed(pad,Buttons.DPadRight);
        if(!input) return _labControls.Focused;
        BuildLabControls(); _labUiRefresh=0;
        bool handled=HandlePanel(_labControls,keys,pad,click,point);
        if(PerfCaptureActive && _labControls.Expanded) CancelPerfCapture();
        return handled;
    }
    private void DrawManualLab()
    {
        // Rebuild controls only on interactions or at a modest status refresh rate;
        // collapsed captures do not allocate a fresh control list every frame.
        long now=System.Diagnostics.Stopwatch.GetTimestamp();
        if(_labControls.Expanded && (_labUiRefresh==0 || now-_labUiRefresh>System.Diagnostics.Stopwatch.Frequency/8))
        { BuildLabControls(); _labUiRefresh=now; }
        _labControls.CompactAction=PerfCaptureActive?CancelPerfCapture:null;
        _labControls.Draw(_spriteBatch!,_pixel!,GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height,IsSpatialLab?"ENGINE SCALABILITY LAB":IsQualityLab?"LIGHTING QUALITY LAB":"ENGINE PERFORMANCE LAB",PerfCaptureActive||_phase21BenchmarkStatus.StartsWith("SAVED:",StringComparison.Ordinal)?_phase21BenchmarkStatus:IsSpatialLab||_perfOverlay?LiveLabStatus:"GRAPHICS "+CurrentLabGraphicsPreset);
    }
    private void BeginGraphicsDraft()
    {
        _graphicsDraft=EnginePerformanceOptions.SavedGraphics ?? new GraphicsPreferences { VSync=EffectiveVSync, FpsLimit=EnginePerformanceOptions.RenderCap is 0 or 30 or 60 or 120 or 144?EnginePerformanceOptions.RenderCap:0 };
        if(_processOverridesGraphics) _graphicsDraft=_graphicsDraft with { LightScale=EnginePerformanceOptions.Po10LightScale,MaskScale=EnginePerformanceOptions.Po10MaskScale,BloomScale=EnginePerformanceOptions.Po10bBloomScale,Adaptive=EnginePerformanceOptions.Po10bAdaptiveSamples,AreaSamples=EnginePerformanceOptions.AreaSamples };
        _graphicsControls.Reset(); _graphicsNotice=_processOverridesGraphics?"PROCESS OVERRIDES ACTIVE - READ ONLY":"EDIT THEN APPLY AND SAVE";
    }
    private void EditGraphics(Func<GraphicsPreferences,GraphicsPreferences> change)
    { if(!_processOverridesGraphics) { _graphicsDraft=change(_graphicsDraft!); _graphicsNotice="UNSAVED CHANGES - APPLY OR BACK"; } }
    private void ApplyGraphicsDraft()
    {
        if(_processOverridesGraphics || _graphicsDraft is not { IsValid:true } next) return;
        var display=GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        if(next.Width>display.Width || next.Height>display.Height) { _graphicsNotice="RESOLUTION EXCEEDS DISPLAY - CHOOSE SMALLER"; return; }
        int oldW=_graphics.PreferredBackBufferWidth, oldH=_graphics.PreferredBackBufferHeight;
        bool oldFull=_graphics.IsFullScreen, oldVsync=_graphics.SynchronizeWithVerticalRetrace;
        try
        {
            _graphics.PreferredBackBufferWidth=next.Width; _graphics.PreferredBackBufferHeight=next.Height;
            _graphics.IsFullScreen=next.Fullscreen; _graphics.SynchronizeWithVerticalRetrace=InteractiveLab?!_perfUncappedRequested:next.VSync;
            _graphics.ApplyChanges();
            next.Save(_graphicsPath);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            _graphics.PreferredBackBufferWidth=oldW; _graphics.PreferredBackBufferHeight=oldH;
            _graphics.IsFullScreen=oldFull; _graphics.SynchronizeWithVerticalRetrace=oldVsync;
            try { _graphics.ApplyChanges(); } catch(Exception rollback) when(rollback is not OutOfMemoryException) { _graphicsNotice="DISPLAY RESTORE FAILED - RESTART GAME"; return; }
            _graphicsNotice="APPLY FAILED: "+ex.GetType().Name; return;
        }
        EnginePerformanceOptions.SavedGraphics=next;
        _perfUncappedActive=!_graphics.SynchronizeWithVerticalRetrace;
        if(!InteractiveLab) _lightingQuality=next.Quality;
        _gameLighting?.ResetManualLightingProfile(); _adaptiveAreaSamples.Clear(); ResetPerfWindow();
        _graphicsNotice=InteractiveLab?"SAVED - LAB LIGHTING OVERRIDE REMAINS":"APPLIED AND SAVED";
    }
    private void BuildGraphicsControls()
    {
        if(_graphicsDraft is null) BeginGraphicsDraft();
        var p=_graphicsDraft!; var ui=_graphicsControls; ui.Clear();
        void Add(string label,string value,Action action) => ui.Add(label,value,_processOverridesGraphics?null:action);
        Add("ENABLE ALL ESTABLISHED OPTS",p.AllEstablishedOptimizations?"ALL 13 ON":"ENABLE",() => EditGraphics(v=>v.EnableAllOptimizations()));
        Add("DISABLE ALL LIVE OPTIMIZATIONS",p.Profile==1&&!p.RenderProfile.StaticFields?"OFF":"DISABLE",() => EditGraphics(v=>v with { Profile=1, StaticFieldsOverride=false, StaticShadowFields=false, GpuShadowBuffers=false, GpuShadowResident=false, GpuShadowIndexed=false, GpuLocalMasks=false, GpuSpecializedMaterials=false, GpuFusedLayer=false, GpuBatchUnshadowed=false, GpuFastBloom=false, GpuInstancedParticles=false, GpuScopedBinds=false, GpuMaterialClear=false }));
        ui.Choice("GRAPHICS PRESET (LIGHT/SHADOW)",GraphicsQualityPreset.CurrentLabel(p.Quality,p.LightScale,p.MaskScale,p.BloomScale,p.Adaptive,p.AreaSamples),
            GraphicsPresetLabels,_processOverridesGraphics?null:i=>EditGraphics(v=>GraphicsQualityPreset.For(i).Apply(v)));
        ui.Add("BUDGET / BLOOM / SAMPLES",$"{p.Quality} / {p.BloomScale}% / {Samples(p.AreaSamples,p.Adaptive)}",null);
        Add("SKIP REDUNDANT TARGET BINDS",On(p.GpuScopedBinds),()=>EditGraphics(v=>v with { GpuScopedBinds=!v.GpuScopedBinds }));
        Add("SKIP OVERWRITTEN MATERIAL CLEAR",On(p.GpuMaterialClear),()=>EditGraphics(v=>v with { GpuMaterialClear=!v.GpuMaterialClear }));
        Add("GPU SHADOW SUBMISSION",p.GpuShadowBuffers?"BUFFERED (EXPERIMENTAL)":"COMPATIBILITY",() => EditGraphics(v=>v with { GpuShadowBuffers=!v.GpuShadowBuffers }));
        Add("GPU RECOMMENDED (SHADER CANDIDATE)","APPLY",()=>EditGraphics(v=>v with { GpuShadowBuffers=false, GpuShadowResident=false, GpuShadowIndexed=false, GpuLocalMasks=false, GpuSpecializedMaterials=true, GpuFusedLayer=true, GpuBatchUnshadowed=true, GpuFastBloom=false, GpuInstancedParticles=false, GpuScopedBinds=false, GpuMaterialClear=false }));
        Add("ENABLE ALL GPU EXPERIMENTS","ENABLE",() => EditGraphics(v=>v with { GpuShadowBuffers=true, GpuShadowResident=true, GpuShadowIndexed=true, GpuLocalMasks=true, GpuSpecializedMaterials=true, GpuFusedLayer=true, GpuBatchUnshadowed=true, GpuInstancedParticles=true, GpuScopedBinds=true, GpuMaterialClear=true }));
        Add("GPU COMPATIBILITY MODE","DISABLE GPU EXPERIMENTS",() => EditGraphics(v=>v with { GpuShadowBuffers=false, GpuShadowResident=false, GpuShadowIndexed=false, GpuLocalMasks=false, GpuSpecializedMaterials=false, GpuFusedLayer=false, GpuBatchUnshadowed=false, GpuFastBloom=false, GpuInstancedParticles=false, GpuScopedBinds=false, GpuMaterialClear=false }));
        ui.Add("GPU RESIDENT SHADOW VERTICES",p.GpuShadowBuffers?On(p.GpuShadowResident):"BYPASSED",_processOverridesGraphics||!p.GpuShadowBuffers?null:()=>EditGraphics(v=>v with { GpuShadowResident=!v.GpuShadowResident }));
        ui.Add("GPU INDEXED SHADOW FANS",p.GpuShadowBuffers?On(p.GpuShadowIndexed):"BYPASSED",_processOverridesGraphics||!p.GpuShadowBuffers?null:()=>EditGraphics(v=>v with { GpuShadowIndexed=!v.GpuShadowIndexed }));
        Add("LIGHT-LOCAL HARD MASKS",On(p.GpuLocalMasks),()=>EditGraphics(v=>v with { GpuLocalMasks=!v.GpuLocalMasks }));
        Add("SPECIALIZED MATERIAL SHADERS",On(p.GpuSpecializedMaterials),()=>EditGraphics(v=>v with { GpuSpecializedMaterials=!v.GpuSpecializedMaterials }));
        Add("FUSED LAYER COMPOSITION",On(p.GpuFusedLayer),()=>EditGraphics(v=>v with { GpuFusedLayer=!v.GpuFusedLayer }));
        Add("BATCH UNSHADOWED LIGHTS",On(p.GpuBatchUnshadowed),()=>EditGraphics(v=>v with { GpuBatchUnshadowed=!v.GpuBatchUnshadowed }));
        Add("FAST BLOOM (QUALITY EXPERIMENT)",On(p.GpuFastBloom),()=>EditGraphics(v=>v with { GpuFastBloom=!v.GpuFastBloom }));
        Add("INSTANCED PARTICLE DRAW",On(p.GpuInstancedParticles),()=>EditGraphics(v=>v with { GpuInstancedParticles=!v.GpuInstancedParticles }));
        Add("APPLY AND SAVE","APPLY",ApplyGraphicsDraft);
        Add("STATIC HARD SHADOW (EXPERIMENT)",On(p.StaticShadowFields),() => EditGraphics(v=>v with { StaticShadowFields=!v.StaticShadowFields,StaticFieldsOverride=v.StaticShadowFields?v.StaticFieldsOverride:true }));
        ui.Add("BACK / DISCARD UNSAVED","BACK",() => { _graphicsDraft=null; _flow.Back(); });
        Add("RESET DRAFT DEFAULTS","RESET",() => EditGraphics(_ => new()));
        void Choice(string label,string value,string[] options,Action<int> choose) => ui.Choice(label,value,options,_processOverridesGraphics?null:choose);
        Choice("RESOLUTION",$"{p.Width} X {p.Height}",new[]{"1280 X 720","1600 X 900","1920 X 1080"},i=>EditGraphics(v=>v with { Width=1280+i*320,Height=720+i*180 }));
        Add("DISPLAY MODE",p.Fullscreen?"FULLSCREEN":"WINDOWED",() => EditGraphics(v=>v with { Fullscreen=!v.Fullscreen }));
        Add("VSYNC",On(p.VSync),() => EditGraphics(v=>v with { VSync=!v.VSync }));
        Choice("FPS LIMIT",p.FpsLimit==0?"UNCAPPED":p.FpsLimit.ToString(),CapLabels,i=>EditGraphics(v=>v with { FpsLimit=Caps[i] }));
        Choice("LIGHTING QUALITY",p.Quality.ToString(),PerfQualities.Select(q=>q.ToString()).ToArray(),i=>EditGraphics(v=>v with { Quality=PerfQualities[i] }));
        Choice("LIGHTING RESOLUTION",p.LightScale+"%",ScaleLabels,i=>EditGraphics(v=>v with { LightScale=ScaleValues[i] }));
        Choice("SHADOW RESOLUTION",p.MaskScale+"%",ScaleLabels,i=>EditGraphics(v=>v with { MaskScale=ScaleValues[i] }));
        Choice("BLOOM RESOLUTION",p.BloomScale+"%",ScaleLabels,i=>EditGraphics(v=>v with { BloomScale=ScaleValues[i] }));
        Choice("AREA SAMPLES",Samples(p.AreaSamples,p.Adaptive),SampleLabels,i=>EditGraphics(v=>v with { Adaptive=i==1,AreaSamples=i<2?0:(i-2)*2+1 }));
        ui.Add("ACTIVE GAME RESOLUTION",$"{GraphicsDevice.PresentationParameters.BackBufferWidth} X {GraphicsDevice.PresentationParameters.BackBufferHeight}",null);
        ui.Add("ACTIVE VSYNC",On(_graphics.SynchronizeWithVerticalRetrace),null);
        ui.Add("LAB LIGHTING SETTINGS",InteractiveLab?"TEMPORARY OVERRIDE":"NOT ACTIVE",null);
    }
    private bool HandleGraphicsMenu(KeyboardState keys,GamePadState pad,bool click,Point point)
    {
        if(_flow.IsTransitionActive || _flow.CurrentMenu!=GameUiScreen.Graphics) { _graphicsDraft=null; return false; }
        if(JustPressed(keys,Keys.Escape)||JustPressed(pad,Buttons.B)) { if(_graphicsControls.Choosing) _graphicsControls.DismissChoice(); else { _graphicsDraft=null; _flow.Back(); } return true; }
        BuildGraphicsControls(); HandlePanel(_graphicsControls,keys,pad,click,point,true); return true;
    }
    private void DrawGraphicsMenu()
    {
        BuildGraphicsControls();
        _graphicsControls.Draw(_spriteBatch!,_pixel!,GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height,"GRAPHICS SETTINGS",_graphicsNotice,true);
    }
}
