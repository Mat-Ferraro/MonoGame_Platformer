namespace PlatformerSandbox.Gameplay.World;

/// <summary>Classified physical crush handling, independent of numerical fault sensitivity.</summary>
public enum WorldCrushPolicy
{
    Throw,
    RespawnAtNamedSpawn,
    Death
}
