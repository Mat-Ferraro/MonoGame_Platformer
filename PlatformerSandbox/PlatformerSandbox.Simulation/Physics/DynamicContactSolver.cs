using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.Physics;

/// <summary>Shared chronological linear CCD for translational props and static terrain.
/// Inelastic closure merges conflicting normal-velocity groups conservatively; it is not
/// a general rigid-body impulse optimizer. Character motion is staged; platform boundaries are prescribed linear segments.</summary>
internal static class DynamicContactSolver
{
    private const double ContactGap=1e-8;
    private const int EventBudget=512;
    private sealed class Node(DynamicBodyState state, double mass, Vector2D velocity, Vector2D accelerationStep)
    {
        public DynamicBodyState State=state;
        public double Mass=mass;
        public Vector2D V=velocity;
        public Vector2D AccelerationStep=accelerationStep;
        public Aabb Box=state.Bounds;
        public bool XImpact,YImpact;
    }
    private readonly record struct Hit(int A,int B,int Terrain,double Time,Vector2D Normal);
    private readonly record struct Constraint(int Lower,int Upper,bool X);
    internal sealed record Result(SortedDictionary<int,DynamicBodyState> Bodies,long Sweeps,long Contacts,long Pairs,int LargestIsland,long CharacterImpacts,long PlatformImpacts,long EventIterations);
    private static double Dot(Vector2D a,Vector2D b)=>a.X*b.X+a.Y*b.Y;
    internal static Result Advance(IEnumerable<DynamicBodyState> states,IReadOnlyList<StaticAabbCollider> terrain,
        PhysicsMaterialCatalog materials,Func<int,string> surface,Func<DynamicBodyState,StaticAabbCollider,bool> solid,
        Func<DynamicBodyState,DynamicBodyState,bool> pairSolid,double dt,double gravity,IReadOnlySet<int>? characterBoundaries=null, IReadOnlyList<MovingPlatformTrajectory>? platforms=null, double startTime=0, Dictionary<int,List<(double Time,Aabb Bounds)>>? trace=null)
    {
        var nodes=states.Select(s=>{
            double m=s.Definition.ResolveMass(materials)!.Value;
            var acceleration=(s.PendingForce*(1/m)+new Vector2D(0,gravity*s.Settings.GravityScale))*dt;
            var v=(s.Velocity+s.PendingImpulse*(1/m)+acceleration)*Math.Exp(-materials.Require(s.Definition.MaterialId).LinearDamping*dt);
            return new Node(s,m,new(Math.Clamp(v.X,-s.Settings.MaxSpeed,s.Settings.MaxSpeed),Math.Clamp(v.Y,-s.Settings.MaxSpeed,s.Settings.MaxSpeed)),acceleration);
        }).ToArray();
        var solids=nodes.Select(n=>SpatialColliderSet.Wrap(terrain.Where(c=>solid(n.State,c)).ToArray())).ToArray();
        var paths=nodes.Any(n=>DynamicBodyWorld.PlatformSolid(n.State))?(platforms??Array.Empty<MovingPlatformTrajectory>()).OrderBy(p=>p.Id).ToArray():Array.Empty<MovingPlatformTrajectory>();
        bool PlatformSolid(int a)=>DynamicBodyWorld.PlatformSolid(nodes[a].State);
        var pairs=new List<(int A,int B)>();
        for(int a=0;a<nodes.Length;a++)for(int b=a+1;b<nodes.Length;b++)
            if(pairSolid(nodes[a].State,nodes[b].State))pairs.Add((a,b));
        bool Overlap(Aabb[] boxes,double time)
        {
            for(int a=0;a<nodes.Length;a++)if(solids[a].Any(c=>AabbCollisionDetector.Overlaps(boxes[a],c.Bounds)))return true;
            for(int a=0;a<nodes.Length;a++)foreach(var path in paths)
                if(PlatformSolid(a)&&AabbCollisionDetector.Overlaps(boxes[a],path.Evaluate(time).Bounds))return true;
            return pairs.Any(p=>AabbCollisionDetector.Overlaps(boxes[p.A],boxes[p.B]));
        }
        if(Overlap(nodes.Select(n=>n.Box).ToArray(),startTime))
        {
            // Keep the strict initial-state invariant, but identify the actual
            // pair so a future fault distinguishes a staged character boundary
            // from a static collider, platform, or another dynamic body.
            string Describe(Aabb box)=>FormattableString.Invariant($"[{box.Left:R},{box.Bottom:R} -> {box.Right:R},{box.Top:R}]");
            string DescribeNode(int index)=>$"dynamic {nodes[index].State.Id.LocalId} {Describe(nodes[index].Box)}";
            InvalidOperationException Failure(string pair)=>new(FormattableString.Invariant(
                $"Initial coupled body penetration. Time={startTime:R}; {pair}."));
            for(int a=0;a<nodes.Length;a++)foreach(var collider in solids[a])
                if(AabbCollisionDetector.Overlaps(nodes[a].Box,collider.Bounds))
                    throw Failure($"{DescribeNode(a)} vs {((characterBoundaries?.Contains(collider.Id)??false)?"character boundary":"terrain")} {collider.Id} {Describe(collider.Bounds)}");
            for(int a=0;a<nodes.Length;a++)foreach(var path in paths)
            {
                var bounds=path.Evaluate(startTime).Bounds;
                if(PlatformSolid(a)&&AabbCollisionDetector.Overlaps(nodes[a].Box,bounds))
                    throw Failure($"{DescribeNode(a)} vs platform {path.Id} {Describe(bounds)}");
            }
            foreach(var pair in pairs)
                if(AabbCollisionDetector.Overlaps(nodes[pair.A].Box,nodes[pair.B].Box))
                    throw Failure($"{DescribeNode(pair.A)} vs {DescribeNode(pair.B)}");
            throw Failure("unidentified pair");
        }
        void Record(double at)
        {
            if(trace is null)return;
            foreach(var node in nodes)
            {
                if(!trace.TryGetValue(node.State.Id.LocalId,out var points))continue;
                if(points.Count>0&&points[^1].Time==at)points[^1]=(at,node.Box);
                else points.Add((at,node.Box));
            }
        }
        Record(startTime);
        long sweeps=0,contacts=0,pairContacts=0,characterImpacts=0,platformImpacts=0,eventIterations=0; int largestIsland=0;
        double remaining=dt;
        // Use one absolute clock for moving boundaries. Reconstructing it from
        // dt-remaining can leave a sub-ULP residual or sample a different end
        // face than the caller's authoritative startTime+dt.
        double time=startTime, endTime=startTime+dt;
        if(paths.Length>0&&(!double.IsFinite(endTime)||endTime<=startTime))
            throw new InvalidOperationException("Platform tick cannot advance time.");
        for(int step=0;remaining>0;step++)
        {
            if(step==EventBudget)throw new InvalidOperationException("Coupled dynamic event budget exhausted.");
            eventIterations++;
            double horizon=remaining, segmentEnd=endTime;
            if(paths.Length>0)
            {
                foreach(var path in paths)
                {
                    double turn=path.TimeUntilNextTurn(time);
                    double boundary=time+turn;
                    // A positive event duration may round to the current time.
                    // The trajectory already snaps analytical turns within a few
                    // ULPs. Advance to the next representable instant, never
                    // beyond the tick end; still perform all collision sweeps.
                    if(boundary<=time)boundary=Math.BitIncrement(time);
                    segmentEnd=Math.Min(segmentEnd,boundary);
                }
                horizon=segmentEnd-time;
            }
            if(horizon<=0)throw new InvalidOperationException("Platform segment cannot advance time.");
            var samples=paths.ToDictionary(p=>p.Id,p=>p.Evaluate(time));
            Hit? best=null;
            void Consider(Hit h){if(best is null||h.Time<best.Value.Time)best=h;}
            for(int a=0;a<nodes.Length;a++)
            {
                var n=nodes[a];if(n.V==Vector2D.Zero)continue;
                sweeps++;var h=AabbCollisionDetector.SweepFirst(n.Box,n.V*horizon,solids[a]);
                if(h is { } t)Consider(new(a,-1,t.ColliderId,t.TimeOfImpact,t.Normal));
            }
            for(int a=0;a<nodes.Length;a++)foreach(var sample in samples.Values)
            {
                if(!PlatformSolid(a))continue;
                var displacement=(nodes[a].V-sample.Velocity)*horizon;
                if(displacement==Vector2D.Zero)continue;
                sweeps++;var h=AabbCollisionDetector.Sweep(nodes[a].Box,displacement,new StaticAabbCollider(sample.PlatformId,sample.Bounds));
                if(h is { } t)Consider(new(a,-2,sample.PlatformId,t.TimeOfImpact,t.Normal));
            }
            foreach(var (a,b) in pairs)
            {
                var da=nodes[a].V*horizon;var db=nodes[b].V*horizon;
                if(da==db)continue;
                sweeps++;var h=PhysicalBodySweep.Linear(new(nodes[a].State.Id,nodes[a].Box,nodes[a].V),da,
                    new(nodes[b].State.Id,nodes[b].Box,nodes[b].V),db);
                if(h is { } t)Consider(new(a,b,-1,t.TimeOfImpact,t.Normal));
            }
            double elapsed=horizon*(best?.Time??1);
            double EventTime(double delta)=>paths.Length==0?time+delta:
                delta==horizon?segmentEnd:Math.Clamp(time+delta,time,segmentEnd);
            double acceptedTime=EventTime(elapsed);
            if(paths.Length>0)elapsed=acceptedTime-time;
            Aabb[] Candidate(double deltaTime)=>nodes.Select(n=>n.Box.Translated(n.V*deltaTime)).ToArray();
            var next=Candidate(elapsed);
            // The TOI multiplication may round to the penetrating side of a face. Back off
            // only a bounded numerical distance. Never use this to hide a missed collision.
            bool SnapCoMovingRoundoff(Aabb[] boxes)
            {
                // Exact co-motion can differ by a few ULPs when an analytical boundary
                // is independently sampled. Correct only microscopic overlap on that
                // same normal, never an inward swept collision or a pre-existing overlap.
                if(paths.Length==0)return false;
                var original=(Aabb[])boxes.Clone();
                static Aabb Snap(Aabb b,Aabb target,Vector2D n)=>n.X<0
                    ?new(new(target.Left-b.Width,b.Bottom),new(target.Left,b.Top))
                    :n.X>0?new(new(target.Right,b.Bottom),new(target.Right+b.Width,b.Top))
                    :n.Y<0?new(new(b.Left,target.Bottom-b.Height),new(b.Right,target.Bottom))
                    :new(new(b.Left,target.Top),new(b.Right,target.Top+b.Height));
                bool Fix(int a,Aabb other,Vector2D otherVelocity,int id)
                {
                    var h=AabbCollisionDetector.FindOverlap(boxes[a],new StaticAabbCollider(id,other));
                    if(h is not { } overlapContact)return true;
                    if(overlapContact.PenetrationDepth>ContactGap/4||Math.Abs(Dot(nodes[a].V-otherVelocity,overlapContact.Normal))>1e-10)return false;
                    boxes[a]=Snap(boxes[a],other,overlapContact.Normal);
                    return Math.Abs(boxes[a].Left-original[a].Left)<=ContactGap&&Math.Abs(boxes[a].Bottom-original[a].Bottom)<=ContactGap;
                }
                for(int pass=0;pass<=nodes.Length;pass++)
                {
                    for(int a=0;a<nodes.Length;a++)foreach(var sample in samples.Values)
                        if(PlatformSolid(a)&&!Fix(a,paths.Single(p=>p.Id==sample.PlatformId).Evaluate(acceptedTime).Bounds,sample.Velocity,sample.PlatformId))return false;
                    foreach(var (a,b) in pairs)
                    {
                        // Propagate a roundoff correction away from the corrected body.
                        if(boxes[b]!=original[b]&&boxes[a]==original[a]){if(!Fix(a,boxes[b],nodes[b].V,nodes[b].State.Id.LocalId))return false;}
                        else if(!Fix(b,boxes[a],nodes[a].V,nodes[a].State.Id.LocalId))return false;
                    }
                    if(!Overlap(boxes,acceptedTime))return true;
                }
                return false;
            }
            if(Overlap(next,acceptedTime)&&!SnapCoMovingRoundoff(next))
            {
                double speed=nodes.Select(n=>Math.Max(Math.Abs(n.V.X),Math.Abs(n.V.Y))).DefaultIfEmpty(0).Max();
                double backoff=ContactGap/(Math.Max(1,speed+samples.Values.Select(p=>Math.Max(Math.Abs(p.Velocity.X),Math.Abs(p.Velocity.Y))).DefaultIfEmpty(0).Max())*4);
                if(best is null)throw new InvalidOperationException("Missed coupled collision.");
                elapsed=Math.Max(0,elapsed-backoff);
                acceptedTime=EventTime(elapsed);
                if(paths.Length>0)elapsed=acceptedTime-time;
                next=Candidate(elapsed);
                if(Overlap(next,acceptedTime)&&!SnapCoMovingRoundoff(next))throw new InvalidOperationException("Coupled TOI penetration exceeds numerical allowance.");
            }
            for(int a=0;a<nodes.Length;a++)nodes[a].Box=next[a];
            if(paths.Length>0)
            {
                time=acceptedTime;
                remaining=endTime-time;
            }
            else remaining=Math.Max(0,remaining-elapsed);
            Record(remaining==0?endTime:paths.Length>0?time:startTime+(dt-remaining));
            if(best is not { } hit){if(paths.Length==0)break;continue;}
            var first=nodes[hit.A];var second=hit.B>=0?nodes[hit.B]:null;
            var normal=hit.Normal;var tangent=new Vector2D(-normal.Y,normal.X);
            bool platformHit=hit.B==-2;
            var boundaryVelocity=platformHit?samples[hit.Terrain].Velocity:Vector2D.Zero;
            var rel=first.V-(second?.V??boundaryVelocity);
            double inward=-Dot(rel,normal);
            if(inward<=0)throw new InvalidOperationException("Non-inward coupled impact.");
            double support=Math.Max(0,-Dot(first.AccelerationStep-(second?.AccelerationStep??Vector2D.Zero),normal));
            double cutoff=Math.Max(support,Math.Max(first.State.Settings.BounceThreshold,second?.State.Settings.BounceThreshold??0));
            bool firstAxis=normal.X!=0?!first.XImpact:!first.YImpact;
            if(second is not null)firstAxis &= normal.X!=0?!second.XImpact:!second.YImpact;
            if(normal.X!=0){first.XImpact=true;if(second is not null)second.XImpact=true;}
            else{first.YImpact=true;if(second is not null)second.YImpact=true;}
            double impact=Math.Max(0,inward-(firstAxis?support*Math.Clamp(remaining/dt,0,1):0));
            var material=materials.ResolveContact(first.State.Definition.MaterialId,second?.State.Definition.MaterialId??(platformHit?"Default":surface(hit.Terrain)));
            bool characterHit=second is null&&!platformHit&&characterBoundaries?.Contains(hit.Terrain)==true;
            double bounce=characterHit||platformHit||impact<=cutoff?0:impact*material.Restitution;
            double ia=1/first.Mass,ib=second is null?0:1/second.Mass;
            double impulse=(inward+bounce)/(ia+ib);
            double vt=Dot(rel,tangent),desired=Math.Abs(vt)/(ia+ib);
            double friction=characterHit?0:desired<=material.StaticFriction*impulse?desired:Math.Min(desired,material.DynamicFriction*impulse);
            var change=normal*impulse-tangent*(Math.Sign(vt)*friction);
            first.V+=change*ia;if(second is not null)second.V-=change*ib;
            contacts++;if(second is not null)pairContacts++;if(characterHit)characterImpacts++;if(platformHit)platformImpacts++;
            // Find the active face constraints after advancing every body to the same time.
            // The hit itself is included even for a simultaneous corner entry.
            var constraints=new HashSet<Constraint>();var edges=new HashSet<PhysicalBodyPair>();
            var typedConstraints=new List<AxisContactConstraint>();
            var platformFaces=new HashSet<(int Body,int Platform,Vector2D Normal)>();
            var terrainFaces=new HashSet<(int Body,int Terrain,Vector2D Normal)>();
            var prescribed=new Dictionary<PhysicalBodyId,Vector2D>();
            var anchors=new Dictionary<PhysicalBodyId,int>();var anchorVelocities=new List<Vector2D>();
            int Anchor(PhysicalBodyId id,Vector2D velocity)
            {
                if(paths.Length==0&&anchorVelocities.Count>0)return nodes.Length;
                if(anchors.TryGetValue(id,out int index))return index;
                index=nodes.Length+anchorVelocities.Count;anchors.Add(id,index);anchorVelocities.Add(velocity);prescribed.Add(id,velocity);return index;
            }
            void Add(int a,int b,int terrainId,Vector2D n)
            {
                bool x=n.X!=0;bool positive=x?n.X>0:n.Y>0;
                var id=b>=0?nodes[b].State.Id:new PhysicalBodyId(b==-2?PhysicalBodyKind.KinematicPlatform:PhysicalBodyKind.Static,terrainId);
                int other=b>=0?b:Anchor(id,b==-2?samples[terrainId].Velocity:Vector2D.Zero);
                constraints.Add(new(positive?other:a,positive?a:other,x));
                edges.Add(new(nodes[a].State.Id,id));
                typedConstraints.Add(AxisContactConstraint.FromNormal(nodes[a].State.Id,id,n));
                if(b==-2)platformFaces.Add((a,terrainId,n));
                else if(b==-1&&characterBoundaries?.Contains(terrainId)!=true)terrainFaces.Add((a,terrainId,n));
            }
            void Faces(int a,int b,int terrainId,Aabb other)
            {
                var box=nodes[a].Box;
                if(box.Bottom<other.Top&&box.Top>other.Bottom)
                {
                    if(Math.Abs(box.Right-other.Left)<=ContactGap)Add(a,b,terrainId,new(-1,0));
                    if(Math.Abs(box.Left-other.Right)<=ContactGap)Add(a,b,terrainId,new(1,0));
                }
                if(box.Left<other.Right&&box.Right>other.Left)
                {
                    if(Math.Abs(box.Top-other.Bottom)<=ContactGap)Add(a,b,terrainId,new(0,-1));
                    if(Math.Abs(box.Bottom-other.Top)<=ContactGap)Add(a,b,terrainId,new(0,1));
                }
            }
            Add(hit.A,hit.B,hit.Terrain,normal);
            for(int a=0;a<nodes.Length;a++)foreach(var c in solids[a])Faces(a,-1,c.Id,c.Bounds);
            foreach(var (a,b) in pairs)Faces(a,b,-1,nodes[b].Box);
            for(int a=0;a<nodes.Length;a++)foreach(var sample in samples.Values)
                if(PlatformSolid(a))Faces(a,-2,sample.PlatformId,paths.Single(p=>p.Id==sample.PlatformId).Evaluate(acceptedTime).Bounds);
            if(paths.Length>0)
            {
                var crushed=CompressionConstraintGraph.Find(typedConstraints,prescribed);
                if(crushed.Count>0)
                {
                    var relevant=crushed.Where(c=>c.Body.Kind==PhysicalBodyKind.Dynamic).ToArray();
                    var moving=relevant.SelectMany(c=>new[]{c.LowerAnchor,c.UpperAnchor})
                        .Where(id=>id.Kind==PhysicalBodyKind.KinematicPlatform&&prescribed[id]!=Vector2D.Zero).Select(id=>id.LocalId).Distinct().OrderBy(id=>id).ToArray();
                    if(moving.Length==0)throw new InvalidOperationException("Incompatible prescribed contact constraints.");
                    var participants=relevant.Select(c=>c.Body.LocalId).Distinct().OrderBy(id=>id).ToArray();
                    throw new PlatformPropCompressionException(moving,participants);
                }
            }
            // Simultaneous terrain contacts also need a material response. Otherwise
            // inelastic constraint closure removes their normal velocity without using
            // its impulse for friction: all but the primary grounded slider can coast
            // forever. Consume each inward normal once; separating contacts (including
            // the already resolved primary impact) do not receive another impulse.
            // Keep secondary normal closure inelastic, as before. Character boundaries
            // are deliberately excluded: they are not frictional terrain.
            foreach(var face in terrainFaces.OrderBy(f=>f.Body).ThenBy(f=>f.Terrain).ThenBy(f=>f.Normal.X).ThenBy(f=>f.Normal.Y))
            {
                var node=nodes[face.Body];var n=face.Normal;
                double closing=-Dot(node.V,n);
                if(closing<=0)continue;
                var t=new Vector2D(-n.Y,n.X);double tangential=Dot(node.V,t);
                var m=materials.ResolveContact(node.State.Definition.MaterialId,surface(face.Terrain));
                double frictionSpeed=Math.Abs(tangential)<=m.StaticFriction*closing?Math.Abs(tangential):Math.Min(Math.Abs(tangential),m.DynamicFriction*closing);
                node.V+=n*closing-t*(Math.Sign(tangential)*frictionSpeed);
                contacts++;
            }
            // A different island's floor TOI can win the event tie. Apply secondary
            // platform normal and friction responses as well so resting carriers do
            // not depend on which island happened to supply the primary impact.
            foreach(var face in platformFaces.OrderBy(f=>f.Body).ThenBy(f=>f.Platform).ThenBy(f=>f.Normal.X).ThenBy(f=>f.Normal.Y))
            {
                var node=nodes[face.Body];var n=face.Normal;
                var relative=node.V-samples[face.Platform].Velocity;
                double closing=-Dot(relative,n);
                if(closing<=0)continue;
                var t=new Vector2D(-n.Y,n.X);double tangential=Dot(relative,t);
                var m=materials.ResolveContact(node.State.Definition.MaterialId,"Default");
                double frictionSpeed=Math.Abs(tangential)<=m.StaticFriction*closing?Math.Abs(tangential):Math.Min(Math.Abs(tangential),m.DynamicFriction*closing);
                node.V+=n*closing-t*(Math.Sign(tangential)*frictionSpeed);
                platformImpacts++;contacts++;
            }
            var islands=BodyContactGraph.Build(edges);largestIsland=Math.Max(largestIsland,islands.Select(i=>i.Participants.Count).DefaultIfEmpty(0).Max());
            // Deterministic inelastic closure: each violating inequality merges two groups.
            // An anchored group inherits its prescribed normal velocity. Bounded merges; no
            // repeated epsilon impulses or arbitrary iteration-order-dependent body stepping.
            foreach(bool x in new[]{true,false})
            {
                var active=constraints.Where(c=>c.X==x).OrderBy(c=>paths.Length==0&&c.Lower>=nodes.Length?-1:c.Lower).ThenBy(c=>paths.Length==0&&c.Upper>=nodes.Length?-1:c.Upper).ToArray();
                int count=nodes.Length;
                var group=Enumerable.Range(0,count+anchorVelocities.Count).ToArray();
                var value=nodes.Select(n=>x?n.V.X:n.V.Y).Concat(anchorVelocities.Select(v=>x?v.X:v.Y)).ToArray();
                var mass=nodes.Select(n=>n.Mass).Concat(anchorVelocities.Select(_=>0d)).ToArray();
                while(true)
                {
                    bool merged=false;
                    foreach(var c in active)
                    {
                        int l=group[c.Lower],u=group[c.Upper];
                        if(l==u||value[l]<=value[u])continue;
                        if(l>=count&&u>=count)throw new InvalidOperationException("Incompatible anchored groups after contact analysis.");
                        int keep=l>=count?l:u>=count?u:Math.Min(l,u),drop=keep==l?u:l;
                        double sum=mass[l]+mass[u];
                        double v=keep>=count?value[keep]:value[l]*(mass[l]/sum)+value[u]*(mass[u]/sum);
                        if(!double.IsFinite(v)||!double.IsFinite(sum))throw new InvalidOperationException("Invalid contact group mass or velocity.");
                        for(int i=0;i<group.Length;i++)if(group[i]==drop)group[i]=keep;
                        value[keep]=v;mass[keep]=sum;
                        // A violating prop-pair constraint is a real momentum response even
                        // when a simultaneous terrain hit was selected as the primary TOI.
                        // Count applied responses, not merely touching faces. A pair may
                        // respond more than once during an event as its island is resolved.
                        if(c.Lower<count&&c.Upper<count){pairContacts++;contacts++;}
                        else if(paths.Length>0&&anchors.Any(p=>p.Key.Kind==PhysicalBodyKind.KinematicPlatform&&(p.Value==c.Lower||p.Value==c.Upper)))
                        {platformImpacts++;contacts++;}
                        merged=true;break;
                    }
                    if(!merged)break;
                }
                for(int a=0;a<count;a++)nodes[a].V=x?new(value[group[a]],nodes[a].V.Y):new(nodes[a].V.X,value[group[a]]);
            }
        }
        // Recheck the actual committed endpoint, not a reconstructed clock.
        if(paths.Length>0&&Overlap(nodes.Select(n=>n.Box).ToArray(),endTime))
            throw new InvalidOperationException("Coupled platform tick ended in penetration at the authoritative endpoint.");
        Record(endTime);
        var output=new SortedDictionary<int,DynamicBodyState>();
        foreach(var n in nodes)output.Add(n.State.Id.LocalId,n.State with{Bounds=n.Box,Velocity=n.V,PendingForce=default,PendingImpulse=default});
        return new(output,sweeps,contacts,pairContacts,largestIsland,characterImpacts,platformImpacts,eventIterations);
    }
}
