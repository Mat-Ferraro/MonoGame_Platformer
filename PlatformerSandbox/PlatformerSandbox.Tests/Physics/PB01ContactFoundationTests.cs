using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Physics;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Foundation.Spatial;

namespace PlatformerSandbox.Tests.Physics;

public sealed class PB01ContactFoundationTests
{
    private static PhysicalBodyId Id(PhysicalBodyKind kind,int n)=>new(kind,n);
    private static Aabb Box(double x,double y)=>new(new(x,y),new(x+1,y+1));
    private static PhysicalBodyId Wall=>Id(PhysicalBodyKind.Static,1);
    private static PhysicalBodyId Pusher=>Id(PhysicalBodyKind.KinematicPlatform,1);
    private static PhysicalBodyId Player(int n)=>Id(PhysicalBodyKind.KinematicCharacter,n);
    private static PhysicalBodyId Crate(int n)=>Id(PhysicalBodyKind.Dynamic,n);

    [Fact]
    public void TypedPairExclusionsAreSymmetricAndDoNotAliasKinds()
    {
        var set=new BodyPairExclusions();var a=new PhysicalBodyPair(Player(1),Crate(1));
        set.Set(a,true);Assert.True(set.Contains(new(Crate(1),Player(1))));
        Assert.False(set.Contains(new(Wall,Crate(1))));Assert.True(set.Has(Player(1)));
        set.Set(new(Wall,Crate(1)),true);set.Remove(Player(1));
        Assert.False(set.Contains(a));Assert.True(set.Contains(new(Wall,Crate(1))));
        Assert.Throws<ArgumentException>(()=>new PhysicalBodyPair(Player(1),Player(1)));
    }

    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void RelativeSweepReportsBothWorldSpaceImpactBounds(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var a=new PhysicalBodySnapshot(Crate(1),Box(0,0),new(3,0));
        var b=new PhysicalBodySnapshot(Crate(2),Box(5,0),new(-1,0));
        var h=PhysicalBodySweep.Linear(a,new(6,0),b,new(-2,0));Assert.NotNull(h);
        Assert.Equal(.5,h!.Value.TimeOfImpact,12);Assert.Equal(new Vector2D(-1,0),h.Value.Normal);
        Assert.Equal(Box(3,0),h.Value.BoundsA);Assert.Equal(Box(4,0),h.Value.BoundsB);
        Assert.Null(PhysicalBodySweep.Linear(a,new(6,0),b,new(6,0)));
    }

    [Fact]
    public void StaticFloorDoesNotMergeIndependentContactIslands()
    {
        var edges=new PhysicalBodyPair[]{new(Wall,Player(1)),new(Wall,Player(2)),new(Player(1),Crate(1)),new(Crate(1),Pusher)};
        var islands=BodyContactGraph.Build(edges);Assert.Equal(2,islands.Count);
        Assert.Equal(new[]{Player(1),Crate(1)},islands[0].Participants.ToArray());
        Assert.Equal(new[]{Player(2)},islands[1].Participants.ToArray());
        var reverse=BodyContactGraph.Build(edges.Reverse());
        for(int i=0;i<islands.Count;i++){Assert.Equal(islands[i].Participants.ToArray(),reverse[i].Participants.ToArray());Assert.Equal(islands[i].Edges.ToArray(),reverse[i].Edges.ToArray());}
        Assert.True(BodyContactGraph.TouchingFaces(Box(0,0),Box(1,0)));
        Assert.False(BodyContactGraph.TouchingFaces(Box(0,0),Box(1,1)));
        Assert.False(BodyContactGraph.TouchingFaces(Box(0,0),Box(.5,0)));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(10)] [InlineData(100)]
    public void CompressionPropagatesThroughArbitraryMixedChains(int length)
    {
        var ids=Enumerable.Range(1,length).Select(i=>i%2==0?Crate(i):Player(i)).ToArray();
        var chain=new[]{Pusher}.Concat(ids).Append(Wall).ToArray();
        var constraints=Enumerable.Range(0,chain.Length-1).Select(i=>new AxisContactConstraint(chain[i],chain[i+1],PhysicalContactAxis.X)).ToList();
        // A body on top shares a contact island, not the horizontal compression path.
        constraints.Add(new(ids[0],Player(1001),PhysicalContactAxis.Y));
        var anchors=new Dictionary<PhysicalBodyId,Vector2D>{{Wall,Vector2D.Zero},{Pusher,new(2,0)}};
        var conflicts=CompressionConstraintGraph.Find(constraints,anchors);
        Assert.Equal(length,conflicts.Count);Assert.All(conflicts,c=>{Assert.Equal(Pusher,c.LowerAnchor);Assert.Equal(Wall,c.UpperAnchor);Assert.Equal(2,c.MinimumVelocity);Assert.Equal(0,c.MaximumVelocity);});
        Assert.Equal(conflicts.ToArray(),CompressionConstraintGraph.Find(constraints.AsEnumerable().Reverse(),anchors).ToArray());
        anchors[Pusher]=Vector2D.Zero;Assert.Empty(CompressionConstraintGraph.Find(constraints,anchors));
        anchors[Pusher]=new(2,0);constraints.RemoveAt(chain.Length-2);Assert.Empty(CompressionConstraintGraph.Find(constraints,anchors));
    }

    [Fact]
    public void VerticalCompressionIsIndependentOfHorizontalConstraints()
    {
        var constraints=new[]{AxisContactConstraint.FromNormal(Player(1),Pusher,new(0,-1)),AxisContactConstraint.FromNormal(Player(1),Crate(1),new(0,1)),AxisContactConstraint.FromNormal(Crate(1),Wall,new(0,1))};
        var result=CompressionConstraintGraph.Find(constraints,new Dictionary<PhysicalBodyId,Vector2D>{{Wall,Vector2D.Zero},{Pusher,new(0,-3)}});
        Assert.Equal(2,result.Count);Assert.All(result,c=>{Assert.Equal(PhysicalContactAxis.Y,c.Axis);Assert.Equal(Wall,c.LowerAnchor);Assert.Equal(Pusher,c.UpperAnchor);});
    }

    [Fact]
    public void RuntimePlaybackControlsPreservePositionAndDoNotMutateAuthoredPath()
    {
        var authored=new MovingPlatformTrajectory(7,Box(0,0),new(4,0),2);
        var runtime=authored.CreateRuntimeCopy();var independent=authored.CreateRuntimeCopy();
        var at=runtime.Evaluate(.5);runtime.ApplyPlayback(PlatformPlaybackCommand.Pause,.5);
        Assert.Equal(at.Bounds,runtime.Evaluate(.5).Bounds);Assert.Equal(at.Bounds,runtime.Evaluate(10).Bounds);
        Assert.Equal(Vector2D.Zero,runtime.Evaluate(1).Velocity);Assert.True(double.IsPositiveInfinity(runtime.TimeUntilNextTurn(1)));
        Assert.Equal(authored.Evaluate(1),independent.Evaluate(1));Assert.NotEqual(runtime.Evaluate(1).Bounds,authored.Evaluate(1).Bounds);
        runtime.ApplyPlayback(PlatformPlaybackCommand.Resume,10);Assert.Equal(at.Bounds,runtime.Evaluate(10).Bounds);
        Assert.Equal(Box(2,0),runtime.Evaluate(10.5).Bounds);
        runtime.ApplyPlayback(PlatformPlaybackCommand.Reverse,10.5);
        Assert.Equal(Box(2,0),runtime.Evaluate(10.5).Bounds);Assert.Equal(new Vector2D(-2,0),runtime.Evaluate(10.5).Velocity);
        Assert.Equal(Box(1,0),runtime.Evaluate(11).Bounds);Assert.Equal(Box(0,0),runtime.Evaluate(11.5).Bounds);
        Assert.Equal(.25,runtime.TimeUntilNextTurn(.25),12);Assert.Equal(9d,runtime.TimeUntilNextTurn(1),12);
        Assert.Throws<InvalidOperationException>(()=>authored.ApplyPlayback(PlatformPlaybackCommand.Pause,0));
    }
}
