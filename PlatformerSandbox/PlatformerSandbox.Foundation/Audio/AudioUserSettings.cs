using System.Text.Json;

namespace PlatformerSandbox.Foundation.Audio;

/// <summary>Portable, strictly validated volume preferences. Mute remains session-scoped.</summary>
public sealed class AudioUserSettings
{
    public const int CurrentVersion = 1;
    public float Master { get; init; } = .75f;
    public float Music { get; init; } = .6f;
    public float Gameplay { get; init; } = 1f;
    public float Ambience { get; init; } = .65f;
    public float Ui { get; init; } = 1f;
    public float Voice { get; init; } = 1f;

    public void Validate()
    {
        foreach (float gain in new[] { Master, Music, Gameplay, Ambience, Ui, Voice })
            if (!float.IsFinite(gain) || gain is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(gain),
                    "Audio volumes must be finite values between zero and one.");
    }

    public void Apply(AudioService audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        Validate();
        audio.SetMasterVolume(Master);
        audio.SetCategoryVolume(AudioCategory.Music, Music);
        audio.SetCategoryVolume(AudioCategory.Gameplay, Gameplay);
        audio.SetCategoryVolume(AudioCategory.Ambience, Ambience);
        audio.SetCategoryVolume(AudioCategory.Ui, Ui);
        audio.SetCategoryVolume(AudioCategory.Voice, Voice);
    }

    public static AudioUserSettings FromMixer(AudioMixer mixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        return new AudioUserSettings
        {
            Master = mixer.MasterVolume,
            Music = mixer.GetCategoryVolume(AudioCategory.Music),
            Gameplay = mixer.GetCategoryVolume(AudioCategory.Gameplay),
            Ambience = mixer.GetCategoryVolume(AudioCategory.Ambience),
            Ui = mixer.GetCategoryVolume(AudioCategory.Ui),
            Voice = mixer.GetCategoryVolume(AudioCategory.Voice)
        };
    }
}

/// <summary>Small standalone JSON store; host chooses the local app-data path.</summary>
public static class AudioSettingsStore
{
    public static AudioUserSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return new AudioUserSettings();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.GetProperty("version").GetInt32() != AudioUserSettings.CurrentVersion)
                throw new FormatException("Unsupported audio settings version.");
            float Read(string key, float fallback) => root.TryGetProperty(key, out var value) ?
                value.GetSingle() : fallback;
            var settings = new AudioUserSettings
            {
                Master = Read("master", .75f), Music = Read("music", .6f),
                Gameplay = Read("gameplay", 1f), Ambience = Read("ambience", .65f),
                Ui = Read("ui", 1f), Voice = Read("voice", 1f)
            };
            settings.Validate();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine("A02: invalid or inaccessible audio settings, using defaults: " + ex);
            return new AudioUserSettings();
        }
    }

    public static void Save(string path, AudioUserSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null) throw new ArgumentException("Settings path needs a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var document = new
        {
            version = AudioUserSettings.CurrentVersion,
            master = settings.Master, music = settings.Music,
            gameplay = settings.Gameplay, ambience = settings.Ambience,
            ui = settings.Ui, voice = settings.Voice
        };
        string temp = Path.Combine(directory, ".audio-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
