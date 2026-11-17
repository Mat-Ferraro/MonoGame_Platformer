using PlatformerSandbox.Foundation.Audio;
using Xunit;

namespace PlatformerSandbox.Tests.Audio;

public sealed class A02SpatialSettingsAndSoundscapesTests
{
    [Fact]
    public void SpatialAttenuationAndPanRespectWorldCoordinatesAndClipping()
    {
        var center = new AudioListener(0, 0);
        var nearRight = new AudioEmitterPosition(5, 0, 1, 10);
        var nearLeft = new AudioEmitterPosition(-5, 0, 1, 10);
        AudioSpatialMix right = AudioSpatializer.Evaluate(center, nearRight);
        AudioSpatialMix left = AudioSpatializer.Evaluate(center, nearLeft);
        Assert.InRange(right.Gain, .2f, .4f);
        Assert.True(right.Pan > 0);
        Assert.Equal(right.Gain, left.Gain, 5);
        Assert.True(left.Pan < 0);
        Assert.Equal(0f, AudioSpatializer.Evaluate(center,
            new AudioEmitterPosition(10, 0, 1, 10)).Gain);
        Assert.Equal(new AudioSpatialMix(1, 0),
            AudioSpatializer.Evaluate(null, nearRight));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioSpatializer.Evaluate(center, new AudioEmitterPosition(0, 0, 10, 5)));
    }

    [Fact]
    public void ListenerMovementRemixesExistingLoopWithoutRestartingIt()
    {
        var backend = new FakeBackend();
        var cue = new AudioCueDefinition("wind", AudioCategory.Ambience,
            new[] { "wind_sample" }, loop: true);
        using var audio = new AudioService(new AudioCueCatalog(new[] { cue }), backend);
        audio.SetListener(new AudioListener(0, 0));
        Assert.Equal(AudioPlayResult.Played, audio.Play(new AudioCueRequest("wind",
            Spatial: new AudioEmitterPosition(8, 0, 1, 10))));
        var voice = Assert.Single(backend.Voices);
        float far = voice.Gain;
        Assert.True(voice.Pan > 0);
        audio.SetListener(new AudioListener(8, 0));
        Assert.True(voice.Gain > far);
        Assert.Equal(0f, voice.Pan);
        audio.SetListener(new AudioListener(18, 0));
        Assert.Equal(0f, voice.Gain);
        Assert.Single(backend.Voices); // No repeated Play and no simulation effect.
    }

    [Fact]
    public void NonSpatialUiRetainsFullGainAndUnaffectedPan()
    {
        var backend = new FakeBackend();
        using var audio = new AudioService(new AudioCueCatalog(new[]
        {
            new AudioCueDefinition("ui", AudioCategory.Ui, new[] { "ui_sample" })
        }), backend);
        audio.SetListener(new AudioListener(1000, -1000));
        Assert.Equal(AudioPlayResult.Played,
            audio.Play(new AudioCueRequest("ui", Owner: "ui")));
        Assert.Equal(0f, Assert.Single(backend.Voices).Pan);
        Assert.Equal(.75f, backend.Voices[0].Gain, 4);
    }

    [Fact]
    public void SettingsPersistAtomicallyAndRoundtripAllCategories()
    {
        string folder = Path.Combine(Path.GetTempPath(), "A02_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "audio.json");
        try
        {
            var saved = new AudioUserSettings
            {
                Master = .3f, Music = .25f, Gameplay = .4f,
                Ambience = .5f, Ui = .6f, Voice = .7f
            };
            AudioSettingsStore.Save(path, saved);
            AudioUserSettings loaded = AudioSettingsStore.Load(path);
            var backend = new FakeBackend();
            using var audio = new AudioService(new AudioCueCatalog(
                Array.Empty<AudioCueDefinition>()), backend);
            loaded.Apply(audio);
            Assert.Equal(.3f, audio.Mixer.MasterVolume);
            Assert.Equal(.25f, audio.Mixer.GetCategoryVolume(AudioCategory.Music));
            Assert.Equal(.4f, audio.Mixer.GetCategoryVolume(AudioCategory.Gameplay));
            Assert.Equal(.5f, audio.Mixer.GetCategoryVolume(AudioCategory.Ambience));
            Assert.Equal(.6f, audio.Mixer.GetCategoryVolume(AudioCategory.Ui));
            Assert.Equal(.7f, audio.Mixer.GetCategoryVolume(AudioCategory.Voice));
            Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void CorruptOrOutOfRangeSettingsFallBackToSafeDefaults()
    {
        string folder = Path.Combine(Path.GetTempPath(), "A02_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "audio.json");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(path, "{not json");
            Assert.Equal(.75f, AudioSettingsStore.Load(path).Master);
            File.WriteAllText(path, "{\"version\":1,\"master\":-1}");
            Assert.Equal(.75f, AudioSettingsStore.Load(path).Master);
            File.WriteAllText(path, "{\"version\":22}");
            Assert.Equal(.75f, AudioSettingsStore.Load(path).Master);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AudioSettingsStore.Save(path, new AudioUserSettings { Music = float.NaN }));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void SoundscapeManifestParsesAndRejectsDuplicateSourcesAndBadRadii()
    {
        const string good = """
            {"version":1,"scenes":[{"level":"CAVERN_C","music":"music_cavern_c",
            "emitters":[{"id":"waterfall","cue":"ambience.air","x":14,"y":23,
            "innerRadius":1,"outerRadius":16}]}]}
            """;
        AudioSoundscapeCatalog catalog = AudioSoundscapeCatalog.FromJson(good);
        Assert.True(catalog.TryGet("cavern_c", out AudioSoundscape? scene));
        Assert.Equal("music_cavern_c", scene.TrackId);
        Assert.Equal(14, Assert.Single(scene.Emitters).Position.X);
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioSoundscapeCatalog.FromJson(
            good.Replace("\"outerRadius\":16", "\"outerRadius\":0")));
        Assert.Throws<ArgumentException>(() => AudioSoundscapeCatalog.FromJson(
            good.Replace("\"level\":\"CAVERN_C\"",
                "\"level\":\"../escape\"")));
        const string duplicate = """
            {"version":1,"scenes":[{"level":"CAVERN_C","emitters":[
            {"id":"air","cue":"ambience.air","x":1,"y":2},
            {"id":"air","cue":"ambience.air","x":3,"y":4}]}]}
            """;
        Assert.Throws<ArgumentException>(() => AudioSoundscapeCatalog.FromJson(duplicate));
    }

    [Fact]
    public void MusicRetargetDuringFadeInDoesNotJumpToFullVolume()
    {
        var backend = new FakeMusicBackend();
        using var director = new MusicDirector(backend);
        director.Request("title", 1);
        director.Update(.2);
        float before = backend.Gain;
        Assert.InRange(before, .19f, .21f);
        director.Request("cavern", 1);
        Assert.InRange(backend.Gain, before - .001f, before + .001f);
        director.Update(.5);
        Assert.True(backend.Gain < before);
        director.Update(.5);
        Assert.Equal("cavern", director.ActiveTrack);
        Assert.Equal(0, backend.Gain);
        director.Update(1);
        Assert.Equal(1, backend.Gain);
        Assert.Equal(new[] { "title", "cavern" }, backend.Played);
    }

    private sealed class FakeBackend : IAudioPlaybackBackend
    {
        public List<FakeVoice> Voices { get; } = new();
        public IAudioVoice Play(string sampleId, float volume, float pitch, bool loop)
        {
            var voice = new FakeVoice { Gain = volume };
            Voices.Add(voice);
            return voice;
        }
        public void Dispose() { }
    }
    private sealed class FakeVoice : IAudioVoice
    {
        public float Gain;
        public float Pan;
        public bool Stopped;
        public bool IsPlaying => !Stopped;
        public void SetVolume(float volume) => Gain = volume;
        public void SetPan(float pan) => Pan = pan;
        public void Stop() => Stopped = true;
        public void Dispose() => Stopped = true;
    }
    private sealed class FakeMusicBackend : IMusicPlaybackBackend
    {
        public List<string> Played { get; } = new();
        public float Gain;
        public void Play(string id, bool repeat) => Played.Add(id);
        public void Stop() { }
        public void Pause() { }
        public void Resume() { }
        public void SetVolume(float gain) => Gain = gain;
        public void Dispose() { }
    }
}
