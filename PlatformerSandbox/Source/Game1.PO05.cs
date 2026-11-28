using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Foundation.Presentation;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Presentation;

namespace PlatformerSandbox;

public sealed partial class Game1
{
    private bool IsPhysicsLab=>_ldtkLevel?.World.LevelId==GameLevelIds.PhysicsInteractionLab;
    private bool IsSpatialLab=>_ldtkLevel?.World.LevelId==GameLevelIds.EngineScalabilityLab || IsPhysicsLab;
    private bool _spatialGrid=true,_spatialQuery=true;
    private int _spatialRadius=4,_spatialCameraAxes;
    private int _spatialMatches;
    private string _spatialVisualCamera="";
    private void BuildSpatialControls(InteractiveControlPanel ui)
    {
        ui.CompactAction=null;
        if(IsPhysicsLab) BuildBodyFoundationControls(ui);
        ui.Add("ENABLE ALL SPATIAL OPTIMIZATIONS","INDEXED",()=>{SpatialQueryPolicy.ResetLab();SpatialQueryPolicy.LabMode=SpatialQueryMode.Indexed;},SpatialQueryPolicy.Current==SpatialQueryMode.Indexed);
        ui.Add("REFERENCE MODE","LINEAR",()=>SpatialQueryPolicy.LabMode=SpatialQueryMode.Reference,SpatialQueryPolicy.Current==SpatialQueryMode.Reference);
        ui.Add("VERIFY MODE","COMPARE",()=>SpatialQueryPolicy.LabMode=SpatialQueryMode.Verify,SpatialQueryPolicy.Current==SpatialQueryMode.Verify);
        ui.Toggle("STATIC COLLISION QUERIES",SpatialQueryPolicy.LabCollision,()=>SpatialQueryPolicy.LabCollision=!SpatialQueryPolicy.LabCollision);
        ui.Toggle("RENDER VISIBILITY QUERIES",SpatialQueryPolicy.LabRendering,()=>SpatialQueryPolicy.LabRendering=!SpatialQueryPolicy.LabRendering);
        ui.Toggle("TRIGGER QUERIES",SpatialQueryPolicy.LabTriggers,()=>SpatialQueryPolicy.LabTriggers=!SpatialQueryPolicy.LabTriggers);
        ui.Add("EFFECTIVE MODE",SpatialQueryPolicy.Current.ToString(),null);
        ui.Add("STATIC COLLIDERS",(_worldClockTest?.StaticColliders.Count??0).ToString(),null);
        ui.Add("MOVING PLATFORMS",(_worldClockTest?.MovingPlatforms.Count??0).ToString(),null);
        ui.Add("COLLISION CELL SIZE","4 WORLD UNITS",null);
        ui.Add("MOVING COLLISION PATH","REFERENCE",null);
        var cameraModes=new[]{"FOLLOW PLAYER","HORIZONTAL SWEEP","VERTICAL SWEEP","BOTH AXES"};
        ui.Choice("CAMERA MODE",cameraModes[_spatialCameraAxes],cameraModes,i=>_spatialCameraAxes=i);
        ui.Add("GRID OVERLAY",On(_spatialGrid),()=>_spatialGrid=!_spatialGrid,_spatialGrid);
        ui.Add("QUERY OVERLAY",On(_spatialQuery),()=>_spatialQuery=!_spatialQuery,_spatialQuery);
        ui.Choice("QUERY RADIUS",_spatialRadius.ToString(),new[]{"2","4","8","16"},i=>_spatialRadius=new[]{2,4,8,16}[i]);
        ui.Add("QUERY MATCHES",_spatialMatches.ToString(),null);
        var stats=SpatialCollisionDiagnostics.Snapshot;
        ui.Add("SPATIAL QUERIES (PROCESS)",stats.Queries.ToString(),null);
        ui.Add("EXAMINED / MATCHES",$"{stats.Examined} / {stats.Matches}",null);
        ui.Add("EXPORT DIAGNOSTIC REPORT",_diagnostics is null?"UNAVAILABLE":"EXPORT ZIP",_diagnostics is null?null:()=>_diagnostics.ExportReport());
        ui.Add("RESET LAB","REFERENCE",()=>{SpatialQueryPolicy.ResetLab();SpatialQueryPolicy.LabMode=SpatialQueryMode.Reference;_spatialGrid=true;_spatialQuery=true;_spatialRadius=4;_spatialCameraAxes=0;});
    }
    private void DrawSpatialOverlay(Camera2DView camera,WorldSession world)
    {
        if(_spriteBatch is null||_pixel is null) return;
        int w=camera.ViewportWidth,h=camera.ViewportHeight;
        if(_spatialGrid)
        {
            const double size=4;
            for(double x=Math.Ceiling(camera.Left/size)*size;x<=camera.Left+camera.VisibleWidth;x+=size)
                _spriteBatch.Draw(_pixel,new Rectangle((int)Math.Round((x-camera.Left)*camera.PixelsPerWorldUnit),0,1,h),Color.White*.22f);
            for(double y=Math.Ceiling(camera.Bottom/size)*size;y<=camera.Bottom+camera.VisibleHeight;y+=size)
                _spriteBatch.Draw(_pixel,new Rectangle(0,(int)Math.Round(h-(y-camera.Bottom)*camera.PixelsPerWorldUnit),w,1),Color.White*.22f);
        }
        if(!_spatialQuery) return;
        var center=world.GetPlayer(1).Bounds.Center;
        var bounds=new SpatialBounds(center.X-_spatialRadius,center.Y-_spatialRadius,center.X+_spatialRadius,center.Y+_spatialRadius);
        var matches=world.CaptureSpatialSnapshot().Query(bounds);
        _spatialMatches=matches.Count;
        void Outline(Aabb box,Color color)
        {
            var r=camera.WorldRect(box);
            _spriteBatch.Draw(_pixel,new Rectangle(r.X,r.Y,r.Width,2),color);
            _spriteBatch.Draw(_pixel,new Rectangle(r.X,r.Y+r.Height-2,r.Width,2),color);
            _spriteBatch.Draw(_pixel,new Rectangle(r.X,r.Y,2,r.Height),color);
            _spriteBatch.Draw(_pixel,new Rectangle(r.X+r.Width-2,r.Y,2,r.Height),color);
        }
        Outline(new Aabb(new Vector2D(bounds.Left,bounds.Bottom),new Vector2D(bounds.Right,bounds.Top)),Color.Yellow);
        foreach(var match in matches) Outline(match.Bounds,match.Kind==WorldSpatialKind.StaticCollider?Color.Orange:Color.Cyan);
    }
}
