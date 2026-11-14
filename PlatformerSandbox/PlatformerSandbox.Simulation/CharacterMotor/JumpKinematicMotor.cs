using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Stateful, headless vertical-slice motor for one configured jump and a STATIC
/// collider snapshot. While unobstructed it evaluates the exact designer jump
/// reference at absolute time, independent of how updates are partitioned.
/// At its first contact (or its reference endpoint), movement ownership moves
/// to fresh analytical constant-acceleration segments using the caller's
/// explicit post-reference acceleration. The original reference is NEVER
/// resumed after contact. No MonoGame dependency, input devices, abilities,
/// moving platforms, or hidden depenetration.
/// </summary>
public sealed class JumpKinematicMotor
{
    private readonly Aabb _takeoffBounds;
    private readonly JumpReferenceCollisionPlan _reference;
    private readonly StaticAabbCollider[] _obstacles;
    private readonly JumpReferenceInterruptionResult _referenceOutcome;
    private readonly CollisionResponseMode _responseMode;
    private readonly int _maxContactEvents;
    private readonly double _contactAlignmentTolerance;

    private Aabb _bounds;
    private Vector2D _velocity;
    private double _elapsedSeconds;
    private JumpMotorPhase _phase;

    public JumpKinematicMotor(
        Aabb takeoffBounds,
        JumpReferenceCollisionPlan reference,
        IReadOnlyList<StaticAabbCollider> staticObstacles,
        CollisionResponseMode responseMode = CollisionResponseMode.Slide,
        int maxContactEvents = 8,
        double contactAlignmentTolerance = 1e-8)
    {
        if (!takeoffBounds.IsValid)
            throw new ArgumentException("Takeoff bounds must be valid.", nameof(takeoffBounds));
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(staticObstacles);
        if (!Enum.IsDefined(responseMode))
            throw new ArgumentOutOfRangeException(nameof(responseMode));
        if (maxContactEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxContactEvents));
        if (!double.IsFinite(contactAlignmentTolerance) || contactAlignmentTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(contactAlignmentTolerance));

        _takeoffBounds = takeoffBounds;
        _reference = reference;
        _obstacles = staticObstacles.ToArray(); // stable world snapshot, independent of caller's array
        _responseMode = responseMode;
        _maxContactEvents = maxContactEvents;
        _contactAlignmentTolerance = contactAlignmentTolerance;

        // Also validates unique collider IDs and rejects initial penetration.
        // This precomputed event is valid only because this milestone models
        // a fixed reference input schedule against static geometry.
        _referenceOutcome = JumpReferenceInterruption.AdvanceToFirstContact(
            takeoffBounds, reference, _obstacles, responseMode, contactAlignmentTolerance);

        JumpTravelSample takeoff = reference.Reference.Evaluate(0.0);
        _bounds = takeoffBounds;
        _velocity = new Vector2D(takeoff.HorizontalVelocity, takeoff.VerticalVelocity);
        _phase = JumpMotorPhase.Reference;
    }

    public Aabb Bounds => _bounds;
    public Vector2D Velocity => _velocity;
    public double ElapsedSeconds => _elapsedSeconds;
    public JumpMotorPhase Phase => _phase;
    public bool IsGrounded => HasFloorSupport();

    /// <summary>
    /// Advance by an arbitrary nonnegative finite duration (the scheduler owns
    /// the fixed timestep). Post-reference acceleration is used ONLY once the
    /// designer reference ends or is interrupted; acceleration is constant
    /// for this request, and may be changed explicitly on the next request.
    /// A collision projects inward acceleration for the remainder of THIS
    /// step, without permanently modifying the next requested acceleration.
    /// </summary>
    public JumpMotorStepResult Step(double durationSeconds, Vector2D postReferenceAcceleration)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        if (durationSeconds == 0.0)
            return Snapshot(0.0, []);

        double startTime = _elapsedSeconds;
        double endTime = startTime + durationSeconds;
        if (!double.IsFinite(endTime) || endTime <= startTime)
            throw new OverflowException("The requested step cannot advance the motor clock at finite precision.");

        // All calculations use locals; a failed collision query or event-limit
        // exception must not partially commit an authoritative motor update.
        Aabb bounds = _bounds;
        Vector2D velocity = _velocity;
        JumpMotorPhase phase = _phase;
        double consumed = 0.0;
        double remaining = durationSeconds;
        Vector2D accelerationForThisStep = postReferenceAcceleration;
        var contacts = new List<JumpMotorContact>();

        if (phase == JumpMotorPhase.Reference)
        {
            double referenceEventTime = _referenceOutcome.ElapsedSeconds;
            double timeUntilEvent = referenceEventTime - startTime;
            if (timeUntilEvent < 0.0)
                throw new InvalidOperationException("Reference event time precedes the motor clock.");

            if (remaining < timeUntilEvent)
            {
                JumpTravelSample sample = _reference.Reference.Evaluate(endTime);
                bounds = _takeoffBounds.Translated(new Vector2D(
                    sample.HorizontalPosition, sample.VerticalDisplacement));
                velocity = new Vector2D(sample.HorizontalVelocity, sample.VerticalVelocity);
                consumed = durationSeconds;
                remaining = 0.0;
            }
            else
            {
                // Reference endpoint or first contact. Continuing the old
                // profile afterward would violate collision authority.
                consumed = timeUntilEvent;
                remaining -= timeUntilEvent;
                bounds = _referenceOutcome.Bounds;
                velocity = _referenceOutcome.Velocity;

                if (_referenceOutcome.Contact is { } hit)
                {
                    contacts.Add(new JumpMotorContact(
                        hit.ColliderId, consumed, startTime + consumed,
                        hit.Normal, bounds, hit.VelocityAtImpact, velocity));
                    phase = _responseMode == CollisionResponseMode.Stop
                        ? JumpMotorPhase.Halted : JumpMotorPhase.FreeMotion;

                    if (phase == JumpMotorPhase.FreeMotion)
                    {
                        // Preserve an established contact for the remaining
                        // portion of THIS step, including inward gravity.
                        accelerationForThisStep = CollisionResponse.Apply(
                            postReferenceAcceleration, hit.Normal, _responseMode);
                    }
                }
                else
                {
                    phase = JumpMotorPhase.FreeMotion;
                }
            }
        }

        if (phase == JumpMotorPhase.FreeMotion && remaining > 0.0)
        {
            // The reference impact and all follow-up events share ONE budget
            // for this Step call. The existing stepper requires a positive
            // budget, so permit one provisional event and reject it here if
            // the reference has already used the final available slot.
            int availableContactSlots = _maxContactEvents - contacts.Count;
            AcceleratedKinematicStepResult continuation = AcceleratedKinematicStepper.Step(
                bounds, velocity, accelerationForThisStep, remaining,
                _obstacles, _responseMode, Math.Max(1, availableContactSlots),
                _contactAlignmentTolerance);
            if (continuation.Contacts.Count > availableContactSlots)
                throw new InvalidOperationException(
                    $"Motor collision event limit ({_maxContactEvents}) exceeded; " +
                    "the incomplete movement is not committed.");

            foreach (AcceleratedKinematicContact hit in continuation.Contacts)
            {
                contacts.Add(new JumpMotorContact(
                    hit.ColliderId, consumed + hit.TimeSeconds,
                    startTime + consumed + hit.TimeSeconds,
                    hit.Normal, hit.BoundsAtImpact,
                    hit.IncomingVelocity, hit.OutgoingVelocity));
            }

            bounds = continuation.Bounds;
            velocity = continuation.Velocity;
            if (_responseMode == CollisionResponseMode.Stop && continuation.Contacts.Count != 0)
                phase = JumpMotorPhase.Halted;
        }

        // Commit only after the entire requested step succeeds. The clock
        // advances even when Stop freezes spatial movement early.
        _bounds = bounds;
        _velocity = velocity;
        _phase = phase;
        _elapsedSeconds = endTime;
        return Snapshot(durationSeconds, contacts.ToArray());
    }

    private bool HasFloorSupport()
    {
        if (_velocity.Y > 0.0)
            return false;
        foreach (StaticAabbCollider obstacle in _obstacles)
        {
            if (_bounds.Bottom == obstacle.Bounds.Top &&
                _bounds.Right > obstacle.Bounds.Left &&
                _bounds.Left < obstacle.Bounds.Right)
                return true;
        }
        return false;
    }

    private JumpMotorStepResult Snapshot(double stepDuration, JumpMotorContact[] contacts) =>
        new(_bounds, _velocity, _phase, HasFloorSupport(),
            _elapsedSeconds, stepDuration, contacts);
}
