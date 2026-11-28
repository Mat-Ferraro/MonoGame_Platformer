using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Simulation.Physics;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Tests.Physics;

public sealed class PB01DynamicBodyTests
{
    private static PhysicsMaterialCatalog Catalog()=>new(new[]{new PhysicsMaterial("Default",.5,.4,0,1,0),new PhysicsMaterial("Rubber",0,0,1,1,0),new PhysicsMaterial("Ice",0,0,0,1,0)});
    private static PhysicalBodyDefinition Def(int id=1,double mass=1,string material="Default")=>new(new(PhysicalBodyKind.Dynamic,id),new(.5,.5),material,mass);
    private static StaticAabbCollider Floor=>new(1,new(new(-100,-1),new(100,0)));
    [Fact]
    public void ForceAndImpulseHaveMassDependentVelocityAndAreConsumedOnlyOnce()
    {
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        w.Add(Def(1,2),new DynamicBodySettings{GravityScale=0},new(0,0));w.ApplyForce(1,new(4,0));w.ApplyImpulse(1,new(2,0));
        w.Advance(.5,-10);Assert.Equal(2,w.Get(1).Velocity.X,12);Assert.Equal(1,w.Get(1).Bounds.Center.X,12);
        w.Advance(.5,-10);Assert.Equal(2,w.Get(1).Velocity.X,12);Assert.Equal(2,w.Get(1).Bounds.Center.X,12);
    }
    [Fact]
    public void GravityScaleAndDampingAreIndependentOfCharacterMovement()
    {
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        w.Add(Def(),new DynamicBodySettings{GravityScale=2},new(0,10));w.Advance(.1,-10);
        Assert.Equal(-2,w.Get(1).Velocity.Y,12);Assert.Equal(9.8,w.Get(1).Bounds.Center.Y,12);
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void HighSpeedSweepStopsAtThinWall(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var wall=new StaticAabbCollider(5,new(new(3,-10),new(3.01,10)));
        var w=new DynamicBodyWorld(new[]{wall},Catalog());w.Add(Def(),new DynamicBodySettings{GravityScale=0,MaxSpeed=1000},new(0,0),new(100,0));
        w.Advance(.1,-10);Assert.Equal(3,w.Get(1).Bounds.Right);Assert.Equal(0,w.Get(1).Velocity.X);Assert.Equal(1L,w.Metrics.Contacts);
    }
    [Fact]
    public void RestingFrictionAndRestitutionAreApplied()
    {
        var w=new DynamicBodyWorld(new[]{Floor},Catalog());
        w.Add(Def(),new(),new(0,.5),new(.02,0));w.Advance(.1,-10);Assert.Equal(Vector2D.Zero,w.Get(1).Velocity);
        w.Add(Def(2,1,"Rubber"),new(),new(5,2),new(0,-10));w.Advance(.2,-10);
        Assert.True(w.Get(2).Velocity.Y>0);Assert.True(w.Get(2).Bounds.Bottom>=0);
        for(int i=0;i<600;i++)w.Advance(1d/60,-10);
        Assert.Equal(0,w.Get(1).Bounds.Bottom);Assert.Equal(Vector2D.Zero,w.Get(1).Velocity);
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference,60,60d,1d)]
    [InlineData(SpatialQueryMode.Indexed,60,60d,1d)]
    [InlineData(SpatialQueryMode.Verify,60,60d,1d)]
    [InlineData(SpatialQueryMode.Reference,30,60d,1d)]
    [InlineData(SpatialQueryMode.Reference,120,120d,2d)]
    public void RestingRubberDoesNotBounceFromGravityEachTick(SpatialQueryMode mode,int hz,double gravity,double scale)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var w=new DynamicBodyWorld(new[]{Floor},Catalog());
        w.Add(Def(1,1,"Rubber"),new DynamicBodySettings{GravityScale=scale},new(0,.5));
        for(int i=0;i<120;i++)
        {
            w.Advance(1d/hz,-gravity);
            Assert.Equal(0d,w.Get(1).Bounds.Bottom);
            Assert.Equal(Vector2D.Zero,w.Get(1).Velocity);
        }
        w.ApplyImpulse(1,new(0,20));w.Advance(1d/hz,-gravity);
        Assert.True(w.Get(1).Velocity.Y>0);
        Assert.True(w.Get(1).Bounds.Bottom>0);
    }
    [Theory]
    [InlineData(30,3.5)] [InlineData(60,3.5)] [InlineData(120,3.5)]
    [InlineData(30,10d)] [InlineData(60,10d)] [InlineData(120,10d)]
    public void AuthoredRubberBouncesThenSettlesWithoutMicroBouncing(int hz,double dropHeight)
    {
        var materials=new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",.5,.4,0,1,0),new PhysicsMaterial("Rubber",.9,.75,.8,1.1,0)});
        var w=new DynamicBodyWorld(new[]{Floor},materials);
        w.Add(Def(1,1,"Rubber"),new(),new(0,dropHeight+.5));
        bool bounced=false;
        for(int i=0;i<20*hz;i++)
        {
            w.Advance(1d/hz,-60);
            var body=w.Get(1);
            Assert.True(body.Bounds.Bottom>=0);
            bounced|=body.Velocity.Y>0;
        }
        Assert.True(bounced);
        for(int i=0;i<2*hz;i++)
        {
            w.Advance(1d/hz,-60);
            Assert.Equal(0d,w.Get(1).Bounds.Bottom);
            Assert.Equal(Vector2D.Zero,w.Get(1).Velocity);
        }
    }
    [Fact]
    public void SustainedForceDoesNotBounceRestingBodyAgainstWall()
    {
        var wall=new StaticAabbCollider(5,new(new(1,-10),new(2,10)));
        var w=new DynamicBodyWorld(new[]{wall},Catalog());
        w.Add(Def(1,1,"Rubber"),new DynamicBodySettings{GravityScale=0},new(.5,0));
        for(int i=0;i<120;i++)
        {
            w.ApplyForce(1,new(120,0));w.Advance(1d/60,-60);
            Assert.Equal(1d,w.Get(1).Bounds.Right);
            Assert.Equal(Vector2D.Zero,w.Get(1).Velocity);
        }
        w.ApplyImpulse(1,new(-5,0));w.Advance(1d/60,-60);
        Assert.True(w.Get(1).Velocity.X<0);
    }
    [Fact]
    public void BounceCutoffPreservesTangentialMotionAndIsInclusive()
    {
        var w=new DynamicBodyWorld(new[]{Floor},Catalog());
        w.Add(Def(1,1,"Rubber"),new DynamicBodySettings{GravityScale=0,BounceThreshold=1},new(0,.5),new(3,-1));
        w.Advance(.1,0);
        Assert.Equal(0d,w.Get(1).Bounds.Bottom);
        Assert.Equal(0d,w.Get(1).Velocity.Y);
        Assert.Equal(3d,w.Get(1).Velocity.X);
        Assert.Equal(.3,w.Get(1).Bounds.Center.X,12);
        w.ApplyImpulse(1,new(0,-5));w.Advance(.1,0);
        Assert.Equal(5d,w.Get(1).Velocity.Y);
        Assert.True(w.Get(1).Bounds.Bottom>0);
    }
    [Fact]
    public void PairFilteringIsSymmetricScopedAndRestorationIsSafe()
    {
        Assert.True(BodyCollisionFilter.DynamicTerrain.Allows(BodyCollisionFilter.Terrain));
        Assert.False(BodyCollisionFilter.Terrain.Allows(new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.None)));
        var w=new DynamicBodyWorld(new[]{Floor},Catalog());w.Add(Def(),new(),new(0,.5));w.Add(Def(2),new(),new(3,.5));
        w.SetExcluded(1,1,true);w.Advance(.1,-10);
        Assert.True(w.Get(1).Bounds.Bottom<0);Assert.Equal(0,w.Get(2).Bounds.Bottom);
        Assert.Throws<InvalidOperationException>(()=>w.SetExcluded(1,1,false));Assert.True(w.IsExcluded(1,1));
        w.Remove(1);w.Add(Def(),new(),new(0,.5));Assert.False(w.IsExcluded(1,1));
        w.Configure(1,"Default",1,new DynamicBodySettings{Filter=new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.All)});
        Assert.True(DynamicBodyWorld.PlatformSolid(w.Get(1)));
    }
    [Fact]
    public void TerrainMaterialCanBeAuthoredOrEditedAndChangesPhysicalResponse()
    {
        var w=new DynamicBodyWorld(new[]{Floor},Catalog(),new Dictionary<int,string>{{1,"Rubber"}});
        Assert.Equal("Rubber",w.TerrainMaterial(1));
        w.Add(Def(),new(),new(0,2),new(0,-10));w.Advance(.2,-10);
        Assert.True(w.Get(1).Velocity.Y>0);
        w.SetTerrainMaterial(1,"Default");Assert.Equal("Default",w.TerrainMaterial(1));
        Assert.Throws<ArgumentException>(()=>w.SetTerrainMaterial(1,"Missing"));Assert.Equal("Default",w.TerrainMaterial(1));
    }

    [Fact]
    public void InvalidSpawnAndInvalidMaterialReloadAreRejectedWithoutMutation()
    {
        var w=new DynamicBodyWorld(new[]{Floor},Catalog());Assert.Throws<ArgumentException>(()=>w.Add(Def(),new(),new(0,0)));
        w.Add(Def(1,1,"Rubber"),new(),new(0,1));var prior=w.Get(1);var catalog=w.Materials;
        Assert.Throws<ArgumentException>(()=>w.ReloadMaterials(new PhysicsMaterialCatalog(new[]{catalog.Require("Default")})));
        Assert.Same(catalog,w.Materials);Assert.Equal(prior,w.Get(1));
    }
    [Fact]
    public void DynamicReplayIsIndependentOfRegistrationOrder()
    {
        var a=new DynamicBodyWorld(new[]{Floor},Catalog());var b=new DynamicBodyWorld(new[]{Floor},Catalog());
        foreach(int id in new[]{1,2,3})a.Add(Def(id),new(),new(id*2,3));
        foreach(int id in new[]{3,1,2})b.Add(Def(id),new(),new(id*2,3));
        for(int i=0;i<300;i++) {a.Advance(1d/60,-10);b.Advance(1d/60,-10);Assert.Equal(a.Bodies.Select(x=>x.Snapshot).ToArray(),b.Bodies.Select(x=>x.Snapshot).ToArray());}
        Assert.Equal(a.Metrics,b.Metrics);
    }
    [Fact]
    public void DampingAndSpeedCapAreExplicitAndFailedAdvanceIsAtomic()
    {
        var materials=new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",0,0,0,1,2)});
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),materials);
        w.Add(Def(),new DynamicBodySettings{GravityScale=0,MaxSpeed=3},new(0,0),new(10,0));
        w.Advance(.5,-10);Assert.Equal(3d,w.Get(1).Velocity.X);
        w.Add(Def(2),new DynamicBodySettings{GravityScale=double.MaxValue},new(10,10));
        var before=w.Bodies.ToArray();var metrics=w.Metrics;
        Assert.Throws<ArgumentOutOfRangeException>(()=>w.Advance(.1,-10));
        Assert.Equal(before,w.Bodies.ToArray());Assert.Equal(metrics,w.Metrics);
    }
}
