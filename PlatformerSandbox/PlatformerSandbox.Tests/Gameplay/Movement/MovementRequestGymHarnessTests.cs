using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class MovementRequestGymHarnessTests
{
    private static MovementRequest[] Make(
        MovementRequestGymScenario scenario,
        HorizontalDirection keyboard = HorizontalDirection.Neutral,
        bool press = false, bool release = false, bool reverse = false)
    {
        var buffer = new List<MovementRequest>(5);
        MovementRequestGymHarness.Populate(buffer, scenario, keyboard,
            press, release, reverse);
        return buffer.ToArray();
    }

    [Theory]
    [InlineData(MovementRequestGymScenario.ControlLock, HorizontalDirection.Neutral, 20, 100)]
    [InlineData(MovementRequestGymScenario.ForceLeft, HorizontalDirection.Left, 21, 100)]
    [InlineData(MovementRequestGymScenario.ForceRight, HorizontalDirection.Right, 21, 100)]
    [InlineData(MovementRequestGymScenario.EqualPriorityTie, HorizontalDirection.Right, 30, 80)]
    [InlineData(MovementRequestGymScenario.KeyboardOnly, HorizontalDirection.Left, 1, 0)]
    public void ScenariosSelectExpectedPriorityAndSource(
        MovementRequestGymScenario scenario,
        HorizontalDirection expectedDirection, int source, int priority)
    {
        MovementResolution result = MovementRequestResolver.ResolveDetailed(
            Make(scenario, HorizontalDirection.Left));
        Assert.Equal(expectedDirection, result.Intent.Direction);
        Assert.Equal(source, result.WinningSourceId);
        Assert.Equal(priority, result.WinningPriority);
    }

    [Theory]
    [InlineData(MovementRequestGymScenario.ControlLock)]
    [InlineData(MovementRequestGymScenario.ForceLeft)]
    [InlineData(MovementRequestGymScenario.ForceRight)]
    [InlineData(MovementRequestGymScenario.EqualPriorityTie)]
    [InlineData(MovementRequestGymScenario.KeyboardOnly)]
    public void ReversingSubmissionOrderNeverChangesResolution(
        MovementRequestGymScenario scenario)
    {
        MovementResolution normal = MovementRequestResolver.ResolveDetailed(
            Make(scenario, HorizontalDirection.Right, press: true, release: true));
        MovementResolution reversed = MovementRequestResolver.ResolveDetailed(
            Make(scenario, HorizontalDirection.Right, press: true, release: true,
                reverse: true));
        Assert.Equal(normal, reversed);
    }

    [Fact]
    public void JumpEdgesSurviveMovementLock()
    {
        MovementResolution result = MovementRequestResolver.ResolveDetailed(
            Make(MovementRequestGymScenario.ControlLock,
                HorizontalDirection.Right, press: true, release: true));
        Assert.Equal(HorizontalDirection.Neutral, result.Intent.Direction);
        Assert.True(result.Intent.JumpPressed);
        Assert.True(result.Intent.JumpReleased);
        Assert.Equal(4, result.TotalRequestCount);
    }

    [Fact]
    public void TieIncludesTwoDifferentSourcesAtSamePriority()
    {
        MovementRequest[] requests = Make(
            MovementRequestGymScenario.EqualPriorityTie);
        Assert.Contains(requests, r => r.SourceId == 30 &&
            r.Priority == 80 && r.Direction == HorizontalDirection.Right);
        Assert.Contains(requests, r => r.SourceId == 31 &&
            r.Priority == 80 && r.Direction == HorizontalDirection.Left);
        Assert.Equal(3, MovementRequestResolver.ResolveDetailed(requests)
            .HorizontalRequestCount);
    }

    [Fact]
    public void KeyboardOnlyRetainsAllInputEdgesAndNoSyntheticSources()
    {
        MovementRequest[] requests = Make(
            MovementRequestGymScenario.KeyboardOnly,
            HorizontalDirection.Right, press: true, release: true);
        Assert.Equal(3, requests.Length);
        Assert.All(requests, r => Assert.Equal(1, r.SourceId));
        Assert.Equal(new MovementFrameIntent(HorizontalDirection.Right, true, true),
            MovementRequestResolver.Resolve(requests));
    }

    [Fact]
    public void CaseCyclingWrapsWithoutSkippingKeyboardOnly()
    {
        MovementRequestGymScenario scenario = MovementRequestGymScenario.ControlLock;
        var cases = new List<MovementRequestGymScenario>();
        for (int i = 0; i < 5; i++)
        {
            cases.Add(scenario);
            scenario = MovementRequestGymHarness.Next(scenario);
        }
        Assert.Equal(5, cases.Distinct().Count());
        Assert.Equal(MovementRequestGymScenario.ControlLock, scenario);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementRequestGymHarness.Next((MovementRequestGymScenario)99));
    }

    [Fact]
    public void InvalidScenarioFailsBeforeChangingScratchRequests()
    {
        var buffer = new List<MovementRequest> {
            MovementRequest.Horizontal(1, HorizontalDirection.Right)
        };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementRequestGymHarness.Populate(buffer,
                (MovementRequestGymScenario)99,
                HorizontalDirection.Right, false, false));
        Assert.Single(buffer);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementRequestGymHarness.Populate(buffer,
                MovementRequestGymScenario.ControlLock,
                (HorizontalDirection)99, false, false));
        Assert.Single(buffer);
    }

    [Fact]
    public void GymStepReportsExactlyTheIntentThatItExecuted()
    {
        var gym = new MovementGymSession();
        MovementRequest[] requests = Make(MovementRequestGymScenario.ForceRight,
            HorizontalDirection.Left, press: true);
        MovementResolution expected = MovementRequestResolver.ResolveDetailed(requests);
        var step = gym.Step(MovementGymSession.FixedStepSeconds,
            requests, out MovementResolution actual);
        Assert.Equal(expected, actual);
        Assert.Equal(step.Bounds, gym.PlayerBounds);
        Assert.Equal(step.Velocity, gym.PlayerVelocity);
        Assert.True(actual.Intent.JumpPressed);
        Assert.True(step.JumpStarted);
    }

    [Theory]
    [InlineData(MovementRequestGymScenario.ControlLock)]
    [InlineData(MovementRequestGymScenario.ForceRight)]
    [InlineData(MovementRequestGymScenario.EqualPriorityTie)]
    public void FullGymIsIdenticalForNormalAndReversedRequestOrder(
        MovementRequestGymScenario scenario)
    {
        var normal = new MovementGymSession();
        var reversed = new MovementGymSession();
        for (int frame = 0; frame < 12; frame++)
        {
            var a = Make(scenario, HorizontalDirection.Left,
                press: frame == 2, release: frame == 6);
            var b = Make(scenario, HorizontalDirection.Left,
                press: frame == 2, release: frame == 6, reverse: true);
            var x = normal.Step(MovementGymSession.FixedStepSeconds,
                a, out MovementResolution ra);
            var y = reversed.Step(MovementGymSession.FixedStepSeconds,
                b, out MovementResolution rb);
            Assert.Equal(ra, rb);
            Assert.Equal(x.Bounds, y.Bounds);
            Assert.Equal(x.Velocity, y.Velocity);
            Assert.Equal(x.IsGrounded, y.IsGrounded);
            Assert.Equal(x.JumpStarted, y.JumpStarted);
            Assert.Equal(normal.ElapsedSeconds, reversed.ElapsedSeconds);
        }
    }

    [Fact]
    public void LockCanBlockHorizontalIntentWithoutPreventingJump()
    {
        var gym = new MovementGymSession();
        var step = gym.Step(MovementGymSession.FixedStepSeconds,
            Make(MovementRequestGymScenario.ControlLock,
                HorizontalDirection.Right, press: true),
            out MovementResolution selection);
        Assert.Equal(HorizontalDirection.Neutral, selection.Intent.Direction);
        Assert.True(step.JumpStarted);
        Assert.Equal(0.0, step.Velocity.X);
    }

    [Fact]
    public void InvalidTypedFrameRemainsTransactionalWithDetailedOverload()
    {
        var gym = new MovementGymSession();
        var before = gym.PlayerBounds;
        double time = gym.ElapsedSeconds;
        var invalid = new[] {
            MovementRequest.Horizontal(1, HorizontalDirection.Right),
            MovementRequest.Horizontal(1, HorizontalDirection.Left)
        };
        Assert.Throws<ArgumentException>(() =>
            gym.Step(MovementGymSession.FixedStepSeconds, invalid, out _));
        Assert.Equal(before, gym.PlayerBounds);
        Assert.Equal(time, gym.ElapsedSeconds);
    }

    [Fact]
    public void ResolverReportsNoWinnerForEmptyRequestSet()
    {
        var result = MovementRequestResolver.ResolveDetailed([]);
        Assert.Null(result.WinningSourceId);
        Assert.Null(result.WinningPriority);
        Assert.Equal(0, result.HorizontalRequestCount);
        Assert.Equal(new MovementFrameIntent(HorizontalDirection.Neutral,
            false, false), result.Intent);
    }
}
