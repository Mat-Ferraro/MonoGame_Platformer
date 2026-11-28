using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation;
using PlatformerSandbox.Simulation.Physics;
using PlatformerSandbox.Simulation.MovingPlatforms;
using System.Collections.Generic;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Presentation;

namespace PlatformerSandbox;

public sealed partial class Game1
{
    private PhysicsMaterialCatalog? _bodyLabMaterials;
    private string? _bodyLabMaterialError;
    private string _bodyLabFirst="Wood",_bodyLabSecond="Ice";
    private int _pbSelected=1,_pbTerrainId=-1,_pbPlatformId=-1,_pbOtherBodyId=-1;
    private bool _pbContactObservation;
    private bool _pbIntegrationPage;
    private string _pbNotice="READY";
    private int _pbVisible;
    private bool _pbVisualTargetVisible;
    private int PbVisualTargetId=>int.TryParse(Environment.GetEnvironmentVariable("PS_PB01_VISUAL_BODY"),out int n)&&n>0?n:1;
    private long? _pbWarmupStartTick;
    private bool _pbTickWarmupComplete;
    private long _pbStartSweeps,_pbStartContacts,_pbStartPairContacts,_pbStartCharacterPushes,_pbStartCharacterImpacts,_pbStartPlatformContacts,_pbStartPlatformStops,_pbStartDestroyed;
    private double _pbStartDynamicMs;
    private int _pbMeasuredPlaybackRequests,_pbMeasuredDeaths,_pbMeasuredRespawns;
    private int PbWarmupTicks=>int.TryParse(Environment.GetEnvironmentVariable("PS_PB01_WARMUP_TICKS"),out int n)&&n>0?n:60;
    private int PbRecordedTicks=>int.TryParse(Environment.GetEnvironmentVariable("PS_PB01_RECORDED_TICKS"),out int n)&&n>0?n:300;
    // Absolute world-tick windows, shared by every PB01 graphical configuration.
    private const long PbStartupAllowanceTicks=120;
    private long PbTargetStartTick=>PbStartupAllowanceTicks+PbWarmupTicks;
    private bool PbScriptEnabled=>PoAutomated||Environment.GetEnvironmentVariable("PS_PB01_TICK_BENCHMARK")=="1";
    private bool PbBenchmarkBoundaryReached=>PbTickBenchmark&&PerfCaptureActive&&_worldClockTest is WorldSession w&&
        w.WorldTick>=(_pbTickWarmupComplete?_pbMeasurementStartTick+PbRecordedTicks:PbTargetStartTick);
    private bool PbTickBenchmark=>_poAutoStarted&&Environment.GetEnvironmentVariable("PS_PB01_TICK_BENCHMARK")=="1";
    private void ResetPbBenchmark(){_pbWarmupStartTick=null;_pbTickWarmupComplete=false;_pbStartSweeps=_pbStartContacts=_pbStartPairContacts=_pbStartCharacterPushes=_pbStartCharacterImpacts=_pbStartPlatformContacts=_pbStartPlatformStops=_pbStartDestroyed=0;_pbStartDynamicMs=0;_pbMeasuredPlaybackRequests=_pbMeasuredDeaths=_pbMeasuredRespawns=0;}
    private void ApplyPbScript(WorldSession world)
    {
        if(!PbScriptEnabled||world.DynamicBodies is not DynamicBodyWorld d)return;
        long tick=world.WorldTick;
        if(tick%60==0&&d.Bodies.Any(b=>b.Id.LocalId==1))d.ApplyImpulse(1,new(0,2));
        // Version 3: repeatedly impulse the lower member of the authored prop stack.
        int stackId=d.Bodies.Any(b=>b.Id.LocalId==9)?9:6;
        if(tick%60==15&&d.Bodies.Any(b=>b.Id.LocalId==stackId))d.ApplyImpulse(stackId,new(0,4));
        if(Environment.GetEnvironmentVariable("PS_PB01_PLATFORM_SCRIPT")!="1")return;
        // Same chronological commands on all compared runs, including warmup.
        if(world.MovingPlatformTrajectories.Count>0&&tick%180 is 30 or 90 or 120)
        {
            int id=world.MovingPlatformTrajectories[0].Id;
            var command=tick%180==30?PlatformPlaybackCommand.Pause:tick%180==90?PlatformPlaybackCommand.Resume:PlatformPlaybackCommand.Reverse;
            world.ControlPlatform(id,command);
            if(PbTickBenchmark&&_pbTickWarmupComplete)_pbMeasuredPlaybackRequests++;
        }
        if(tick%300==200&&world.HasPlayer(1))world.RequestPlayerDeath(1);
    }
    private static string PbContent(string name)=>Path.Combine(Directory.GetParent(FindLdtkDirectory())!.FullName,"Physics",name);
    private static PhysicsMaterialCatalog LoadPbMaterials()=>PhysicsMaterialCatalog.FromJson(File.ReadAllText(PbContent("Materials.json")));
    private static WorldDynamicBodyFixtures LoadPbFixtures()=>WorldDynamicBodyFixtures.FromJson(File.ReadAllText(PbContent("DynamicBodies.json")));
    private static void PreparePbWorld(WorldSession world,string levelId)
    {
        // All authored worlds share the gameplay policy, not a level-specific exception handler.
        string lifecycle=PbContent("CrushLifecycle.json");
        var policy=File.Exists(lifecycle)?WorldCrushSettingsFile.FromJson(File.ReadAllText(lifecycle)).ForLevel(levelId):new WorldCrushSettingsFile();
        // Benchmarks explicitly use the standard death/immediate response, not edited player preferences.
        if(Environment.GetEnvironmentVariable("PS_PB01_PLATFORM_SCRIPT")=="1")world.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new());
        else world.ConfigureCrushLifecycle(policy.Crush,policy.Respawn);
        string path=PbContent("DynamicBodies.json");
        if(!File.Exists(path))
        {
            if(levelId is GameLevelIds.PhysicsInteractionLab or GameLevelIds.EngineIntegrationLab) throw new FileNotFoundException("Missing PB01 fixtures",path);
            return;
        }
        var fixtures=LoadPbFixtures();
        if(fixtures.Levels.ContainsKey(levelId)) fixtures.TryInstall(world,levelId,LoadPbMaterials());
    }
    private void PbEdit(Action operation)
    {
        try { operation(); _pbNotice="APPLIED"; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        { _pbNotice=ex.Message; }
    }
    private void BuildBodyFoundationControls(InteractiveControlPanel ui)
    {
        ui.Add("PB01 PHYSICS", "DYNAMIC CONTACTS / PLATFORM / LIFE", null);
        if(_worldClockTest is not WorldSession world) return;
        var frame=WorldPhysicalBodyView.Capture(world);
        ui.Add("STATIC / PLATFORMS",$"{world.StaticColliders.Count} / {world.MovingPlatforms.Count}",null);
        ui.Add("KINEMATIC CHARACTERS",frame.Bodies.Count(b=>b.Authority==MotionAuthority.CharacterController).ToString(),null);
        var crushPolicies=new[]{WorldCrushPolicy.Death,WorldCrushPolicy.RespawnAtNamedSpawn,WorldCrushPolicy.Throw};
        ui.Choice("CRUSH RESPONSE",world.CrushPolicy.ToString(),crushPolicies.Select(p=>p.ToString()).ToArray(),i=>PbEdit(()=>world.ConfigureCrushLifecycle(crushPolicies[i],world.RespawnSettings)));
        var modes=new[]{WorldRespawnMode.None,WorldRespawnMode.Immediate,WorldRespawnMode.Delayed,WorldRespawnMode.Manual};
        ui.Choice("DEATH RESPAWN MODE",world.RespawnSettings.Mode.ToString(),modes.Select(m=>m.ToString()).ToArray(),i=>PbEdit(()=>world.ConfigureCrushLifecycle(world.CrushPolicy,world.RespawnSettings with {Mode=modes[i],DelayTicks=modes[i]==WorldRespawnMode.Delayed?Math.Max(60,world.RespawnSettings.DelayTicks):0})));
        if(world.RespawnSettings.Mode==WorldRespawnMode.Delayed)
            ui.Choice("DELAY TICKS",world.RespawnSettings.DelayTicks.ToString(),new[]{"30","60","90","180"},i=>PbEdit(()=>world.ConfigureCrushLifecycle(world.CrushPolicy,world.RespawnSettings with {DelayTicks=new[]{30,60,90,180}[i]})));
        ui.Choice("MAXIMUM RESPAWNS",world.RespawnSettings.MaximumRespawns?.ToString()??"UNLIMITED",new[]{"UNLIMITED","0","1","3","5"},i=>PbEdit(()=>world.ConfigureCrushLifecycle(world.CrushPolicy,world.RespawnSettings with {MaximumRespawns=new int?[]{null,0,1,3,5}[i]})));
        if(world.HasPlayer(1))
        {
            var life=world.GetPlayer(1).Life;
            ui.Add("PLAYER 1 LIFE",life.IsDead?"DEAD / "+life.Mode:"ALIVE",null);
            ui.Add("RESPAWNS USED",life.RespawnsUsed.ToString(),null);
            ui.Add("TEST DEATH","NEXT TICK",()=>PbEdit(()=>world.RequestPlayerDeath(1)));
            if(life.IsDead&&life.Mode==WorldRespawnMode.Manual&&life.RespawnAllowed)
                ui.Add("REQUEST RESPAWN","WHEN SAFE",()=>PbEdit(()=>world.RequestPlayerRespawn(1)));
        }
        var platforms=world.MovingPlatformTrajectories.ToArray();
        if(platforms.Length>0)
        {
            if(!platforms.Any(p=>p.Id==_pbPlatformId)) _pbPlatformId=platforms[0].Id;
            var selected=platforms.Single(p=>p.Id==_pbPlatformId); int pid=selected.Id;
            ui.Choice("SELECT PLATFORM",pid.ToString(),platforms.Select(p=>p.Id.ToString()).ToArray(),i=>_pbPlatformId=platforms[i].Id);
            ui.Toggle("PLATFORM PAUSED",selected.IsPaused(world.WorldTimeSeconds),()=>PbEdit(()=>world.ControlPlatform(pid,selected.IsPaused(world.WorldTimeSeconds)?PlatformPlaybackCommand.Resume:PlatformPlaybackCommand.Pause)));
            ui.Add("REVERSE PLATFORM","AT CURRENT TICK",()=>PbEdit(()=>world.ControlPlatform(pid,PlatformPlaybackCommand.Reverse)));
            var sample=selected.Evaluate(world.WorldTimeSeconds);
            double?[] recoveryOptions={null,.1,.25,.5,1,2};
            string[] recoveryLabels={"HOLD","100 MS","250 MS","500 MS","1 S","2 S"};
            var recovery=world.GetPlatformObstructionRecoverySeconds(pid);
            ui.Choice("OBSTRUCTION RECOVERY",recovery is double seconds?$"{seconds*1000:F0} MS":"HOLD",recoveryLabels,
                i=>PbEdit(()=>world.ConfigurePlatformObstructionRecovery(pid,recoveryOptions[i])));
            var remaining=world.GetPlatformObstructionRemainingSeconds(pid);
            ui.Add("PROP OBSTRUCTION",!world.IsPropObstructed(pid)?"CLEAR":remaining is double wait?$"RETURN IN {wait*1000:F0} MS":"BLOCKED / MANUAL HOLD",null);
            if(world.IsPropObstructed(pid))
                ui.Add("HOLD PLATFORM","CANCEL AUTO RECOVERY",()=>PbEdit(()=>world.ControlPlatform(pid,PlatformPlaybackCommand.Pause)));
            ui.Add("PLATFORM VELOCITY",$"{sample.Velocity.X:F2}, {sample.Velocity.Y:F2}",null);
        }
        ui.Toggle("OBSERVE FACE CONTACTS",_pbContactObservation,()=>_pbContactObservation=!_pbContactObservation);
        if(_pbContactObservation)
        {
            var edges=new List<PhysicalBodyPair>(); var observed=frame.Bodies;
            // Geometry-only observation. Excludes fixed/fixed pairs. Not a solver or an active contact list.
            foreach(var a in observed.Where(b=>b.Id.Kind is PhysicalBodyKind.Dynamic or PhysicalBodyKind.KinematicCharacter))
                foreach(var b in observed)
                    if(a.Id!=b.Id && BodyContactGraph.TouchingFaces(a.Bounds,b.Bounds)) edges.Add(new(a.Id,b.Id));
            var islands=BodyContactGraph.Build(edges);
            ui.Add("FACE CANDIDATES / ISLANDS",$"{edges.Distinct().Count()} / {islands.Count}",null);
            ui.Add("CONTACT OBSERVATION","GEOMETRY ONLY / NOT RESOLVED",null);
        }
        if(world.DynamicBodies is not DynamicBodyWorld dynamics) {ui.Add("DYNAMIC BODIES","NO AUTHORED FIXTURES",null);return;}
        _bodyLabMaterials=dynamics.Materials;
        ui.Add("DYNAMIC BODIES",dynamics.Bodies.Count.ToString(),null);
        ui.Add("SWEEPS / CONTACTS",$"{dynamics.Metrics.Sweeps} / {dynamics.Metrics.Contacts}",null);
        ui.Add("PLATFORM CONTACTS / STOPS",$"{dynamics.PlatformContacts} / {world.PropPlatformStops}",null);
        ui.Add("PLATFORM RECOVERY ATTEMPTS",world.PropPlatformRecoveryAttempts.ToString(),null);
        ui.Add("PROPS CRUSH DESTROYED",dynamics.DestroyedByPlatform.ToString(),null);
        ui.Add("PROP PAIR IMPACTS",dynamics.PairContacts.ToString(),null);
        ui.Add("LARGEST CONTACT ISLAND",dynamics.LargestContactIsland.ToString(),null);
        ui.Add("LAST DYNAMIC STEP",$"{dynamics.LastStepMilliseconds:F3} MS",null);
        ui.Add("RESET BODIES","RELOAD FIXTURES",()=>PbEdit(()=>{LoadPbFixtures().TryInstall(world,_ldtkLevel!.World.LevelId,LoadPbMaterials());_pbSelected=1;_pbTerrainId=-1;}));
        ui.Add("RELOAD PHYSICS MATERIALS","READ JSON",()=>PbEdit(()=>dynamics.ReloadMaterials(LoadPbMaterials())));
        ui.Add("SPAWN WOOD CRATE","ABOVE PLAYER",()=>PbEdit(()=>{
            int id=dynamics.Bodies.Select(b=>b.Id.LocalId).DefaultIfEmpty(0).Max()+1;
            dynamics.Add(new(new(PhysicalBodyKind.Dynamic,id),new(.5,.5),"Wood"),new DynamicBodySettings{Filter=new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.All)},world.GetPlayer(1).Bounds.Center+new Vector2D(0,3));_pbSelected=id;
        }));
        var bodies=dynamics.Bodies.ToArray();
        if(bodies.Length>0)
        {
            if(!bodies.Any(b=>b.Id.LocalId==_pbSelected)) _pbSelected=bodies[0].Id.LocalId;
            var body=dynamics.Get(_pbSelected);int id=body.Id.LocalId;
            ui.Choice("SELECT BODY",id.ToString(),bodies.Select(b=>b.Id.LocalId.ToString()).ToArray(),i=>{_pbSelected=bodies[i].Id.LocalId;_pbTerrainId=-1;});
            ui.Add("REMOVE SELECTED BODY","REMOVE",()=>PbEdit(()=>dynamics.Remove(id)));
            string[] names=dynamics.Materials.Materials.Keys.ToArray();
            ui.Choice("BODY MATERIAL",body.Definition.MaterialId,names,i=>PbEdit(()=>dynamics.Configure(id,names[i],body.Definition.MassOverride,body.Settings)));
            ui.Choice("MASS OVERRIDE",body.Definition.MassOverride?.ToString("F1")??"DENSITY",new[]{"DENSITY","1","5","20"},i=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,new double?[]{null,1,5,20}[i],body.Settings)));
            ui.Add("EFFECTIVE MASS",body.Definition.ResolveMass(dynamics.Materials)!.Value.ToString("F2"),null);
            ui.Choice("GRAVITY SCALE",body.Settings.GravityScale.ToString("F1"),new[]{"0","0.5","1","2"},i=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,body.Definition.MassOverride,body.Settings with {GravityScale=new[]{0d,.5,1,2}[i]})));
            ui.Choice("BOUNCE CUTOFF (UNITS/S)",body.Settings.BounceThreshold.ToString("F2"),new[]{"0","0.25","0.5","1","2","5"},i=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,body.Definition.MassOverride,body.Settings with {BounceThreshold=new[]{0d,.25,.5,1,2,5}[i]})));
            ui.Toggle("TERRAIN COLLISION",(body.Settings.Filter.Mask&BodyCollisionLayer.Terrain)!=0,()=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,body.Definition.MassOverride,body.Settings with {Filter=new(BodyCollisionLayer.Dynamic,body.Settings.Filter.Mask^BodyCollisionLayer.Terrain)})));
            ui.Toggle("PROP COLLISION",(body.Settings.Filter.Mask&BodyCollisionLayer.Dynamic)!=0,()=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,body.Definition.MassOverride,body.Settings with {Filter=new(BodyCollisionLayer.Dynamic,body.Settings.Filter.Mask^BodyCollisionLayer.Dynamic)})));
            ui.Toggle("CHARACTER COLLISION",DynamicBodyWorld.CharacterSolid(body),()=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,body.Definition.MassOverride,body.Settings with {Filter=new(BodyCollisionLayer.Dynamic,body.Settings.Filter.Mask^BodyCollisionLayer.Character)})));
            ui.Toggle("PLATFORM COLLISION",DynamicBodyWorld.PlatformSolid(body),()=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,body.Definition.MassOverride,body.Settings with {Filter=new(BodyCollisionLayer.Dynamic,body.Settings.Filter.Mask^BodyCollisionLayer.Platform)})));
            var responses=new[]{RigidBodyCrushResponse.StopPlatform,RigidBodyCrushResponse.Destroy};
            ui.Choice("PROP CRUSH RESPONSE",body.Settings.CrushResponse.ToString(),responses.Select(r=>r.ToString()).ToArray(),i=>PbEdit(()=>dynamics.Configure(id,body.Definition.MaterialId,body.Definition.MassOverride,body.Settings with {CrushResponse=responses[i]})));
            var others=bodies.Where(b=>b.Id.LocalId!=id).Select(b=>b.Id.LocalId).ToArray();
            if(others.Length>0)
            {
                if(!others.Contains(_pbOtherBodyId))_pbOtherBodyId=others[0];int other=_pbOtherBodyId;
                ui.Choice("PROP PAIR ID",other.ToString(),others.Select(x=>x.ToString()).ToArray(),i=>_pbOtherBodyId=others[i]);
                ui.Toggle("IGNORE PROP PAIR",dynamics.IsBodyPairExcluded(id,other),()=>PbEdit(()=>dynamics.SetBodyPairExcluded(id,other,!dynamics.IsBodyPairExcluded(id,other))));
            }
            ui.Add("IMPULSE LEFT","-5",()=>PbEdit(()=>dynamics.ApplyImpulse(id,new(-5,0))));
            ui.Add("IMPULSE RIGHT","+5",()=>PbEdit(()=>dynamics.ApplyImpulse(id,new(5,0))));
            ui.Add("IMPULSE UP","+8",()=>PbEdit(()=>dynamics.ApplyImpulse(id,new(0,8))));
            ui.Add("FORCE NEXT TICK","+120 X",()=>PbEdit(()=>dynamics.ApplyForce(id,new(120,0))));
            if(world.StaticColliders.Count>0)
            {
                if(!world.StaticColliders.Any(c=>c.Id==_pbTerrainId)) _pbTerrainId=world.StaticColliders.OrderBy(c=>Math.Abs(c.Bounds.Center.X-body.Bounds.Center.X)+Math.Abs(c.Bounds.Center.Y-body.Bounds.Center.Y)).First().Id;
                var ids=world.StaticColliders.Select(c=>c.Id).OrderBy(x=>x).ToArray();int terrain=_pbTerrainId;
                ui.Choice("TERRAIN PAIR ID",terrain.ToString(),ids.Select(x=>x.ToString()).ToArray(),i=>_pbTerrainId=ids[i]);
                ui.Choice("TERRAIN MATERIAL",dynamics.TerrainMaterial(terrain),names,i=>PbEdit(()=>dynamics.SetTerrainMaterial(terrain,names[i])));
                ui.Toggle("IGNORE SELECTED PAIR",dynamics.IsExcluded(id,terrain),()=>PbEdit(()=>dynamics.SetExcluded(id,terrain,!dynamics.IsExcluded(id,terrain))));
            }
            ui.Add("VELOCITY",$"{body.Velocity.X:F2}, {body.Velocity.Y:F2}",null);
        }
        ui.Choice("CHARACTER PUSH FORCE",world.CharacterPushForce.ToString("F0"),new[]{"0","30","60","120","240"},i=>PbEdit(()=>world.SetCharacterPushForce(new[]{0d,30,60,120,240}[i])));
        ui.Add("CHAR PUSH REQUESTS / IMPACTS",$"{dynamics.CharacterPushes} / {dynamics.CharacterBoundaryImpacts}",null);
        ui.Add("CHARACTER / PROP CONTACTS","STAGED / NO MOVING SUPPORT",null);
        ui.Add("STATUS",_pbNotice,null);
        var materials=dynamics.Materials;var materialNames=materials.Materials.Keys.ToArray();
        if(!materials.Materials.ContainsKey(_bodyLabFirst)) _bodyLabFirst="Default";
        if(!materials.Materials.ContainsKey(_bodyLabSecond)) _bodyLabSecond="Default";
        ui.Choice("PREVIEW MATERIAL A",_bodyLabFirst,materialNames,i=>_bodyLabFirst=materialNames[i]);
        ui.Choice("PREVIEW MATERIAL B",_bodyLabSecond,materialNames,i=>_bodyLabSecond=materialNames[i]);
        var contact=materials.ResolveContact(_bodyLabFirst,_bodyLabSecond);
        ui.Add("STATIC / SLIDING FRICTION",$"{contact.StaticFriction:F3} / {contact.DynamicFriction:F3}",null);
        ui.Add("RESTITUTION",contact.Restitution.ToString("F3"),null);
    }
    private void DrawPbBodies(Camera2DView camera,WorldSession world)
    {
        _pbVisible=0;_pbVisualTargetVisible=false;
        if(world.DynamicBodies is not DynamicBodyWorld dynamics||_spriteBatch is null||_pixel is null) return;
        foreach(var b in dynamics.Bodies)
        {
            var q=b.Bounds;
            if(q.Right<camera.Left||q.Left>camera.Left+camera.VisibleWidth||q.Top<camera.Bottom||q.Bottom>camera.Bottom+camera.VisibleHeight) continue;
            var r=camera.WorldRect(q);_pbVisible++;if(b.Id.LocalId==PbVisualTargetId)_pbVisualTargetVisible=true;
            var color=b.Definition.MaterialId switch {"Rubber"=>Color.IndianRed,"Ice"=>Color.LightSkyBlue,"Metal"=>Color.SlateGray,"Stone"=>Color.Gray,_=>Color.SandyBrown};
            _spriteBatch.Draw(_pixel,new Rectangle(r.X,r.Y,r.Width,r.Height),color);
            _spriteBatch.Draw(_pixel,new Rectangle(r.X+2,r.Y+2,Math.Max(1,r.Width-4),Math.Max(1,r.Height-4)),color*.65f);
            if(!AutomatedLaunch&&b.Id.LocalId==_pbSelected) _spriteBatch.Draw(_pixel,new Rectangle(r.X,r.Y,r.Width,2),Color.Yellow);
        }
    }
    private string PbBodySignature()=>_worldClockTest?.DynamicBodies is DynamicBodyWorld d
        ?string.Join(";",d.Bodies.Select(b=>FormattableString.Invariant($"{b.Id.LocalId}:{b.Bounds.Left:R},{b.Bounds.Bottom:R},{b.Velocity.X:R},{b.Velocity.Y:R}"))):"";
}
