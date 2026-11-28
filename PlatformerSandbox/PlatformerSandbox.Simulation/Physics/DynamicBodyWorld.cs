using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.Physics;

[Flags]
public enum BodyCollisionLayer : uint { None=0, Terrain=1, Platform=2, Character=4, Dynamic=8, All=15 }

/// <summary>Symmetric category/mask test. A pair must consent on both sides.</summary>
public readonly record struct BodyCollisionFilter(BodyCollisionLayer Category, BodyCollisionLayer Mask)
{
    public bool IsValid => (Category is BodyCollisionLayer.Terrain or BodyCollisionLayer.Platform or BodyCollisionLayer.Character or BodyCollisionLayer.Dynamic)
        && (Mask & ~BodyCollisionLayer.All)==0;
    public bool Allows(BodyCollisionFilter other) => IsValid && other.IsValid &&
        (Mask & other.Category)!=0 && (other.Mask & Category)!=0;
    public static BodyCollisionFilter Terrain => new(BodyCollisionLayer.Terrain,BodyCollisionLayer.Dynamic);
    public static BodyCollisionFilter DynamicTerrain => new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.Terrain);
    public static BodyCollisionFilter DynamicSolid => new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.Terrain|BodyCollisionLayer.Dynamic);
    public static BodyCollisionFilter DynamicCharacterSolid => new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.Terrain|BodyCollisionLayer.Dynamic|BodyCollisionLayer.Character);
    public static BodyCollisionFilter Character => new(BodyCollisionLayer.Character,BodyCollisionLayer.Dynamic);
}

public enum RigidBodyCrushResponse { StopPlatform, Destroy }

/// <summary>Classified incompatible prescribed motion, not a numerical fault.</summary>
public sealed class PlatformPropCompressionException : InvalidOperationException
{
    public PlatformPropCompressionException(int[] platforms,int[] bodies):base($"Platform compression: platforms {string.Join(",",platforms)}; props {string.Join(",",bodies)}")
    { PlatformIds=Array.AsReadOnly(platforms);BodyIds=Array.AsReadOnly(bodies); }
    public IReadOnlyList<int> PlatformIds { get; }
    public IReadOnlyList<int> BodyIds { get; }
}
public sealed record DynamicBodySettings
{
    public RigidBodyCrushResponse CrushResponse { get; init; }=RigidBodyCrushResponse.StopPlatform;
    public double GravityScale { get; init; }=1;
    public double MaxSpeed { get; init; }=120;
    /// <summary>Minimum inward impact speed for restitution (world units/second).
    /// The solver also suppresses impacts at or below one tick of sustained inward acceleration.</summary>
    public double BounceThreshold { get; init; }=.5;
    public BodyCollisionFilter Filter { get; init; }=BodyCollisionFilter.DynamicTerrain;
    internal void Validate()
    {
        if (!double.IsFinite(GravityScale)||GravityScale<0||!double.IsFinite(MaxSpeed)||MaxSpeed<=0||
            !double.IsFinite(BounceThreshold)||BounceThreshold<0||!Enum.IsDefined(CrushResponse)||!Filter.IsValid||Filter.Category!=BodyCollisionLayer.Dynamic)
            throw new ArgumentException("Invalid dynamic settings.");

    }
}

public readonly record struct DynamicBodyState(PhysicalBodyDefinition Definition, DynamicBodySettings Settings,
    Aabb Bounds, Vector2D Velocity, Vector2D PendingForce, Vector2D PendingImpulse)
{
    public PhysicalBodyId Id=>Definition.Id;
    public PhysicalBodySnapshot Snapshot=>new(Id,Bounds,Velocity);
}
public readonly record struct DynamicStepMetrics(long Steps,long Sweeps,long Contacts);

/// <summary>First translational integrator. Single authoritative tick owner calls Advance.
/// Terrain CCD uses the existing linear sweep; acceleration is semi-implicit per fixed tick.
/// Dynamic-to-dynamic contacts opt into the shared linear solver; kinematic coupling remains separate.</summary>
public sealed class DynamicBodyWorld
{
    private readonly IReadOnlyList<StaticAabbCollider> _terrain;
    private readonly Dictionary<int,StaticAabbCollider> _byId;
    private readonly Dictionary<int,string> _terrainMaterials;
    private SortedDictionary<int,DynamicBodyState> _bodies=new();
    private readonly BodyPairExclusions _ignored=new();
    private static PhysicalBodyId DynamicId(int id)=>new(PhysicalBodyKind.Dynamic,id);
    private static PhysicalBodyPair TerrainPair(int body,int terrain)=>new(DynamicId(body),new(PhysicalBodyKind.Static,terrain));
    // World-owned provider for validating edits against committed live characters.
    private readonly Func<IReadOnlyList<PhysicalBodySnapshot>>? _characters;
    public PhysicsMaterialCatalog Materials { get; private set; }
    private readonly Func<IReadOnlyList<MovingPlatformSample>>? _platforms;
    public long PlatformContacts { get; private set; }
    public long DestroyedByPlatform { get; private set; }
    public IReadOnlyList<int> LastPlatformDestroyedBodyIds { get; private set; }=Array.Empty<int>();
    public static bool PlatformSolid(DynamicBodyState body)=>(body.Settings.Filter.Mask&BodyCollisionLayer.Platform)!=0;
    public long CharacterBoundaryImpacts { get; private set; }
    public long CharacterPushes { get; private set; }
    public static bool CharacterSolid(DynamicBodyState body)=>body.Settings.Filter.Allows(BodyCollisionFilter.Character);
    public DynamicStepMetrics Metrics { get; private set; }
    /// <summary>Applied prop-pair impact and inelastic closure responses, not unique
    /// collision pairs or passive touching faces. A subset of Metrics.Contacts.</summary>
    public long PairContacts { get; private set; }
    public int LargestContactIsland { get; private set; }
    /// <summary>Event-loop passes in successfully committed coupled solves only. Excludes previews, discarded retries and independent terrain solves.</summary>
    public long CoupledEventIterations { get; private set; }
    public double LastStepMilliseconds { get; private set; }
    public double TotalStepMilliseconds { get; private set; }
    public IReadOnlyList<DynamicBodyState> Bodies=>Array.AsReadOnly(_bodies.Values.ToArray());
    public DynamicBodyWorld(IReadOnlyList<StaticAabbCollider> terrain,PhysicsMaterialCatalog materials,
        IReadOnlyDictionary<int,string>? terrainMaterials=null, Func<IReadOnlyList<PhysicalBodySnapshot>>? characters=null, Func<IReadOnlyList<MovingPlatformSample>>? platforms=null)
    {
        ArgumentNullException.ThrowIfNull(terrain); ArgumentNullException.ThrowIfNull(materials);
        _characters=characters; _platforms=platforms; _terrain=SpatialColliderSet.Wrap(terrain); _byId=_terrain.ToDictionary(c=>c.Id); Materials=materials;
        _terrainMaterials=terrainMaterials?.ToDictionary(p=>p.Key,p=>p.Value)??new();
        foreach(var pair in _terrainMaterials) { if(!_byId.ContainsKey(pair.Key)) throw new ArgumentException("Unknown terrain ID."); materials.Require(pair.Value); }
    }
    public string TerrainMaterial(int id)
    {
        if(!_byId.ContainsKey(id))throw new ArgumentOutOfRangeException(nameof(id));
        return Surface(id);
    }
    public void SetTerrainMaterial(int id,string material)
    {
        if(!_byId.ContainsKey(id))throw new ArgumentOutOfRangeException(nameof(id));
        Materials.Require(material);_terrainMaterials[id]=material;
    }
    private string Surface(int id)=>_terrainMaterials.TryGetValue(id,out var material)?material:"Default";
    private bool Solid(DynamicBodyState body,StaticAabbCollider c)=>body.Settings.Filter.Allows(BodyCollisionFilter.Terrain)&&!_ignored.Contains(TerrainPair(body.Id.LocalId,c.Id));
    private bool PairSolid(DynamicBodyState a,DynamicBodyState b)=>a.Settings.Filter.Allows(b.Settings.Filter)&&!_ignored.Contains(new(a.Id,b.Id));
    private void ValidateState(DynamicBodyState state)
    {
        state.Settings.Validate(); _=state.Definition.ResolveMass(Materials);
        if(PlatformSolid(state)&&_platforms is not null&&_platforms().Any(c=>AabbCollisionDetector.Overlaps(state.Bounds,c.Bounds)))
            throw new ArgumentException("Dynamic body overlaps a moving platform.");
        if(CharacterSolid(state)&&_characters is not null&&_characters().Any(c=>AabbCollisionDetector.Overlaps(state.Bounds,c.Bounds)))
            throw new ArgumentException("Dynamic body overlaps a live character.");
        if(_terrain.Any(c=>Solid(state,c)&&AabbCollisionDetector.Overlaps(state.Bounds,c.Bounds)))
            throw new ArgumentException("Dynamic body overlaps solid terrain.");
        if(_bodies.Values.Any(b=>b.Id!=state.Id&&PairSolid(state,b)&&AabbCollisionDetector.Overlaps(state.Bounds,b.Bounds)))
            throw new ArgumentException("Dynamic body overlaps another solid body.");
    }
    public void Add(PhysicalBodyDefinition definition,DynamicBodySettings settings,Vector2D center,Vector2D velocity=default)
    {
        ArgumentNullException.ThrowIfNull(definition);ArgumentNullException.ThrowIfNull(settings);
        if(definition.Id.Kind!=PhysicalBodyKind.Dynamic||_bodies.ContainsKey(definition.Id.LocalId)) throw new ArgumentException("Invalid/duplicate dynamic ID.");
        var state=new DynamicBodyState(definition,settings,Aabb.FromCenterAndHalfExtents(center,definition.HalfExtents),velocity,default,default);
        ValidateState(state); _bodies.Add(definition.Id.LocalId,state);
    }
    public void Remove(int id) { _bodies.Remove(id);_ignored.Remove(DynamicId(id)); }
    public void Clear() { _bodies.Clear();_ignored.Clear(); }
    public DynamicBodyState Get(int id)=>_bodies[id];
    public void ApplyForce(int id,Vector2D force) { var s=Get(id);_bodies[id]=s with {PendingForce=s.PendingForce+force}; }
    public void ApplyImpulse(int id,Vector2D impulse) { var s=Get(id);_bodies[id]=s with {PendingImpulse=s.PendingImpulse+impulse}; }
    public void Configure(int id,string material,double? mass,DynamicBodySettings settings)
    {
        var s=Get(id);var next=s with {Definition=new PhysicalBodyDefinition(s.Id,s.Definition.HalfExtents,material,mass),Settings=settings};
        ValidateState(next);_bodies[id]=next;
    }
    public void ReloadMaterials(PhysicsMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        foreach(var s in _bodies.Values) _=s.Definition.ResolveMass(materials);
        foreach(string id in _terrainMaterials.Values) materials.Require(id);
        Materials=materials;
    }
    public bool IsExcluded(int body,int terrain)=>_ignored.Contains(TerrainPair(body,terrain));
    public void SetExcluded(int body,int terrain,bool excluded)
    {
        var s=Get(body);var c=_byId[terrain];
        if(excluded) _ignored.Set(TerrainPair(body,terrain),true);
        else { if(s.Settings.Filter.Allows(BodyCollisionFilter.Terrain)&&AabbCollisionDetector.Overlaps(s.Bounds,c.Bounds)) throw new InvalidOperationException("Cannot restore a penetrating pair."); _ignored.Set(TerrainPair(body,terrain),false); }
    }
    public bool IsBodyPairExcluded(int a,int b)=>_ignored.Contains(new(DynamicId(a),DynamicId(b)));
    public void SetBodyPairExcluded(int a,int b,bool excluded)
    {
        var first=Get(a);var second=Get(b);var pair=new PhysicalBodyPair(first.Id,second.Id);
        if(!excluded&&first.Settings.Filter.Allows(second.Settings.Filter)&&AabbCollisionDetector.Overlaps(first.Bounds,second.Bounds))
            throw new InvalidOperationException("Cannot restore a penetrating dynamic pair.");
        _ignored.Set(pair,excluded);
    }
    public void Advance(double dt,double gravity) => Advance(dt,gravity,Array.Empty<PhysicalBodySnapshot>(),null);

    /// <summary>Staged character boundaries are fixed for this prop pass. The player
    /// remains kinematic; pushes are bounded forces, not a mass/impulse response.
    /// Neither pending forces nor state are committed on failure.</summary>
    public void Advance(double dt,double gravity,IReadOnlyList<PhysicalBodySnapshot> characters,
        IReadOnlyDictionary<int,Vector2D>? pushes, IReadOnlyList<MovingPlatformTrajectory>? platforms=null,double startTime=0,
        IReadOnlySet<PhysicalBodyPair>? riderPairs=null,IReadOnlyDictionary<int,Aabb>? expectedSupports=null)
        =>AdvanceCore(dt,gravity,characters,pushes,platforms,startTime,riderPairs,expectedSupports,null);

    public Dictionary<int,List<(double Time,Aabb Bounds)>> PreviewSupportMotion(double dt,double gravity,
        IReadOnlyList<PhysicalBodySnapshot> characters,IReadOnlyList<MovingPlatformTrajectory> platforms,double startTime,
        IReadOnlySet<PhysicalBodyPair> riderPairs,IEnumerable<int> supportIds)
    {
        var trace=supportIds.Distinct().ToDictionary(id=>id,id=>new List<(double Time,Aabb Bounds)>());
        AdvanceCore(dt,gravity,characters,null,platforms,startTime,riderPairs,null,trace);
        return trace;
    }
    private void AdvanceCore(double dt,double gravity,IReadOnlyList<PhysicalBodySnapshot> characters,
        IReadOnlyDictionary<int,Vector2D>? pushes,IReadOnlyList<MovingPlatformTrajectory>? platforms,double startTime,
        IReadOnlySet<PhysicalBodyPair>? riderPairs,IReadOnlyDictionary<int,Aabb>? expectedSupports,
        Dictionary<int,List<(double Time,Aabb Bounds)>>? trace)
    {
        if(!double.IsFinite(dt)||dt<=0||!double.IsFinite(gravity)||gravity>0) throw new ArgumentOutOfRangeException(nameof(dt));
        long start=System.Diagnostics.Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(characters);
        if(characters.Any(c=>c.Id.Kind!=PhysicalBodyKind.KinematicCharacter)||characters.Select(c=>c.Id).Distinct().Count()!=characters.Count)
            throw new ArgumentException("Invalid character boundaries.",nameof(characters));
        if(pushes is not null && pushes.Keys.Any(id=>!_bodies.ContainsKey(id))) throw new ArgumentException("Unknown push body.",nameof(pushes));
        bool useCharacters=characters.Count>0&&_bodies.Values.Any(CharacterSolid);
        if(useCharacters||_bodies.Values.Any(b=>(b.Settings.Filter.Mask&BodyCollisionLayer.Dynamic)!=0||PlatformSolid(b)))
        {
            // Allocate temporary collider IDs in the solver adapter only; body identities remain typed.
            var boundaries=new Dictionary<int,PhysicalBodySnapshot>();
            int nextId=int.MaxValue;
            if(useCharacters)foreach(var character in characters.OrderBy(c=>c.Id.LocalId))
            {
                while(_byId.ContainsKey(nextId)||(platforms?.Any(p=>p.Id==nextId)??false))nextId--;
                boundaries.Add(nextId--,character);
            }
            var terrain=boundaries.Count==0?_terrain:SpatialColliderSet.Wrap(_terrain.Concat(boundaries.Select(p=>new StaticAabbCollider(p.Key,p.Value.Bounds))).ToArray());
            var inputs=_bodies.Values.Select(b=>pushes is not null&&pushes.TryGetValue(b.Id.LocalId,out var f)&&CharacterSolid(b)
                ?b with {PendingForce=b.PendingForce+f}:b);
            var active=inputs.ToArray();var destroyed=new HashSet<int>();
            DynamicContactSolver.Result result;
            while(true)
            {
                try
                {
                    if(trace is not null)foreach(var points in trace.Values)points.Clear();
                    result=DynamicContactSolver.Advance(active,terrain,Materials,Surface,
                        (b,c)=>boundaries.TryGetValue(c.Id,out var character)?CharacterSolid(b)&&
                            !(riderPairs?.Contains(new PhysicalBodyPair(b.Id,character.Id))??false):Solid(b,c),PairSolid,dt,gravity,
                        boundaries.Keys.ToHashSet(),platforms,startTime,trace);
                    break;
                }
                catch(PlatformPropCompressionException ex)
                {
                    // Stop takes precedence in mixed chains. Destruction is staged; retry from
                    // the committed tick with one fewer participant. No mutation on failure.
                    if(ex.BodyIds.Any(id=>Get(id).Settings.CrushResponse==RigidBodyCrushResponse.StopPlatform))throw;
                    int id=ex.BodyIds.First();if(!destroyed.Add(id))throw;
                    active=active.Where(b=>b.Id.LocalId!=id).ToArray();
                }
            }
            if(trace is not null)return; // No bodies, exclusions, impulses or metrics committed.
            // The rider/support pair was swept by the motor against the preview.
            // Reject a changed support path endpoint or penetration BEFORE any commit.
            if(expectedSupports is not null)foreach(var expected in expectedSupports)
                if(!result.Bodies.TryGetValue(expected.Key,out var support)||support.Bounds!=expected.Value)
                    throw new InvalidOperationException($"Dynamic support {expected.Key} changed during character coupling; tick not committed.");
            if(riderPairs is not null)foreach(var character in characters)foreach(var body in result.Bodies.Values)
                if(riderPairs.Contains(new PhysicalBodyPair(body.Id,character.Id))&&AabbCollisionDetector.Overlaps(body.Bounds,character.Bounds))
                    throw new InvalidOperationException("Rider/support coupling ended in penetration; tick not committed.");
            var nextMetrics=new DynamicStepMetrics(checked(Metrics.Steps+1),checked(Metrics.Sweeps+result.Sweeps),checked(Metrics.Contacts+result.Contacts));
            long nextPairs=checked(PairContacts+result.Pairs);
            long nextIterations=checked(CoupledEventIterations+result.EventIterations);
            long nextCharacterImpacts=checked(CharacterBoundaryImpacts+result.CharacterImpacts);
            long nextPushes=checked(CharacterPushes+(pushes?.Count??0));
            long nextPlatformContacts=checked(PlatformContacts+result.PlatformImpacts);
            long nextDestroyed=checked(DestroyedByPlatform+destroyed.Count);
            _bodies=result.Bodies;foreach(int id in destroyed)_ignored.Remove(DynamicId(id));
            PlatformContacts=nextPlatformContacts;DestroyedByPlatform=nextDestroyed;LastPlatformDestroyedBodyIds=Array.AsReadOnly(destroyed.OrderBy(id=>id).ToArray());Metrics=nextMetrics;PairContacts=nextPairs;LargestContactIsland=result.LargestIsland;
            CharacterBoundaryImpacts=nextCharacterImpacts;CharacterPushes=nextPushes;CoupledEventIterations=nextIterations;
            LastStepMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;TotalStepMilliseconds+=LastStepMilliseconds;return;
        }
        if(trace is not null)return;
        var staged=new SortedDictionary<int,DynamicBodyState>();long sweeps=0,contacts=0;
        foreach(var s in _bodies.Values)
        {
            double inverse=1/s.Definition.ResolveMass(Materials)!.Value;
            var mat=Materials.Require(s.Definition.MaterialId);
            // Keep sustained acceleration separate from impulses: a resting body receives
            // this velocity increment every tick even when its previous velocity was zero.
            var accelerationStep=(s.PendingForce*inverse+new Vector2D(0,gravity*s.Settings.GravityScale))*dt;
            var v=(s.Velocity+s.PendingImpulse*inverse+accelerationStep)*Math.Exp(-mat.LinearDamping*dt);
            v=new(Math.Clamp(v.X,-s.Settings.MaxSpeed,s.Settings.MaxSpeed),Math.Clamp(v.Y,-s.Settings.MaxSpeed,s.Settings.MaxSpeed));
            var box=s.Bounds;double remaining=dt;int iteration=0;bool bouncedOnX=false,bouncedOnY=false;
            // Filter before sweep; one-way semantics for authored platforms will be added with platform coupling.
            var solid=s.Settings.Filter.Mask==BodyCollisionLayer.Terrain&&!_ignored.Has(s.Id)
                ?_terrain:SpatialColliderSet.Wrap(_terrain.Where(c=>Solid(s,c)).ToArray());
            while(remaining>0&&v!=Vector2D.Zero)
            {
                if(++iteration>16) throw new InvalidOperationException("Dynamic terrain contact budget exhausted.");
                var delta=v*remaining;sweeps++;
                var hit=AabbCollisionDetector.SweepFirst(box,delta,solid);
                if(hit is null) { box=box.Translated(delta);break; }
                var h=hit.Value;var target=_byId[h.ColliderId].Bounds;contacts++;
                box=box.Translated(delta*h.TimeOfImpact);
                var n=h.Normal;
                // Snap only the contacting face, exactly to the terrain boundary. Prevent roundoff penetration.
                if(n.X<0) box=new(new(target.Left-box.Width,box.Bottom),new(target.Left,box.Top));
                else if(n.X>0) box=new(new(target.Right,box.Bottom),new(target.Right+box.Width,box.Top));
                else if(n.Y<0) box=new(new(box.Left,target.Bottom-box.Height),new(box.Right,target.Bottom));
                else box=new(new(box.Left,target.Top),new(box.Right,target.Top+box.Height));
                double inward=-(v.X*n.X+v.Y*n.Y);
                if(inward<=0) throw new InvalidOperationException("Non-inward swept contact.");
                var m=Materials.ResolveContact(s.Definition.MaterialId,Surface(h.ColliderId));
                // A material must not bounce from the inward velocity that a resting body
                // acquires solely through this tick's sustained force/gravity. Otherwise
                // the fixed-step integrator can sustain a perpetual micro-bounce at rest.
                // Use the normal component only; tangential movement is not clamped.
                double supportSpeed=Math.Max(0,-(accelerationStep.X*n.X+accelerationStep.Y*n.Y));
                double cutoff=Math.Max(s.Settings.BounceThreshold,supportSpeed);
                // The linear sweep uses the full-tick velocity. Do not let the unelapsed
                // portion of sustained inward acceleration also feed the rebound. Apply
                // this correction at most once per axis, since acceleration is integrated once.
                bool firstOnAxis=n.X!=0?!bouncedOnX:!bouncedOnY;
                if(n.X!=0)bouncedOnX=true;else bouncedOnY=true;
                double unelapsedFraction=Math.Clamp((remaining/dt)*(1-h.TimeOfImpact),0,1);
                double impactSpeed=Math.Max(0,inward-(firstOnAxis?supportSpeed*unelapsedFraction:0));
                double reboundSpeed=impactSpeed<=cutoff?0:impactSpeed*m.Restitution;
                var tangent=new Vector2D(-n.Y,n.X);double vt=v.X*tangent.X+v.Y*tangent.Y;
                double normalImpulse=inward+reboundSpeed;
                double friction=Math.Abs(vt)<=m.StaticFriction*normalImpulse?Math.Abs(vt):Math.Min(Math.Abs(vt),m.DynamicFriction*normalImpulse);
                v=n*reboundSpeed+tangent*(vt-Math.Sign(vt)*friction);
                remaining*=1-h.TimeOfImpact;
            }
            var next=s with {Bounds=box,Velocity=v,PendingForce=default,PendingImpulse=default};
            if(solid.Any(c=>AabbCollisionDetector.Overlaps(box,c.Bounds))) throw new InvalidOperationException("Unresolved dynamic terrain overlap.");
            staged.Add(s.Id.LocalId,next);
        }
        var metrics=new DynamicStepMetrics(checked(Metrics.Steps+1),checked(Metrics.Sweeps+sweeps),checked(Metrics.Contacts+contacts));
        _bodies=staged;LastPlatformDestroyedBodyIds=Array.Empty<int>();Metrics=metrics;LargestContactIsland=0;LastStepMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;TotalStepMilliseconds+=LastStepMilliseconds;
    }
}
