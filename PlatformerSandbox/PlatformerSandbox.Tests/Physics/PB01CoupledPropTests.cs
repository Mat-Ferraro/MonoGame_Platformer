using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Simulation.Physics;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Tests.Physics;

public sealed class PB01CoupledPropTests
{
    private static PhysicsMaterialCatalog Catalog()=>new(new[]{new PhysicsMaterial("Default",0,0,0,1,0),new PhysicsMaterial("Elastic",0,0,1,1,0)});
    private static DynamicBodySettings Settings(double gravity=0)=>new(){GravityScale=gravity,MaxSpeed=1000,Filter=BodyCollisionFilter.DynamicSolid};
    private static void Add(DynamicBodyWorld world,int id,double x,double y,double vx=0,double vy=0,double mass=1,double gravity=0,string material="Default")=>
        world.Add(new(new(PhysicalBodyKind.Dynamic,id),new(.5,.5),material,mass),Settings(gravity),new(x,y),new(vx,vy));
    private static StaticAabbCollider Floor=>new(1,new(new(-100,-1),new(100,0)));
    private static void NoOverlap(DynamicBodyWorld world)
    {
        var bodies=world.Bodies;for(int i=0;i<bodies.Count;i++)for(int j=i+1;j<bodies.Count;j++)
            Assert.False(AabbCollisionDetector.Overlaps(bodies[i].Bounds,bodies[j].Bounds));
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void FastRelativeCollisionTransfersMomentumWithoutTunnelling(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        Add(w,1,0,0,100,mass:1);Add(w,2,5,0,-100,mass:3);
        w.Advance(.1,0);NoOverlap(w);Assert.Equal(-50,w.Get(1).Velocity.X,10);Assert.Equal(-50,w.Get(2).Velocity.X,10);
        Assert.True(w.PairContacts>0);Assert.Equal(2,w.LargestContactIsland);
    }
    [Fact]
    public void IsolatedElasticCollisionExchangesEqualMassVelocity()
    {
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        Add(w,1,0,0,10,material:"Elastic");Add(w,2,2,0,material:"Elastic");w.Advance(.2,0);
        NoOverlap(w);Assert.Equal(0,w.Get(1).Velocity.X,10);Assert.Equal(10,w.Get(2).Velocity.X,10);
    }
    [Theory]
    [InlineData(2)] [InlineData(10)] [InlineData(50)]
    public void StackSupportsAllBodiesWithoutRestingJitter(int count)
    {
        var w=new DynamicBodyWorld(new[]{Floor},Catalog());
        for(int i=0;i<count;i++)Add(w,i+1,0,i+.5,gravity:1);
        for(int t=0;t<120;t++)
        {
            w.Advance(1d/60,-60);NoOverlap(w);
            for(int i=0;i<count;i++){Assert.Equal(i,w.Get(i+1).Bounds.Bottom,10);Assert.Equal(Vector2D.Zero,w.Get(i+1).Velocity);}
        }
        Assert.Equal(count,w.LargestContactIsland);
        w.ApplyImpulse(1,new(0,count*2));w.Advance(1d/60,-60);NoOverlap(w);
        Assert.True(w.Get(1).Bounds.Bottom>0);Assert.True(w.Get(count).Velocity.Y>0);
    }
    [Fact]
    public void PushingChainAgainstWallStopsWithoutPenetration()
    {
        var wall=new StaticAabbCollider(4,new(new(5,-10),new(6,10)));
        var w=new DynamicBodyWorld(new[]{wall},Catalog());
        for(int i=0;i<5;i++)Add(w,i+1,i+.5,0);
        w.ApplyImpulse(1,new(20,0));w.Advance(.1,0);NoOverlap(w);
        Assert.All(w.Bodies,b=>Assert.Equal(Vector2D.Zero,b.Velocity));Assert.Equal(5,w.Get(5).Bounds.Right);Assert.Equal(5,w.LargestContactIsland);
    }
    [Fact]
    public void PairExclusionRestorationAndMaskChangesAreValidated()
    {
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        Add(w,1,0,0,2);Add(w,2,2,0);w.SetBodyPairExcluded(1,2,true);w.Advance(1,0);
        Assert.True(AabbCollisionDetector.Overlaps(w.Get(1).Bounds,w.Get(2).Bounds));
        Assert.Throws<InvalidOperationException>(()=>w.SetBodyPairExcluded(1,2,false));Assert.True(w.IsBodyPairExcluded(2,1));
        w.Advance(1,0);w.SetBodyPairExcluded(1,2,false);Assert.False(w.IsBodyPairExcluded(2,1));
        Assert.Throws<ArgumentException>(()=>Add(w,3,2,0));
        w.Remove(2);Add(w,2,2,0);Assert.False(w.IsBodyPairExcluded(1,2));
    }
    [Fact]
    public void ContactChainIsIndependentOfRegistrationOrderAndSpatialMode()
    {
        var a=new DynamicBodyWorld(new[]{Floor},Catalog());var b=new DynamicBodyWorld(new[]{Floor},Catalog());
        foreach(int id in new[]{1,2,3})Add(a,id,id-.5,.5,gravity:1);
        foreach(int id in new[]{3,2,1})Add(b,id,id-.5,.5,gravity:1);
        for(int tick=0;tick<120;tick++)
        {
            if(tick%30==0){a.ApplyImpulse(1,new(4,0));b.ApplyImpulse(1,new(4,0));}
            using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference))a.Advance(1d/60,-60);
            using(SpatialQueryPolicy.Use(SpatialQueryMode.Verify))b.Advance(1d/60,-60);
            NoOverlap(a);NoOverlap(b);Assert.Equal(a.Bodies.ToArray(),b.Bodies.ToArray());Assert.Equal(a.PairContacts,b.PairContacts);
            if(tick==0)
            {
                // A floor TOI can win the simultaneous event; the horizontal chain
                // must nevertheless transfer the impulse and report its pair responses.
                foreach(var body in a.Bodies)
                    Assert.Equal(4d/3,body.Velocity.X,10);
                Assert.True(a.PairContacts>0,"The active prop chain must report resolved pair constraints.");
                Assert.True(a.Metrics.Contacts>=a.PairContacts);
            }
        }
        Assert.True(a.PairContacts>0);
    }
    [Fact]
    public void PassiveAdjacentGroundedPropsDoNotReportPairResponses()
    {
        var w=new DynamicBodyWorld(new[]{Floor},Catalog());
        for(int id=1;id<=3;id++)Add(w,id,id-.5,.5,gravity:1);
        for(int tick=0;tick<60;tick++)w.Advance(1d/60,-60);
        NoOverlap(w);
        Assert.All(w.Bodies,b=>Assert.Equal(Vector2D.Zero,b.Velocity));
        Assert.Equal(0L,w.PairContacts);
    }
    [Fact]
    public void CoupledModeRubberSettlesAndCanBounceAgain()
    {
        var mat=new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",.5,.4,0,1,0),new PhysicsMaterial("Rubber",.9,.75,.8,1.1,0)});
        var w=new DynamicBodyWorld(new[]{Floor},mat);Add(w,1,0,4,gravity:1,material:"Rubber");
        bool bounced=false;
        for(int t=0;t<1200;t++){w.Advance(1d/60,-60);bounced|=w.Get(1).Velocity.Y>0;Assert.True(w.Get(1).Bounds.Bottom>=0);}
        Assert.True(bounced);Assert.Equal(0d,w.Get(1).Bounds.Bottom,7);Assert.Equal(Vector2D.Zero,w.Get(1).Velocity);
        w.ApplyImpulse(1,new(0,8));w.Advance(1d/60,-60);Assert.True(w.Get(1).Velocity.Y>0);
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void EveryGroundedSliderReceivesFrictionEvenWhenAnotherBodyWinsTheToi(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var mat=new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",.5,.4,0,1,0),new PhysicsMaterial("Ice",0,0,0,1,0)});
        var w=new DynamicBodyWorld(new[]{Floor},mat);
        Add(w,1,-40,.5,gravity:1); // stationary, first in stable ID order
        Add(w,2,-20,.5,6,gravity:1);
        Add(w,3,20,.5,-6,gravity:1);
        Add(w,4,40,.5,2,gravity:1,material:"Ice");
        Add(w,5,0,10,2); // airborne, gravity disabled to isolate drag
        for(int tick=0;tick<60;tick++)
        {
            w.Advance(1d/60,-60);NoOverlap(w);
            Assert.Equal(w.Get(2).Velocity.X,-w.Get(3).Velocity.X,10);
            Assert.InRange(w.Get(2).Velocity.X,0,6);
            Assert.Equal(2d,w.Get(4).Velocity.X,10);
            Assert.Equal(2d,w.Get(5).Velocity.X,10);
        }
        Assert.Equal(Vector2D.Zero,w.Get(2).Velocity);
        Assert.Equal(Vector2D.Zero,w.Get(3).Velocity);
        Assert.True(w.Get(2).Bounds.Center.X>-20);
        Assert.True(w.Get(3).Bounds.Center.X<20);
        double x=w.Get(2).Bounds.Center.X;
        w.Advance(1d/60,-60);Assert.Equal(x,w.Get(2).Bounds.Center.X);
    }
    [Fact]
    public void ReleasedGroundedChainSettlesTogetherWithoutPenetration()
    {
        var mat=new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",.5,.4,0,1,0)});
        var w=new DynamicBodyWorld(new[]{Floor},mat);
        Add(w,1,-20,.5,gravity:1);
        for(int id=2;id<=4;id++)Add(w,id,id-.5,.5,gravity:1);
        w.ApplyImpulse(2,new(12,0));
        for(int tick=0;tick<120;tick++){w.Advance(1d/60,-60);NoOverlap(w);}
        for(int id=2;id<=4;id++)
        {
            Assert.Equal(Vector2D.Zero,w.Get(id).Velocity);
            Assert.True(w.Get(id).Bounds.Center.X>id-.5);
        }
    }
    [Fact]
    public void FailedCoupledTickDoesNotConsumePendingImpulseOrCommitMetrics()
    {
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());Add(w,1,0,0);Add(w,2,3,0,gravity:double.MaxValue);
        w.ApplyImpulse(1,new(2,0));var before=w.Bodies.ToArray();var metrics=w.Metrics;
        Assert.Throws<ArgumentOutOfRangeException>(()=>w.Advance(.1,-10));Assert.Equal(before,w.Bodies.ToArray());Assert.Equal(metrics,w.Metrics);Assert.Equal(0L,w.PairContacts);
    }
}
