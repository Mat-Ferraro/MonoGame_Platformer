using PlatformerSandbox.Foundation.Audio;
using Xunit;

namespace PlatformerSandbox.Tests.Audio;

public sealed class A01AudioSystemTests
{
    private static AudioCueCatalog Catalog(params AudioCueDefinition[] cues) => new(cues);
    private static AudioCueDefinition Cue(string id, AudioCategory category = AudioCategory.Gameplay,
        int priority = 10, int maxVoices = 4, double cooldown = 0, bool loop = false) =>
        new(id, category, new[] { id + "_a", id + "_b" }, 0.8f,
            -0.1f, 0.1f, priority, maxVoices, cooldown, loop);

    [Fact]
    public void Catalog_ParsesManifestAndRejectsBadNamesAndDuplicates()
    {
        const string json = @"{""version"":1,""cues"":[{""id"":""player.jump"",""category"":""Gameplay"",
          ""samples"":[""jump_a"",""jump_b""],""gain"":0.5,""maxVoices"":2,
          ""priority"":8,""cooldownSeconds"":0.1,""pitchMin"":-0.2,""pitchMax"":0.1}]}";
        var catalog = AudioCueCatalog.FromJson(json);
        Assert.True(catalog.TryGet("PLAYER.JUMP", out var cue));
        Assert.Equal(2, cue.Samples.Count);
        Assert.Equal(.5f, cue.Gain);
        Assert.Throws<ArgumentException>(() => Catalog(Cue("repeat"), Cue("repeat")));
        Assert.Throws<ArgumentException>(() => new AudioCueDefinition("path/slash",
            AudioCategory.Gameplay, new[] { "sample" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioCueDefinition("invalidGain",
            AudioCategory.Gameplay, new[] { "sample" }, gain: float.NaN));
    }

    [Fact]
    public void CommittedEventKeyIsPlayedOnceAndPerSourceCooldownApplies()
    {
        var backend = new FakeBackend();
        using var audio = new AudioService(Catalog(Cue("jump", cooldown: .2)), backend, randomSeed: 3);
        Assert.Equal(AudioPlayResult.Played,
            audio.Play(new AudioCueRequest("jump", SourceId: "P1", EventKey: "tick-1")));
        Assert.Equal(AudioPlayResult.DuplicateEvent,
            audio.Play(new AudioCueRequest("jump", SourceId: "P1", EventKey: "tick-1")));
        Assert.Equal(AudioPlayResult.Cooldown,
            audio.Play(new AudioCueRequest("jump", SourceId: "P1", EventKey: "tick-2")));
        audio.Update(.21);
        Assert.Equal(AudioPlayResult.Played,
            audio.Play(new AudioCueRequest("jump", SourceId: "P1", EventKey: "tick-2")));
        Assert.Equal(2, backend.Played.Count);
        Assert.All(backend.Played, p => Assert.InRange(p.Pitch, -.1f, .1f));
    }

    [Fact]
    public void VoiceBudgetRejectsLowPriorityAndStealsLowerPriority()
    {
        var backend = new FakeBackend();
        using var audio = new AudioService(Catalog(
            Cue("quiet", priority: 1), Cue("important", priority: 90)), backend, maxVoices: 1);
        Assert.Equal(AudioPlayResult.Played, audio.Play(new AudioCueRequest("quiet")));
        Assert.Equal(AudioPlayResult.VoiceLimit, audio.Play(new AudioCueRequest("quiet")));
        Assert.Equal(AudioPlayResult.Played, audio.Play(new AudioCueRequest("important")));
        Assert.True(backend.Played[0].Stopped);
        Assert.Equal(1, audio.ActiveVoiceCount);
        Assert.Equal(1, audio.DeniedVoiceCount);
    }

    [Fact]
    public void LoopOwnerCleanupDoesNotStopApplicationOrUiVoices()
    {
        var backend = new FakeBackend();
        using var audio = new AudioService(Catalog(
            Cue("ambience", AudioCategory.Ambience, loop: true),
            Cue("menu", AudioCategory.Ui)), backend);
        audio.Play(new AudioCueRequest("ambience", Owner: "level"));
        audio.Play(new AudioCueRequest("menu", Owner: "ui"));
        audio.StopOwner("level");
        Assert.True(backend.Played[0].Stopped);
        Assert.False(backend.Played[1].Stopped);
        Assert.Equal(1, audio.ActiveVoiceCount);
    }

    [Fact]
    public void PauseAndMixControlsMuteWorldWithoutMutingUi()
    {
        var backend = new FakeBackend();
        using var audio = new AudioService(Catalog(
            Cue("amb", AudioCategory.Ambience, loop: true),
            Cue("menu", AudioCategory.Ui)), backend);
        audio.Play(new AudioCueRequest("amb", Owner: "level"));
        audio.Play(new AudioCueRequest("menu", Owner: "ui"));
        audio.SetWorldPaused(true);
        Assert.Equal(0, backend.Played[0].Volume);
        Assert.True(backend.Played[1].Volume > 0);
        audio.SetWorldPaused(false);
        Assert.True(backend.Played[0].Volume > 0);
        audio.SetCategoryVolume(AudioCategory.Ui, .25f);
        Assert.Equal(.75f * .25f * .8f, backend.Played[1].Volume, 5);
        audio.SetMute(true);
        Assert.All(backend.Played, p => Assert.Equal(0, p.Volume));
    }

    [Fact]
    public void PausedSceneCanStartMutedLoopThenRestoreItOnResume()
    {
        var backend = new FakeBackend();
        using var audio = new AudioService(Catalog(
            Cue("amb", AudioCategory.Ambience, loop: true)), backend);
        audio.SetWorldPaused(true);
        Assert.Equal(AudioPlayResult.Played,
            audio.Play(new AudioCueRequest("amb", Owner: "level")));
        Assert.Equal(0, backend.Played[0].Volume);
        audio.SetWorldPaused(false);
        Assert.True(backend.Played[0].Volume > 0);
        audio.SetMute(true);
        Assert.Equal(0, backend.Played[0].Volume);
        audio.SetMute(false);
        Assert.True(backend.Played[0].Volume > 0);
    }

    [Fact]
    public void DisabledBackendCannotAffectGameplayOrThrow()
    {
        var backend = new FakeBackend { Fail = true };
        using var audio = new AudioService(Catalog(Cue("jump")), backend);
        Assert.Equal(AudioPlayResult.BackendUnavailable,
            audio.Play(new AudioCueRequest("jump", EventKey: "world-1-10-2")));
        Assert.Equal(0, audio.ActiveVoiceCount);
    }

    [Fact]
    public void CompletedVoicesReleaseCapacityAndInvalidTimeIsRejected()
    {
        var backend = new FakeBackend();
        using var audio = new AudioService(Catalog(Cue("sound", maxVoices: 1)), backend);
        Assert.Equal(AudioPlayResult.Played, audio.Play(new AudioCueRequest("sound")));
        backend.Played[0].Stopped = true;
        audio.Update(.016);
        Assert.Equal(0, audio.ActiveVoiceCount);
        Assert.Equal(AudioPlayResult.Played, audio.Play(new AudioCueRequest("sound")));
        Assert.Throws<ArgumentOutOfRangeException>(() => audio.Update(double.NaN));
    }

    [Fact]
    public void MusicDirectorDoesNotRestartSameTrackAndUsesSequentialFades()
    {
        var backend = new FakeMusicBackend();
        using var director = new MusicDirector(backend);
        director.Request("title", 0);
        director.Request("title", 0);
        Assert.Equal(new[] { "title" }, backend.Played);
        director.Request("cavern", 1.0);
        director.Update(.5);
        Assert.Equal("title", director.ActiveTrack);
        Assert.InRange(backend.Volume, 0.49f, 0.51f);
        director.Update(.5);
        Assert.Equal("cavern", director.ActiveTrack);
        Assert.Equal(0, backend.Volume);
        director.Update(1);
        Assert.Equal(1, backend.Volume);
        Assert.Equal(new[] { "title", "cavern" }, backend.Played);
    }

    private sealed class FakeBackend : IAudioPlaybackBackend
    {
        public bool Fail;
        public List<FakeVoice> Played { get; } = new();
        public IAudioVoice? Play(string sampleId, float volume, float pitch, bool loop)
        {
            if (Fail) return null;
            var voice = new FakeVoice { Volume = volume, Pitch = pitch };
            Played.Add(voice);
            return voice;
        }
        public void Dispose() { }
    }
    private sealed class FakeVoice : IAudioVoice
    {
        public float Volume;
        public float Pitch;
        public bool Stopped;
        public bool IsPlaying => !Stopped;
        public void SetVolume(float volume) => Volume = volume;
        public void Stop() => Stopped = true;
        public void Dispose() => Stopped = true;
    }
    private sealed class FakeMusicBackend : IMusicPlaybackBackend
    {
        public List<string> Played { get; } = new();
        public float Volume;
        public void Play(string trackId, bool repeat) => Played.Add(trackId);
        public void Stop() { }
        public void Pause() { }
        public void Resume() { }
        public void SetVolume(float gain) => Volume = gain;
        public void Dispose() { }
    }
}
