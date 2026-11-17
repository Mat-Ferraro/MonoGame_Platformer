namespace PlatformerSandbox.Foundation.Audio;

/// <summary>Backend-independent logical mixer. Volumes are linear gains, not loudness units.</summary>
public sealed class AudioMixer
{
    private readonly Dictionary<AudioCategory, float> _gains = new();
    public float MasterVolume { get; private set; } = 0.75f;
    public AudioMixer()
    {
        foreach (AudioCategory category in Enum.GetValues<AudioCategory>()) _gains[category] = 1f;
        _gains[AudioCategory.Music] = 0.6f;
        _gains[AudioCategory.Ambience] = 0.65f;
    }

    public void SetMasterVolume(float volume) => MasterVolume = Validate(volume);
    public void SetCategoryVolume(AudioCategory category, float volume)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        _gains[category] = Validate(volume);
    }
    public float GetCategoryVolume(AudioCategory category) => _gains[category];
    public float EffectiveGain(AudioCategory category, float cueGain, bool worldPaused = false)
    {
        if (!float.IsFinite(cueGain) || cueGain < 0 || cueGain > 1)
            throw new ArgumentOutOfRangeException(nameof(cueGain));
        if (worldPaused && category is (AudioCategory.Gameplay or AudioCategory.Ambience))
            return 0;
        return MasterVolume * _gains[category] * cueGain;
    }
    private static float Validate(float volume) =>
        float.IsFinite(volume) && volume is >= 0 and <= 1
            ? volume : throw new ArgumentOutOfRangeException(nameof(volume));
}
