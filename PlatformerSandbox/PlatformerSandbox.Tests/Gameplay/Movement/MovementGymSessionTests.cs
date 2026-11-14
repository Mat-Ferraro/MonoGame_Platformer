using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class MovementGymSessionTests
{
    [Fact]
    public void NewSessionStartsStationaryAndGrounded()
    {
        var gym = new MovementGymSession();
        Assert.True(gym.IsGrounded);
        Assert.False(gym.IsJumping);
        Assert.Equal(new Aabb(new Vector2D(0, 0), new Vector2D(0.8, 1.6)), gym.PlayerBounds);
        Assert.Equal(Vector2D.Zero, gym.PlayerVelocity);
        Assert.Equal(0, gym.JumpsStarted);
    }

    [Fact]
    public void StageHasUniqueStableColliderIds()
    {
        var gym = new MovementGymSession();
        Assert.Equal(6, gym.Colliders.Count);
        Assert.Equal(6, gym.Colliders.Select(c => c.Id).Distinct().Count());
        Assert.All(gym.Colliders, c =>
            Assert.False(AabbCollisionDetector.Overlaps(gym.PlayerBounds, c.Bounds)));
    }

    [Fact]
    public void GymUsesDeterministicFixedUpdateDuration()
    {
        Assert.Equal(1.0 / 60.0, MovementGymSession.FixedStepSeconds);
        var a = new MovementGymSession();
        var b = new MovementGymSession();
        for (int i = 0; i < 30; i++)
        {
            a.Step(MovementGymSession.FixedStepSeconds, HorizontalDirection.Right, false);
            b.Step(MovementGymSession.FixedStepSeconds, HorizontalDirection.Right, false);
        }
        Assert.Equal(a.PlayerBounds, b.PlayerBounds);
        Assert.Equal(a.PlayerVelocity, b.PlayerVelocity);
    }

    [Fact]
    public void RightInputMovesCharacterRight()
    {
        var gym = new MovementGymSession();
        gym.Step(0.1, HorizontalDirection.Right, false);
        Assert.True(gym.PlayerBounds.Left > 0);
        Assert.True(gym.PlayerVelocity.X > 0);
        Assert.True(gym.IsGrounded);
    }

    [Fact]
    public void NeutralInputBrakesRunningCharacter()
    {
        var gym = new MovementGymSession();
        gym.Step(0.18, HorizontalDirection.Right, false);
        gym.Step(0.13, HorizontalDirection.Neutral, false);
        Assert.InRange(Math.Abs(gym.PlayerVelocity.X), 0, 1e-8);
    }

    [Fact]
    public void SpacePressEquivalentStartsAJump()
    {
        var gym = new MovementGymSession();
        var state = gym.Step(0.05, HorizontalDirection.Neutral, true);
        Assert.True(state.JumpStarted);
        Assert.True(gym.IsJumping);
        Assert.False(gym.IsGrounded);
        Assert.True(gym.PlayerVelocity.Y > 0);
    }

    [Fact]
    public void JumpReturnsToGroundAndCanStartAgain()
    {
        var gym = new MovementGymSession();
        gym.Step(0.8, HorizontalDirection.Neutral, true);
        Assert.True(gym.IsGrounded);
        Assert.False(gym.IsJumping);
        Assert.Equal(1, gym.JumpsStarted);
        var second = gym.Step(0.05, HorizontalDirection.Neutral, true);
        Assert.True(second.JumpStarted);
        Assert.Equal(2, gym.JumpsStarted);
    }

    [Fact]
    public void ResetRecreatesSpawnWithoutChangingLevel()
    {
        var gym = new MovementGymSession();
        int[] ids = gym.Colliders.Select(c => c.Id).ToArray();
        gym.Step(0.1, HorizontalDirection.Right, false);
        gym.Reset();
        Assert.Equal(0, gym.PlayerBounds.Left);
        Assert.Equal(0, gym.PlayerBounds.Bottom);
        Assert.Equal(Vector2D.Zero, gym.PlayerVelocity);
        Assert.True(gym.IsGrounded);
        Assert.Equal(0, gym.JumpsStarted);
        Assert.Equal(ids, gym.Colliders.Select(c => c.Id));
    }

    [Fact]
    public void ZeroDurationStepDoesNotChangeSimulation()
    {
        var gym = new MovementGymSession();
        var state = gym.Step(0, HorizontalDirection.Right, true);
        Assert.False(state.JumpStarted);
        Assert.Equal(0, state.ElapsedSeconds);
        Assert.Equal(0, gym.JumpsStarted);
        Assert.Equal(0, gym.PlayerBounds.Left);
    }

    [Fact]
    public void PitRespawnBoundaryIsBelowWalkableStage()
    {
        Assert.True(MovementGymSession.RespawnBelowY < -1.0);
    }
}
