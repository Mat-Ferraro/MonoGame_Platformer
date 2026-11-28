using System.Text.Json;
using System.Text.Json.Serialization;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Physics;

namespace PlatformerSandbox.Gameplay.World;

/// <summary>Optional sidecar authoring for translational bodies. Independent of LDtk importer;
/// any authored world can opt in with a level ID. LDtk entity schema integration follows PB01N.</summary>
public sealed class WorldDynamicBodyFixtures
{
    public required int Version { get; init; }
    public required Dictionary<string, Fixture[]> Levels { get; init; }
    public Dictionary<string,Dictionary<int,string>>? TerrainMaterials { get; init; }
    public sealed class Fixture
    {
        public required int Id { get; init; }
        public required string Material { get; init; }
        public required double X { get; init; }
        public required double Y { get; init; }
        public double HalfWidth { get; init; }=.5;
        public double HalfHeight { get; init; }=.5;
        public double? Mass { get; init; }
        public double GravityScale { get; init; }=1;
        public BodyCollisionLayer CollisionMask { get; init; }=BodyCollisionLayer.Terrain;
        public double MaxSpeed { get; init; }=120;
        public RigidBodyCrushResponse CrushResponse { get; init; }=RigidBodyCrushResponse.StopPlatform;
        public double BounceThreshold { get; init; }=.5;
        public double VelocityX { get; init; }
        public double VelocityY { get; init; }
        public void AddTo(DynamicBodyWorld world)=>world.Add(new(new(PhysicalBodyKind.Dynamic,Id),new(HalfWidth,HalfHeight),Material,Mass),
            new DynamicBodySettings {GravityScale=GravityScale,MaxSpeed=MaxSpeed,CrushResponse=CrushResponse,BounceThreshold=BounceThreshold,Filter=new(BodyCollisionLayer.Dynamic,CollisionMask)},new(X,Y),new(VelocityX,VelocityY));
    }
    public static WorldDynamicBodyFixtures FromJson(string json)
    {
        var options=new JsonSerializerOptions {UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false));
        var result=JsonSerializer.Deserialize<WorldDynamicBodyFixtures>(json,options)
            ??throw new JsonException("Missing body fixture file.");
        if(result.Version!=1||result.Levels is null) throw new JsonException("Unsupported body fixture version.");
        return result;
    }
    public bool TryInstall(WorldSession world,string levelId,PhysicsMaterialCatalog materials)
    {
        if(!Levels.TryGetValue(levelId,out var fixtures)) return false;
        world.InstallDynamicBodies(materials,fixtures,TerrainMaterials is not null&&TerrainMaterials.TryGetValue(levelId,out var surfaces)?surfaces:null);return true;
    }
}
