using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Gameplay.World;

public enum WorldRespawnMode { None, Immediate, Delayed, Manual }

/// <summary>Independent lifecycle consequence. Delays are authoritative simulation ticks.</summary>
public sealed record WorldRespawnSettings
{
    public WorldRespawnMode Mode { get; init; } = WorldRespawnMode.Immediate;
    public int DelayTicks { get; init; }
    public int? MaximumRespawns { get; init; }
    public void Validate()
    {
        if(!Enum.IsDefined(Mode)||DelayTicks<0||(Mode==WorldRespawnMode.Delayed&&DelayTicks==0)||MaximumRespawns is <0)
            throw new ArgumentException("Invalid respawn settings.");
    }
}

/// <summary>Detached lifecycle status. Each death captures its respawn policy; later configuration
/// edits apply to subsequent deaths. Administrative RespawnPlayer remains an explicit override.</summary>
public readonly record struct WorldLifeStatus(bool IsDead,int RespawnsUsed,WorldRespawnMode Mode,
    long? EligibleTick,bool ManualRequested,bool RespawnAllowed)
{
    internal WorldLifeStatus Die(long tick,WorldRespawnSettings settings)
    {
        settings.Validate();
        bool allowed=settings.Mode!=WorldRespawnMode.None&&(!settings.MaximumRespawns.HasValue||RespawnsUsed<settings.MaximumRespawns.Value);
        long? due=null;
        if(allowed && settings.Mode==WorldRespawnMode.Immediate) due=tick;
        if(allowed && settings.Mode==WorldRespawnMode.Delayed) due=checked(tick+settings.DelayTicks);
        return new(true,RespawnsUsed,settings.Mode,due,false,allowed);
    }
    internal bool CanAttempt(long tick)=>IsDead&&RespawnAllowed&&
        ((Mode==WorldRespawnMode.Manual&&ManualRequested)||(EligibleTick is long due&&tick>=due));
    internal WorldLifeStatus Revive(bool count)=>new(false,count?checked(RespawnsUsed+1):RespawnsUsed,WorldRespawnMode.None,null,false,false);
}

/// <summary>Optional global and per-level authored policy, independent of graphics configuration.</summary>
public sealed class WorldCrushSettingsFile
{
    public int Version { get; init; }=1;
    public WorldCrushPolicy Crush { get; init; }=WorldCrushPolicy.Death;
    public WorldRespawnSettings Respawn { get; init; }=new();
    public Dictionary<string,WorldCrushSettingsFile>? Levels { get; init; }
    public WorldCrushSettingsFile ForLevel(string level)=>Levels is not null&&Levels.TryGetValue(level,out var specific)?specific:this;
    public static WorldCrushSettingsFile FromJson(string json)
    {
        var options=new JsonSerializerOptions {UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false));
        var result=JsonSerializer.Deserialize<WorldCrushSettingsFile>(json,options)??throw new JsonException("Missing crush policy.");
        result.Validate();return result;
    }
    private void Validate()
    {
        if(Version!=1||!Enum.IsDefined(Crush)||Respawn is null) throw new JsonException("Invalid crush policy.");
        Respawn.Validate();
        if(Levels is null)return;
        foreach(var entry in Levels)
        {
            if(string.IsNullOrWhiteSpace(entry.Key)||entry.Value is null||entry.Value.Levels is not null)throw new JsonException("Invalid level override.");
            entry.Value.Validate();
        }
    }
}
