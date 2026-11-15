using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class MovementRequestResolverTests
{
    [Fact]
    public void EmptyFrameIsNeutralWithNoJumpEvents()
    {
        Assert.Equal(new MovementFrameIntent(HorizontalDirection.Neutral,
            false, false), MovementRequestResolver.Resolve([]));
    }

    [Fact]
    public void HighestPriorityHorizontalRequestWinsRegardlessOfEnumerationOrder()
    {
        MovementRequest[] forward = [
            MovementRequest.Horizontal(3, HorizontalDirection.Left, 1),
            MovementRequest.Horizontal(5, HorizontalDirection.Right, 10),
            MovementRequest.Horizontal(2, HorizontalDirection.Neutral, 5)];
        MovementFrameIntent expected = MovementRequestResolver.Resolve(forward);
        Assert.Equal(HorizontalDirection.Right, expected.Direction);
        Assert.Equal(expected, MovementRequestResolver.Resolve(forward.Reverse().ToArray()));
    }

    [Fact]
    public void EqualPriorityUsesStableSourceIdEvenIfSourceWithLowerIdIsLast()
    {
        MovementRequest[] requests = [
            MovementRequest.Horizontal(12, HorizontalDirection.Right, 7),
            MovementRequest.Horizontal(3, HorizontalDirection.Left, 7)];
        Assert.Equal(HorizontalDirection.Left,
            MovementRequestResolver.Resolve(requests).Direction);
        Assert.Equal(HorizontalDirection.Left,
            MovementRequestResolver.Resolve(requests.Reverse().ToArray()).Direction);
    }

    [Fact]
    public void HighPriorityNeutralCanGateMovementWithoutTouchingTheMotor()
    {
        MovementFrameIntent frame = MovementRequestResolver.Resolve([
            MovementRequest.Horizontal(1, HorizontalDirection.Right),
            MovementRequest.Horizontal(10, HorizontalDirection.Neutral, 100)]);
        Assert.Equal(HorizontalDirection.Neutral, frame.Direction);
    }

    [Fact]
    public void IndependentJumpPressAndReleaseEdgesCanCoexist()
    {
        MovementFrameIntent frame = MovementRequestResolver.Resolve([
            MovementRequest.JumpRelease(1),
            MovementRequest.Horizontal(1, HorizontalDirection.Right),
            MovementRequest.JumpPress(1)]);
        Assert.Equal(HorizontalDirection.Right, frame.Direction);
        Assert.True(frame.JumpPressed);
        Assert.True(frame.JumpReleased);
    }

    [Fact]
    public void RejectsDuplicateSourceAndAction()
    {
        Assert.Throws<ArgumentException>(() => MovementRequestResolver.Resolve([
            MovementRequest.Horizontal(1, HorizontalDirection.Right),
            MovementRequest.Horizontal(1, HorizontalDirection.Left)]));
    }

    [Fact]
    public void RejectsInvalidSourcePriorityKindOrDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementRequestResolver.Resolve([
            MovementRequest.Horizontal(0, HorizontalDirection.Right)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementRequestResolver.Resolve([
            MovementRequest.Horizontal(1, HorizontalDirection.Right, -1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementRequestResolver.Resolve([
            new MovementRequest((MovementRequestKind)123, 1, 0,
                HorizontalDirection.Neutral)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementRequestResolver.Resolve([
            MovementRequest.Horizontal(1, (HorizontalDirection)123)]));
        Assert.Throws<ArgumentException>(() => MovementRequestResolver.Resolve([
            new MovementRequest(MovementRequestKind.JumpPress, 1, 0,
                HorizontalDirection.Left)]));
        Assert.Throws<ArgumentNullException>(() =>
            MovementRequestResolver.Resolve(null!));
    }

    [Fact]
    public void TypedAndLegacyInputsPreserveAuthoritativeMovementAndTiming()
    {
        var legacy = new MovementGymSession();
        var typed = new MovementGymSession();
        for (int frame = 0; frame < 48; frame++)
        {
            HorizontalDirection direction = frame < 20
                ? HorizontalDirection.Right : HorizontalDirection.Neutral;
            bool press = frame == 3;
            bool release = frame == 8;
            var requests = new List<MovementRequest>
            {
                MovementRequest.Horizontal(MovementRequestSources.Keyboard, direction)
            };
            if (press) requests.Add(MovementRequest.JumpPress(MovementRequestSources.Keyboard));
            if (release) requests.Add(MovementRequest.JumpRelease(MovementRequestSources.Keyboard));

            var a = legacy.Step(MovementGymSession.FixedStepSeconds,
                direction, press, release);
            var b = typed.Step(MovementGymSession.FixedStepSeconds, requests);
            Assert.Equal(a.Bounds, b.Bounds);
            Assert.Equal(a.Velocity, b.Velocity);
            Assert.Equal(a.IsGrounded, b.IsGrounded);
            Assert.Equal(a.JumpStarted, b.JumpStarted);
            Assert.Equal(legacy.ElapsedSeconds, typed.ElapsedSeconds);
            Assert.Equal(legacy.MovingSupportId, typed.MovingSupportId);
        }
    }

    [Fact]
    public void InvalidRequestIsRejectedBeforeAdvancingTheGym()
    {
        var gym = new MovementGymSession();
        var bounds = gym.PlayerBounds;
        var time = gym.ElapsedSeconds;
        Assert.Throws<ArgumentException>(() => gym.Step(
            MovementGymSession.FixedStepSeconds,
            new[] {
                MovementRequest.JumpPress(2),
                MovementRequest.JumpPress(2)
            }));
        Assert.Equal(bounds, gym.PlayerBounds);
        Assert.Equal(time, gym.ElapsedSeconds);
        Assert.Equal(0, gym.JumpsStarted);
    }

    [Fact]
    public void TypedZeroDurationObservationNeverConsumesJumpPress()
    {
        var gym = new MovementGymSession();
        var input = new[] { MovementRequest.JumpPress(1) };
        var snapshot = gym.Step(0.0, input);
        Assert.False(snapshot.JumpStarted);
        Assert.Equal(0, gym.ElapsedSeconds);
        Assert.Equal(0, gym.JumpsStarted);
        Assert.True(gym.Step(MovementGymSession.FixedStepSeconds, input).JumpStarted);
    }

    [Fact]
    public void PlayablePresetStartsOnWhenRequestedAndSurvivesResetAndTuning()
    {
        Assert.True(MovementGymSession.PlayableAssistancePreset.AnyEnabled);
        var gym = new MovementGymSession(
            assistancePolicy: MovementGymSession.PlayableAssistancePreset);
        Assert.True(gym.AssistancePolicy.AnyEnabled);
        gym.Reset();
        Assert.True(gym.AssistancePolicy.AnyEnabled);
        gym.ApplyTuning(MovementGymTuning.Responsive);
        Assert.True(gym.AssistancePolicy.AnyEnabled);
        gym.SetAssistancePolicy(CollisionAssistancePolicy.Disabled);
        Assert.False(gym.AssistancePolicy.AnyEnabled);
        gym.SetAssistancePolicy(MovementGymSession.PlayableAssistancePreset);
        Assert.True(gym.AssistancePolicy.AnyEnabled);

        // Existing unassisted headless fixtures must remain opt-in.
        Assert.False(new MovementGymSession().AssistancePolicy.AnyEnabled);
    }
}
