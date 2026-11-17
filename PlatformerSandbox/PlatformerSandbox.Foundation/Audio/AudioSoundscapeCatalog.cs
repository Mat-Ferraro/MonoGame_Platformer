using System.Text.Json;

namespace PlatformerSandbox.Foundation.Audio;

/// <summary>Authorable sound source bound to a level; units are the same as collision/world units.</summary>
public sealed record AudioSoundscapeEmitter(string Id, string CueId, AudioEmitterPosition Position);

public sealed record AudioSoundscape(string LevelId, string? TrackId,
    IReadOnlyList<AudioSoundscapeEmitter> Emitters);

/// <summary>Pure JSON content contract; actual GPU/native audio stays in the platform host.</summary>
public sealed class AudioSoundscapeCatalog
{
    private readonly Dictionary<string, AudioSoundscape> _scenes;
    public AudioSoundscapeCatalog(IEnumerable<AudioSoundscape> soundscapes)
    {
        ArgumentNullException.ThrowIfNull(soundscapes);
        _scenes = new Dictionary<string, AudioSoundscape>(StringComparer.OrdinalIgnoreCase);
        foreach (AudioSoundscape item in soundscapes)
        {
            if (!AudioCueDefinition.IsAssetId(item.LevelId) ||
                item.TrackId is not null && !AudioCueDefinition.IsAssetId(item.TrackId))
                throw new ArgumentException("Invalid soundscape level or music ID.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AudioSoundscapeEmitter emitter in item.Emitters)
            {
                if (!AudioCueDefinition.IsAssetId(emitter.Id) ||
                    !AudioCueDefinition.IsAssetId(emitter.CueId) || !ids.Add(emitter.Id))
                    throw new ArgumentException("Duplicate or invalid soundscape emitter ID.");
                AudioSpatializer.Validate(emitter.Position);
            }
            if (!_scenes.TryAdd(item.LevelId, item))
                throw new ArgumentException("Duplicate soundscape: " + item.LevelId);
        }
    }
    public bool TryGet(string levelId, out AudioSoundscape soundscape) =>
        _scenes.TryGetValue(levelId, out soundscape!);
    public static AudioSoundscapeCatalog FromJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.GetProperty("version").GetInt32() != 1)
            throw new FormatException("Unsupported soundscape version.");
        var scenes = new List<AudioSoundscape>();
        foreach (JsonElement element in root.GetProperty("scenes").EnumerateArray())
        {
            string level = element.GetProperty("level").GetString() ?? "";
            string? music = element.TryGetProperty("music", out JsonElement song) &&
                song.ValueKind != JsonValueKind.Null ? song.GetString() : null;
            var emitters = new List<AudioSoundscapeEmitter>();
            if (element.TryGetProperty("emitters", out JsonElement list))
            {
                foreach (JsonElement e in list.EnumerateArray())
                    emitters.Add(new AudioSoundscapeEmitter(
                        e.GetProperty("id").GetString() ?? "",
                        e.GetProperty("cue").GetString() ?? "",
                        new AudioEmitterPosition(e.GetProperty("x").GetDouble(),
                            e.GetProperty("y").GetDouble(),
                            e.TryGetProperty("innerRadius", out var inner) ? inner.GetDouble() : 1.5,
                            e.TryGetProperty("outerRadius", out var outer) ? outer.GetDouble() : 16)));
            }
            scenes.Add(new AudioSoundscape(level, music, emitters.AsReadOnly()));
        }
        return new AudioSoundscapeCatalog(scenes);
    }
}
