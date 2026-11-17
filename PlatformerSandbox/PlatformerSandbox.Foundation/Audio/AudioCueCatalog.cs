using System.Text.Json;

namespace PlatformerSandbox.Foundation.Audio;

public enum AudioCategory { Music, Gameplay, Ambience, Ui, Voice }

/// <summary>Immutable, device-agnostic cue definition. Sample IDs are logical
/// asset names, not file paths. Validation happens before the first voice plays.</summary>
public sealed class AudioCueDefinition
{
    public string Id { get; }
    public AudioCategory Category { get; }
    public IReadOnlyList<string> Samples { get; }
    public float Gain { get; }
    public float PitchMinimum { get; }
    public float PitchMaximum { get; }
    public int Priority { get; }
    public int MaxVoices { get; }
    public double CooldownSeconds { get; }
    public bool Loop { get; }

    public AudioCueDefinition(string id, AudioCategory category, IEnumerable<string> samples,
        float gain = 1f, float pitchMinimum = 0, float pitchMaximum = 0,
        int priority = 0, int maxVoices = 4, double cooldownSeconds = 0,
        bool loop = false)
    {
        if (!IsAssetId(id)) throw new ArgumentException("Invalid cue ID", nameof(id));
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        ArgumentNullException.ThrowIfNull(samples);
        string[] array = samples.ToArray();
        if (array.Length == 0 || array.Any(s => !IsAssetId(s)))
            throw new ArgumentException("Cue must have safe sample names", nameof(samples));
        if (!float.IsFinite(gain) || gain < 0 || gain > 1 ||
            !float.IsFinite(pitchMinimum) || !float.IsFinite(pitchMaximum) ||
            pitchMinimum < -1 || pitchMaximum > 1 || pitchMinimum > pitchMaximum)
            throw new ArgumentOutOfRangeException(nameof(gain), "Invalid cue gain/pitch range");
        if (maxVoices < 1 || maxVoices > 64) throw new ArgumentOutOfRangeException(nameof(maxVoices));
        if (priority < 0 || priority > 100) throw new ArgumentOutOfRangeException(nameof(priority));
        if (!double.IsFinite(cooldownSeconds) || cooldownSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
        Id = id;
        Category = category;
        Samples = Array.AsReadOnly(array);
        Gain = gain;
        PitchMinimum = pitchMinimum;
        PitchMaximum = pitchMaximum;
        Priority = priority;
        MaxVoices = maxVoices;
        CooldownSeconds = cooldownSeconds;
        Loop = loop;
    }

    public static bool IsAssetId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 96 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}

/// <summary>Read-only cue catalog loaded from a strict, versioned JSON manifest.</summary>
public sealed class AudioCueCatalog
{
    private readonly Dictionary<string, AudioCueDefinition> _cues;
    public IReadOnlyCollection<AudioCueDefinition> Cues => _cues.Values;

    public AudioCueCatalog(IEnumerable<AudioCueDefinition> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        _cues = new Dictionary<string, AudioCueDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (AudioCueDefinition cue in cues)
            if (!_cues.TryAdd(cue.Id, cue))
                throw new ArgumentException("Duplicate cue ID: " + cue.Id, nameof(cues));
    }

    public bool TryGet(string id, out AudioCueDefinition cue) =>
        _cues.TryGetValue(id, out cue!);

    public static AudioCueCatalog FromJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("version").GetInt32() != 1)
            throw new FormatException("Unsupported audio manifest version");
        var cues = new List<AudioCueDefinition>();
        foreach (JsonElement item in root.GetProperty("cues").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString() ?? "";
            string categoryString = item.GetProperty("category").GetString() ?? "";
            if (!Enum.TryParse(categoryString, true, out AudioCategory category) ||
                !Enum.IsDefined(category))
                throw new FormatException("Unknown audio category: " + categoryString);
            string[] samples = item.GetProperty("samples").EnumerateArray()
                .Select(x => x.GetString() ?? "").ToArray();
            float gain = OptionalSingle(item, "gain", 1f);
            float minPitch = OptionalSingle(item, "pitchMin", 0f);
            float maxPitch = OptionalSingle(item, "pitchMax", 0f);
            int priority = OptionalInt(item, "priority", 0);
            int maxVoices = OptionalInt(item, "maxVoices", 4);
            double cooldownSeconds = OptionalDouble(item, "cooldownSeconds", 0);
            bool loop = item.TryGetProperty("loop", out JsonElement loopValue) && loopValue.GetBoolean();
            cues.Add(new AudioCueDefinition(id, category, samples, gain, minPitch,
                maxPitch, priority, maxVoices, cooldownSeconds, loop));
        }
        return new AudioCueCatalog(cues);
    }

    private static float OptionalSingle(JsonElement item, string name, float otherwise) =>
        item.TryGetProperty(name, out JsonElement value) ? value.GetSingle() : otherwise;
    private static int OptionalInt(JsonElement item, string name, int otherwise) =>
        item.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : otherwise;
    private static double OptionalDouble(JsonElement item, string name, double otherwise) =>
        item.TryGetProperty(name, out JsonElement value) ? value.GetDouble() : otherwise;
}
