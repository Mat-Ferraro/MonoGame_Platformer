using System;

namespace PlatformerSandbox.Gameplay.GameFlow;

/// <summary>
/// Headless, presentation-clock transition gate. The coordinator still owns
/// validation, scene preparation, commit, and disposal; this object only
/// schedules when a pending request may be committed and supplies visual alpha.
/// </summary>
public enum SceneCurtainPhase
{
    Idle,
    FadingOut,
    Covered,
    FadingIn
}

/// <summary>
/// An intentionally small optional fade. Once fully covered, one Draw may
/// present the covered state before preparation/commit occurs at the next
/// safe Update boundary. There is no simulated loading percentage or timer
/// used to delay actual scene preparation.
/// </summary>
public sealed class SceneTransitionCurtain
{
    private readonly double _fadeSeconds;
    private double _elapsed;

    public SceneTransitionCurtain(double fadeSeconds = 0.12)
    {
        if (!double.IsFinite(fadeSeconds) || fadeSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(fadeSeconds));
        _fadeSeconds = fadeSeconds;
    }

    public SceneCurtainPhase Phase { get; private set; } = SceneCurtainPhase.Idle;
    public bool IsActive => Phase != SceneCurtainPhase.Idle;
    public bool IsCovered => Phase == SceneCurtainPhase.Covered;
    public bool? LastSucceeded { get; private set; }

    public float Opacity => Phase switch
    {
        SceneCurtainPhase.Idle => 0f,
        SceneCurtainPhase.FadingOut => (float)Math.Clamp(_elapsed / _fadeSeconds, 0, 1),
        SceneCurtainPhase.Covered => 1f,
        SceneCurtainPhase.FadingIn => (float)(1 - Math.Clamp(_elapsed / _fadeSeconds, 0, 1)),
        _ => throw new InvalidOperationException("Unknown curtain phase.")
    };

    public void Begin()
    {
        if (IsActive)
            throw new InvalidOperationException("A scene transition is already in progress.");
        Phase = SceneCurtainPhase.FadingOut;
        _elapsed = 0;
        LastSucceeded = null;
    }

    /// <summary>
    /// Returns true only in the already-covered phase, so the host can
    /// prepare/commit synchronously at an Update boundary. The frame that
    /// first finishes fading out does not also prepare the next world.
    /// </summary>
    public bool Advance(double presentationSeconds)
    {
        if (!double.IsFinite(presentationSeconds) || presentationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(presentationSeconds));
        if (Phase == SceneCurtainPhase.Covered)
            return true;
        if (Phase is not (SceneCurtainPhase.FadingOut or SceneCurtainPhase.FadingIn))
            return false;

        _elapsed = Math.Min(_elapsed + presentationSeconds, _fadeSeconds);
        if (_elapsed >= _fadeSeconds)
        {
            Phase = Phase == SceneCurtainPhase.FadingOut
                ? SceneCurtainPhase.Covered : SceneCurtainPhase.Idle;
            _elapsed = 0;
        }
        return false;
    }

    public void Complete(bool succeeded)
    {
        if (Phase != SceneCurtainPhase.Covered)
            throw new InvalidOperationException("Cannot complete an uncovered transition.");
        LastSucceeded = succeeded;
        Phase = SceneCurtainPhase.FadingIn;
        _elapsed = 0;
    }

    /// <summary>Used only for explicit immediate/developer commits.</summary>
    public void Reset()
    {
        Phase = SceneCurtainPhase.Idle;
        _elapsed = 0;
    }
}
