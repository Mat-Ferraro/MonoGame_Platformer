using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L21SharedVisibilityKeyTests
{
    [Fact]
    public void ExactMaskKeyIgnoresColorButRequiresCameraGeometryAndRouting()
    {
        var view = new LightingViewport(1280,720,new LightPoint(0,0),32);
        var light = new RadialLight { Id="source", Radius=10, CastShadows=true };
        var wall = new LightingOccluder { Id="wall", LitFaceLayer=SceneLightingLayerSet.Standard11().Gameplay,
            Vertices=new LightPoint[]{new(2,-1),new(3,-1),new(3,1),new(2,1)} };
        var receiver = new SurfaceReceiverProjection();
        var key = new SharedVisibilityKey(view,light,receiver,128,true,4,true,8,new[]{wall});
        bool Match(LightingViewport v, RadialLight l, SurfaceReceiverProjection r, LightingOccluder[] hulls,
            int samples=128,bool lit=true,float blur=4,bool contact=true,float emitter=8) =>
            key.Matches(v,l,r,samples,lit,blur,contact,emitter,hulls);
        Assert.True(Match(view,light with {Color=new LightRgb(1,0,0),Intensity=.4},receiver,
            new[]{wall with {Vertices=wall.Vertices.ToArray()}}));
        Assert.False(Match(view with {Focus=new LightPoint(0,1)},light,receiver,new[]{wall}));
        Assert.False(Match(view,light with {Position=new LightPoint(0,1)},receiver,new[]{wall}));
        Assert.False(Match(view,light with {VirtualZ=-1},receiver,new[]{wall}));
        Assert.False(Match(view,light,receiver,new[]{wall with {LitFaceDepthZ=1}}));
        Assert.False(Match(view,light,receiver,new LightingOccluder[0])); // owner exclusion changes routed set
        Assert.False(Match(view,light,receiver,new[]{wall},samples:192));
        Assert.False(Match(view,light,receiver,new[]{wall},lit:false));
        Assert.False(Match(view,light,receiver,new[]{wall},blur:2));
        Assert.False(Match(view,light,receiver,new[]{wall},contact:false));
        Assert.False(Match(view,light,receiver,new[]{wall},emitter:12));
        Assert.False(Match(view,light,receiver with {Mode=ReceiverProjectionMode.Folded},new[]{wall}));
        wall.Vertices[0]=new LightPoint(1,-1);
        Assert.False(Match(view,light,receiver,new[]{wall})); // defensive copy
    }

    [Fact]
    public void DirectionalProjectionReachAndDirectionArePartOfKey()
    {
        var v=new LightingViewport(800,600,new LightPoint(0,0),32);
        var l=new RadialLight {Id="sun",Shape=LightShape.Directional};
        var r=new SurfaceReceiverProjection();
        var empty=new LightingOccluder[0];
        var k=new SharedVisibilityKey(v,l,r,128,false,0,false,0,empty);
        Assert.False(k.Matches(v,l with {DirectionalShadowLength=20},r,128,false,0,false,0,empty));
        Assert.False(k.Matches(v,l with {Direction=new LightPoint(0,-1)},r,128,false,0,false,0,empty));
    }
}
