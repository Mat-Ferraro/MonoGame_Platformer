using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Input.Buffering;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// A tiny, deterministic gameplay test level. All authoritative motion is
/// delegated to CharacterMovementController; the MonoGame host only provides
/// discrete actions and reads the resulting state for presentation.
/// The stage and movement values are test fixtures, NOT engine defaults.
/// </summary>
public sealed class MovementGymSession
{
    public const double FixedStepSeconds = 1.0 / 60.0;
    public const double RespawnBelowY = -10.0;

    private static readonly Aabb SpawnBounds = new(
        new Vector2D(0.0, 0.0), new Vector2D(0.8, 1.6));

    private readonly IReadOnlyList<StaticAabbCollider> _colliders;
    private HorizontalMotionProfile _groundMotion;
    private HorizontalMotionProfile _airMotion;
    private readonly JumpFlightTrajectory _jump;
    private readonly MovingPlatformTrajectory _movingPlatform;
    private readonly JumpReleaseConstraints _jumpRelease = new(1.10);
    private CharacterMovementController _motor;
    private SimulationFaultPolicy _faultPolicy;
    private readonly BufferedJumpInput _jumpTiming = new(
        new JumpTimingConstraints(CoyoteTimeSeconds, JumpBufferSeconds));

    public const double CoyoteTimeSeconds = 0.10;
    public const double JumpBufferSeconds = 0.10;

    public MovementGymSession(
        MovementGymTuning? tuning = null,
        SimulationFaultSensitivity faultSensitivity = SimulationFaultSensitivity.Strict)
    {
        // Separated walkable floors form a pit from x=6 to x=8.
        // Platforms and walls expose different collision response cases.
        _colliders = Array.AsReadOnly(new[]
        {
            new StaticAabbCollider(1, Box(-35.0, -1.0, 6.0, 0.0)),
            new StaticAabbCollider(2, Box(8.0, -1.0, 40.0, 0.0)),
            new StaticAabbCollider(3, Box(11.0, 2.2, 16.0, 2.5)),
            new StaticAabbCollider(4, Box(22.0, 0.0, 22.8, 3.4)),
            new StaticAabbCollider(5, Box(-12.0, 0.0, -10.0, 1.25)),
            new StaticAabbCollider(6, Box(-5.0, 3.0, -2.5, 3.35))
        });

        // One elevated, horizontally translating test floor. Its full sweep
        // stays clear of the static stage geometry. Stable ID 7 is reserved.
        _movingPlatform = new MovingPlatformTrajectory(
            7, Box(5.3, 2.05, 7.35, 2.30),
            new Vector2D(2.4, 0.0), 2.4);

        _faultPolicy = new SimulationFaultPolicy(faultSensitivity);
        Tuning = tuning ?? MovementGymTuning.Baseline;
        _groundMotion = HorizontalMotionSolver.Solve(Tuning.Ground);
        _airMotion = HorizontalMotionSolver.Solve(Tuning.Air);
        _jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));

        _motor = CreateMotor();
    }

    public MovementGymTuning Tuning { get; private set; }
    public IReadOnlyList<StaticAabbCollider> Colliders => _colliders;
    public MovingPlatformSample MovingPlatform =>
        _movingPlatform.Evaluate(_motor.ElapsedSeconds);
    public bool IsRidingMovingPlatform => _motor.MovingSupportId == _movingPlatform.Id;
    public SimulationFaultSensitivity FaultSensitivity => _motor.FaultSensitivity;
    public int LastStepRecoveryCount => _motor.LastStepRecoveries.Count;

    /// <summary>Changes diagnostics only; does not reset the active player.</summary>
    public void SetFaultSensitivity(SimulationFaultSensitivity sensitivity)
    {
        SimulationFaultPolicy policy = new(sensitivity);
        _motor.SetFaultPolicy(policy);
        _faultPolicy = policy;
    }

    /// <summary>
    /// Applies both profiles as one operation, then resets the character and
    /// input buffers. A failed validation leaves the live session unchanged.
    /// </summary>
    public void ApplyTuning(MovementGymTuning tuning)
    {
        HorizontalMotionProfile ground = HorizontalMotionSolver.Solve(tuning.Ground);
        HorizontalMotionProfile air = HorizontalMotionSolver.Solve(tuning.Air);
        CharacterMovementController motor = CreateMotor(ground, air);
        _groundMotion = ground;
        _airMotion = air;
        Tuning = tuning;
        _motor = motor;
        _jumpTiming.Reset();
    }

    public MovementGymDiagnostics Diagnostics
    {
        get
        {
            bool ground = _motor.IsGrounded;
            HorizontalMotionProfile profile = ground ? _groundMotion : _airMotion;
            double speed = Math.Abs(_motor.Velocity.X);
            double deceleration = profile.StoppingDecelerationRate;
            return new MovementGymDiagnostics(
                speed, profile.MaximumSpeed, deceleration,
                speed / deceleration,
                speed * speed / (2.0 * deceleration), ground);
        }
    }
    public Aabb PlayerBounds => _motor.Bounds;
    public Vector2D PlayerVelocity => _motor.Velocity;
    public bool IsGrounded => _motor.IsGrounded;
    public bool IsJumping => _motor.IsJumping;
    public bool IsJumpEarlyReleased => _motor.IsJumpEarlyReleased;
    public int JumpsStarted => _motor.JumpsStarted;
    public double ElapsedSeconds => _motor.ElapsedSeconds;
    public int PitRespawns { get; private set; }

    /// <summary>Advances simulation. The host calls this once per fixed update.</summary>
    public CharacterControlStepResult Step(
        double durationSeconds, HorizontalDirection direction,
        bool jumpPressedThisStep, bool jumpReleasedThisStep = false)
    {
        // Keep zero-time reads observational (including input buffering).
        if (durationSeconds == 0.0)
            return _motor.Step(0.0, direction, false);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        // Plan input without mutating the buffer. Commit only if simulation
        // successfully advances: a rejected collision step must not eat input.
        bool wasGrounded = _motor.IsGrounded;
        JumpInputPlan plan = _jumpTiming.Plan(
            _motor.ElapsedSeconds, wasGrounded, _motor.IsJumping,
            jumpPressedThisStep, jumpReleasedThisStep);
        CharacterControlStepResult result = _motor.Step(
            durationSeconds, direction, plan.StartJump,
            jumpReleasedThisStep || plan.ReleaseOnStart,
            jumpGraceAuthorized: plan.AuthorizeCoyote);
        _jumpTiming.Commit(plan, _motor.ElapsedSeconds, wasGrounded,
            result.IsGrounded, result.JumpStarted);

        if (_motor.Bounds.Top < RespawnBelowY)
        {
            PitRespawns++;
            Reset();
            // Zero-duration reads return a snapshot without advancing time.
            return _motor.Step(0.0, HorizontalDirection.Neutral, false);
        }

        return result;
    }

    public void Reset()
    {
        _motor = CreateMotor();
        _jumpTiming.Reset();
    }

    private CharacterMovementController CreateMotor() =>
        CreateMotor(_groundMotion, _airMotion);

    private CharacterMovementController CreateMotor(
        HorizontalMotionProfile ground, HorizontalMotionProfile air) => new(
        SpawnBounds, _colliders, ground, air, _jump,
        gravityAcceleration: -24.0,
        releaseConstraints: _jumpRelease,
        movingPlatform: _movingPlatform,
        faultPolicy: _faultPolicy);

    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));
}
