using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Exact screen-space mask inputs after owner/layer routing. No color or
/// intensity: these do not affect visibility. The key owns immutable hull copies.</summary>
public sealed class SharedVisibilityKey
{
    private readonly LightingViewport _view;
    private readonly RadialLight _light;
    private readonly SurfaceReceiverProjection _receiver;
    private readonly int _samples;
    private readonly bool _litFaces;
    private readonly float _softness, _emitter;
    private readonly bool _contact;
    private readonly (LightPoint[] Vertices, bool Exit)[] _hulls;
    public SharedVisibilityKey(LightingViewport view, RadialLight light,
        SurfaceReceiverProjection receiver, int samples, bool litFaces, float softness,
        bool contact, float emitter, IReadOnlyList<LightingOccluder> blockers)
    {
        _view=view; _light=light; _receiver=receiver; _samples=samples;
        _litFaces=litFaces; _softness=softness; _contact=contact; _emitter=emitter;
        _hulls=blockers.Select(h => (h.CaptureValidatedVertices(), litFaces && ReceiverWallShadowPolicy.StartsAtExit(light,h))).ToArray();
    }
    public bool Matches(LightingViewport view, RadialLight light,
        SurfaceReceiverProjection receiver, int samples, bool litFaces, float softness,
        bool contact, float emitter, IReadOnlyList<LightingOccluder> blockers)
    {
        if (_view!=view || _receiver!=receiver || _samples!=samples || _litFaces!=litFaces ||
            _softness!=softness || _contact!=contact || _emitter!=emitter ||
            _light.Position!=light.Position || _light.Radius!=light.Radius ||
            _light.Shape!=light.Shape || _light.Direction!=light.Direction ||
            _light.DirectionalShadowLength!=light.DirectionalShadowLength ||
            _light.VirtualZ!=light.VirtualZ || _hulls.Length!=blockers.Count) return false;
        for(int i=0;i<_hulls.Length;i++)
            if (_hulls[i].Exit!=(litFaces && ReceiverWallShadowPolicy.StartsAtExit(light,blockers[i])) ||
                !_hulls[i].Vertices.AsSpan().SequenceEqual(blockers[i].Vertices)) return false;
        return true;
    }
}
