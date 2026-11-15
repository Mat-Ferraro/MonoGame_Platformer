using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class GroundStepGymFixtureTests
{
    private static CollisionAssistancePolicy Assisted => new(
        0.18, 2, groundStepHeightUnits: 0.12,
        groundStepInwardUnits: 0.04,
        ledgeCatchHorizontalUnits: 0.16,
        ledgeCatchVerticalUnits: 0.15);

    [Fact]
    public void BothGymHasVisibleSizeGroundStepBumpBeforeLeftElevator()
    {
        var gym = new MovementGymSession();
        var bump = Assert.Single(gym.Colliders,
            c => c.Id == MovementGymSession.GroundStepBumpColliderId);
        Assert.Equal(-2.4, bump.Bounds.Left);
        Assert.Equal(-1.2, bump.Bounds.Right);
        Assert.Equal(0.0, bump.Bounds.Bottom);
        Assert.Equal(0.10, bump.Bounds.Top);
        Assert.Equal(MovementGymSession.GroundStepBumpHeight, bump.Bounds.Height);
        Assert.True(bump.Bounds.Right < gym.PlayerBounds.Left);
        Assert.DoesNotContain(new MovementGymSession(
            platformMode: MovingPlatformGymMode.Horizontal).Colliders,
            c => c.Id == MovementGymSession.GroundStepBumpColliderId);
    }

    [Fact]
    public void WalkingLeftStepsOverBumpOnlyWhenF9PolicyIsEnabled()
    {
        var assisted = new MovementGymSession();
        assisted.SetAssistancePolicy(Assisted);
        var baseline = new MovementGymSession();
        bool assistedStepObserved = false;
        for (int i = 0; i < 45; i++)
        {
            var a = assisted.Step(MovementGymSession.FixedStepSeconds,
                HorizontalDirection.Left, false);
            baseline.Step(MovementGymSession.FixedStepSeconds,
                HorizontalDirection.Left, false);
            if (a.Assists.Any(e => e.Kind == CollisionAssistKind.GroundStep))
            {
                assistedStepObserved = true;
                Assert.Equal(MovementGymSession.GroundStepBumpColliderId,
                    Assert.Single(a.Assists).ColliderId);
                Assert.Equal(MovementGymSession.GroundStepBumpHeight,
                    assisted.PlayerBounds.Bottom);
                Assert.True(assisted.IsGrounded);
                Assert.True(assisted.PlayerBounds.Left < -1.2);
                break;
            }
        }
        Assert.True(assistedStepObserved);
        Assert.Equal(0.0, baseline.PlayerBounds.Bottom);
        Assert.True(baseline.PlayerBounds.Left >= -1.2 - 1e-9);
        Assert.Equal(0, baseline.TotalAssists);
    }

    [Fact]
    public void AssistedGroundStepPreservesStrictNonpenetration()
    {
        var gym = new MovementGymSession();
        gym.SetAssistancePolicy(Assisted);
        bool sawStep = false;
        for (int i = 0; i < 40; i++)
        {
            var result = gym.Step(MovementGymSession.FixedStepSeconds,
                HorizontalDirection.Left, false);
            sawStep |= result.Assists.Any(e => e.Kind == CollisionAssistKind.GroundStep);
            Assert.All(gym.Colliders, c =>
                Assert.False(AabbCollisionDetector.Overlaps(gym.PlayerBounds, c.Bounds)));
            Assert.All(gym.MovingPlatforms, p =>
                Assert.False(AabbCollisionDetector.Overlaps(gym.PlayerBounds, p.Bounds)));
        }
        Assert.True(sawStep);
    }
}
