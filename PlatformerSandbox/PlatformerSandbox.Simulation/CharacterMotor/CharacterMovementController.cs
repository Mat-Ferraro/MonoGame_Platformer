using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// First repeatable, headless locomotion controller. Owns authoritative bounds
/// and velocity. Ground/free-fall uses event-aware parabolic motion; an accepted
/// jump transfers authority to a fresh SteerableJumpMotor and returns it on
/// landing. Gameplay supplies discrete direction and jump-press actions; this
/// class does not read devices, buffer inputs, or render anything.
///
/// Current scope: static AABBs, Slide response, a held-jump vertical reference per
/// takeoff. Midair horizontal input re-solves the air-control profile from the
/// authoritative velocity and wall contact does not alter jump height.
/// Exact static-platform ledge departure is handled inside the step. Abilities are later work. An optional
/// designer-defined release constraint enables early-release jump height.
/// </summary>
public sealed class CharacterMovementController
{
    private readonly StaticAabbCollider[] _obstacles;
    private readonly HorizontalMotionProfile _groundMotion;
    private readonly HorizontalMotionProfile _airMotion;
    private readonly JumpFlightTrajectory _heldFlight;
    private readonly JumpReleaseConstraints? _releaseConstraints;
    private readonly JumpDescentConstraints _descentConstraints;
    private readonly double _gravity;
    private readonly int _maxContactEvents;
    private readonly MovingPlatformTrajectory? _movingPlatform;
    private SimulationFaultPolicy _faultPolicy;
    private bool _onMovingPlatform;

    private Aabb _bounds;
    private Vector2D _velocity;
    private bool _grounded;
    private SteerableJumpMotor? _jumpMotor;
    private double _elapsedSeconds;
    private int _jumpsStarted;

    public CharacterMovementController(
        Aabb initialBounds,
        IReadOnlyList<StaticAabbCollider> staticObstacles,
        HorizontalMotionProfile groundMotion,
        HorizontalMotionProfile airMotion,
        JumpFlightTrajectory heldFlight,
        double gravityAcceleration,
        int maxContactEvents = 8,
        JumpReleaseConstraints? releaseConstraints = null,
        JumpDescentConstraints descentConstraints = default,
        MovingPlatformTrajectory? movingPlatform = null,
        SimulationFaultPolicy? faultPolicy = null)
    {
        if (!initialBounds.IsValid)
            throw new ArgumentException("Initial bounds must be valid.", nameof(initialBounds));
        ArgumentNullException.ThrowIfNull(staticObstacles);
        ArgumentNullException.ThrowIfNull(groundMotion);
        ArgumentNullException.ThrowIfNull(airMotion);
        ArgumentNullException.ThrowIfNull(heldFlight);
        if (!double.IsFinite(gravityAcceleration) || gravityAcceleration >= 0.0)
            throw new ArgumentOutOfRangeException(nameof(gravityAcceleration),
                "Gravity must be a finite negative Y acceleration.");
        if (maxContactEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxContactEvents));
        if (releaseConstraints is { } release &&
            (!double.IsFinite(release.MinimumHeight) ||
             release.MinimumHeight <= 0.0 ||
             release.MinimumHeight >= heldFlight.PeakHeight ||
             heldFlight.IsEarlyReleased))
        {
            throw new ArgumentOutOfRangeException(nameof(releaseConstraints));
        }
        if (releaseConstraints is not null)
        {
            switch (descentConstraints.Policy)
            {
                case JumpDescentPolicy.PreserveReferenceFallLaw when
                    descentConstraints.DescentDurationSeconds is null:
                    break;
                case JumpDescentPolicy.SpecifyDescentDuration when
                    descentConstraints.DescentDurationSeconds is double fall &&
                    double.IsFinite(fall) && fall > 0.0:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(descentConstraints));
            }
        }

        _obstacles = staticObstacles.ToArray();
        var ids = new HashSet<int>();
        foreach (StaticAabbCollider obstacle in _obstacles)
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!ids.Add(obstacle.Id))
                throw new ArgumentException("Collider IDs must be unique.", nameof(staticObstacles));
            if (AabbCollisionDetector.Overlaps(initialBounds, obstacle.Bounds))
                throw new InvalidOperationException("Initial body overlaps a static collider.");
        }

        if (movingPlatform is not null)
        {
            if (movingPlatform.TravelOffset.Y != 0.0)
                throw new ArgumentException(
                    "Milestone 23 supports horizontal moving floors only.", nameof(movingPlatform));
            if (ids.Contains(movingPlatform.Id))
                throw new ArgumentException("Moving and static collider IDs must differ.",
                    nameof(movingPlatform));
            if (AabbCollisionDetector.Overlaps(initialBounds,
                    movingPlatform.Evaluate(0.0).Bounds))
                throw new InvalidOperationException(
                    "Initial body overlaps the moving platform.");
        }
        _movingPlatform = movingPlatform;
        _faultPolicy = faultPolicy ?? SimulationFaultPolicy.Development;
        _onMovingPlatform = movingPlatform is not null &&
            MovingPlatformRideSolver.IsSupported(
                initialBounds, movingPlatform.Evaluate(0.0).Bounds);
        _bounds = initialBounds;
        _velocity = _onMovingPlatform
            ? movingPlatform!.Evaluate(0.0).Velocity : Vector2D.Zero;
        _groundMotion = groundMotion;
        _airMotion = airMotion;
        _heldFlight = heldFlight;
        _releaseConstraints = releaseConstraints;
        _descentConstraints = descentConstraints;
        _gravity = gravityAcceleration;
        _maxContactEvents = maxContactEvents;
        _grounded = _onMovingPlatform || HasFloorSupport(initialBounds, _velocity);
    }

    public Aabb Bounds => _bounds;
    public Vector2D Velocity => _velocity;
    public bool IsGrounded => _grounded;
    public bool IsJumping => _jumpMotor is not null;
    public bool IsJumpEarlyReleased => _jumpMotor?.IsEarlyReleased ?? false;
    public int JumpsStarted => _jumpsStarted;
    public double ElapsedSeconds => _elapsedSeconds;
    public int? MovingSupportId => _onMovingPlatform ? _movingPlatform?.Id : null;
    public SimulationFaultSensitivity FaultSensitivity => _faultPolicy.Sensitivity;
    public IReadOnlyList<SimulationRecoveryRecord> LastStepRecoveries { get; private set; } =
        Array.Empty<SimulationRecoveryRecord>();

    /// <summary>
    /// Change diagnostic sensitivity without resetting or modifying physics state.
    /// Tests and the gym may select the policy independently of Debug/Release.
    /// </summary>
    public void SetFaultPolicy(SimulationFaultPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _faultPolicy = policy;
    }

    /// <summary>
    /// Advance by the scheduler-specified time. jumpPressedThisStep is a
    /// discrete action edge (not a continuously held button). A release edge
    /// cuts an active ascending reference once if release is configured.
    /// Airborne jump presses require explicit grace authorization from gameplay.
    /// Timing windows and buffered button edges are owned outside Simulation.
    /// A zero-duration call is strictly observational.
    /// </summary>
    public CharacterControlStepResult Step(
        double durationSeconds, HorizontalDirection direction,
        bool jumpPressedThisStep, bool jumpReleasedThisStep = false,
        bool jumpGraceAuthorized = false)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (direction is not (HorizontalDirection.Left or
            HorizontalDirection.Neutral or HorizontalDirection.Right))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (durationSeconds == 0.0)
            return Snapshot(false, 0.0, []);

        double endTime = _elapsedSeconds + durationSeconds;
        if (!double.IsFinite(endTime) || endTime <= _elapsedSeconds)
            throw new OverflowException("Controller clock cannot advance at finite precision.");

        Aabb nextBounds;
        Vector2D nextVelocity;
        bool nextGrounded;
        // A child jump motor is mutable. Advance a private working copy so a
        // later failure (including a landing carry failure) cannot publish
        // a partially advanced jump while the parent update rolls back.
        SteerableJumpMotor? nextJump = _jumpMotor?.Fork();
        bool started = false;
        CharacterControlContact[] contacts;
        bool nextMovingSupport = _onMovingPlatform;
        SimulationFaultSession faultSession = _faultPolicy.StartStep();

        if (jumpPressedThisStep && nextJump is null &&
            (_grounded || jumpGraceAuthorized))
        {
            // The held vertical flight starts anew at each takeoff, while X
            // inherits current ground speed and remains steerable in the air.
            nextJump = new SteerableJumpMotor(
                _bounds, _velocity.X, _airMotion, _heldFlight,
                _obstacles, _gravity, _maxContactEvents,
                _releaseConstraints, _descentConstraints,
                _movingPlatform, _elapsedSeconds);
            started = true;
            nextMovingSupport = false;
        }

        if (nextJump is not null)
        {
            SteerableJumpStepResult result = nextJump.Step(
                durationSeconds, direction, jumpReleasedThisStep);
            nextBounds = result.Bounds;
            nextVelocity = result.Velocity;
            nextGrounded = result.IsGrounded;
            contacts = result.Contacts.ToArray();
            if (nextGrounded)
            {
                nextJump = null;
                if (_movingPlatform is not null && contacts.Length > 0 &&
                    contacts[^1].ColliderId == _movingPlatform.Id &&
                    contacts[^1].Normal.Y > 0.0)
                {
                    CharacterControlContact finalContact = contacts[^1];
                    nextMovingSupport = true;
                    // The jump motor ends at the landing event. Carry for
                    // the remaining part of this fixed update, not one tick
                    // later, so there is no visual gap or lost displacement.
                    double landingTime = finalContact.TimeWithinStepSeconds;
                    double remainder = durationSeconds - landingTime;
                    double landingWorldTime = _elapsedSeconds + landingTime;
                    // A landing at the very end of the fixed update can leave
                    // a positive remainder smaller than one ULP of the world
                    // clock. The rider is already at the contact position;
                    // there is no representable carry interval to submit.
                    if (remainder > 0.0 &&
                        landingWorldTime + remainder > landingWorldTime)
                    {
                        MovingPlatformSample floor = _movingPlatform.Evaluate(
                            landingWorldTime);
                        MovingPlatformRideResult ride = MovingPlatformRideSolver.Step(
                            _movingPlatform, landingWorldTime,
                            remainder, nextBounds,
                            nextVelocity.X - floor.Velocity.X, 0.0);
                        nextBounds = ride.Bounds;
                        nextVelocity = ride.WorldVelocity;
                        nextMovingSupport = ride.IsSupported;
                        nextGrounded = ride.IsSupported;
                    }
                }
            }
        }
        else
        {
            (nextBounds, nextVelocity, nextGrounded, nextMovingSupport, contacts) =
                StepFreeOrGround(durationSeconds, direction, faultSession);
        }

        // Never publish a partially completed update: all potentially failing
        // collision calculations above execute before committing these fields.
        _bounds = nextBounds;
        _velocity = nextVelocity;
        _grounded = nextGrounded;
        _onMovingPlatform = nextMovingSupport;
        _jumpMotor = nextJump;
        _elapsedSeconds = endTime;
        if (started) _jumpsStarted++;
        LastStepRecoveries = faultSession.Recoveries;
        return Snapshot(started, durationSeconds, contacts);
    }

    private (Aabb Bounds, Vector2D Velocity, bool Grounded,
        bool OnMovingPlatform, CharacterControlContact[] Contacts) StepFreeOrGround(
        double duration, HorizontalDirection direction,
        SimulationFaultSession faultSession)
    {
        Aabb bounds = _bounds;
        Vector2D velocity = _velocity;
        bool grounded = _grounded;
        bool onMoving = _onMovingPlatform;
        double elapsed = 0.0;
        int supportDepartures = 0;
        var contacts = new List<CharacterControlContact>();

        // Re-plan at horizontal acceleration phases and support departures.
        // Walking off a ledge switches to AIR acceleration in the same update.
        // The accelerated stepper may process an internal landing; ground
        // profile selection after that landing occurs at the next phase/step.
        while (elapsed < duration)
        {
            double remaining = duration - elapsed;
            // Last-resort repair for a small overlap inherited from a previous
            // contact event. The pure sweep detector remains strict; deep or
            // ambiguous intersections still fail with useful diagnostics.
            if (!onMoving && _movingPlatform is not null)
            {
                MovingPlatformSample snapshot = _movingPlatform.Evaluate(
                    _elapsedSeconds + elapsed);
                MovingPlatformOverlapCorrection? repair =
                    MovingPlatformOverlapResolver.Resolve(bounds, snapshot, _obstacles);
                if (repair is { } correction)
                {
                    Vector2D incoming = velocity;
                    bounds = correction.Bounds;
                    velocity = CollisionResponse.Apply(
                        velocity - snapshot.Velocity, correction.Normal,
                        CollisionResponseMode.Slide) + snapshot.Velocity;
                    contacts.Add(new CharacterControlContact(
                        correction.PlatformId, elapsed, correction.Normal,
                        incoming, velocity));
                    if (contacts.Count + supportDepartures > _maxContactEvents)
                        throw new InvalidOperationException("Controller event limit exceeded.");
                    if (correction.Normal.Y > 0.0)
                    {
                        onMoving = true;
                        grounded = true;
                    }
                    else
                    {
                        grounded = false;
                    }
                    continue;
                }
            }
            if (onMoving && _movingPlatform is not null)
            {
                double movingFloorWorldTime = _elapsedSeconds + elapsed;
                MovingPlatformSample floor = _movingPlatform.Evaluate(movingFloorWorldTime);
                HorizontalMotionTrajectory relative = _groundMotion.CreateTrajectory(
                    0.0, velocity.X - floor.Velocity.X, direction);
                double rideDuration = remaining;
                if (relative.TimeToCruiseSeconds > 0.0)
                    rideDuration = Math.Min(rideDuration, relative.TimeToCruiseSeconds);
                double localV = velocity.X - floor.Velocity.X;
                double reversalStopTime = 0.0;
                if (direction != HorizontalDirection.Neutral &&
                    (int)direction * localV < 0.0)
                {
                    reversalStopTime =
                        Math.Abs(localV) / _groundMotion.ReversalDecelerationRate;
                    rideDuration = Math.Min(rideDuration, reversalStopTime);
                }
                if (!(rideDuration > 0.0) || !double.IsFinite(rideDuration))
                    throw new InvalidOperationException(
                        "Moving-floor motion has an invalid analytical phase duration.");

                // Ground locomotion is split at cruise/reversal events. A tiny
                // positive interval can be smaller than one representable
                // tick of either the local or world clock. Finish the KNOWN
                // analytical phase without calling the strict ride solver;
                // then re-plan from its exact boundary velocity.
                if (elapsed + rideDuration <= elapsed ||
                    movingFloorWorldTime + rideDuration <= movingFloorWorldTime)
                {
                    if (reversalStopTime > 0.0 &&
                        rideDuration == reversalStopTime)
                    {
                        velocity = new Vector2D(floor.Velocity.X, velocity.Y);
                        continue;
                    }
                    if (relative.TimeToCruiseSeconds > 0.0 &&
                        rideDuration == relative.TimeToCruiseSeconds)
                    {
                        velocity = new Vector2D(
                            floor.Velocity.X + relative.CruiseVelocity, velocity.Y);
                        continue;
                    }
                    if (rideDuration == remaining)
                    {
                        // A terminal remainder with no representable clock
                        // progress has no additional platform displacement.
                        elapsed = duration;
                        break;
                    }
                    throw new InvalidOperationException(
                        "Moving-floor event cannot advance either simulation clock.");
                }

                MovingPlatformRideResult ride = MovingPlatformRideSolver.Step(
                    _movingPlatform, movingFloorWorldTime, rideDuration, bounds,
                    localV, relative.Evaluate(0.0).Acceleration);
                if (!(ride.AdvancedSeconds > 0.0))
                    throw new InvalidOperationException("Moving-floor segment did not advance.");
                bounds = ride.Bounds;
                velocity = ride.WorldVelocity;
                onMoving = ride.IsSupported;
                grounded = ride.IsSupported;
                elapsed += ride.AdvancedSeconds;
                if (!onMoving)
                {
                    if (++supportDepartures + contacts.Count > _maxContactEvents)
                        throw new InvalidOperationException("Controller event limit exceeded.");
                }
                continue;
            }
            HorizontalMotionProfile profile = grounded ? _groundMotion : _airMotion;
            HorizontalMotionTrajectory reference = profile.CreateTrajectory(
                0.0, velocity.X, direction);

            double phaseDuration = remaining;
            if (reference.TimeToCruiseSeconds > 0.0 &&
                reference.TimeToCruiseSeconds < phaseDuration)
                phaseDuration = reference.TimeToCruiseSeconds;
            if (direction != HorizontalDirection.Neutral &&
                (int)direction * velocity.X < 0.0)
            {
                double reversalStop = Math.Abs(velocity.X) /
                    profile.ReversalDecelerationRate;
                if (reversalStop > 0.0 && reversalStop < phaseDuration)
                    phaseDuration = reversalStop;
            }

            double ax = reference.Evaluate(0.0).Acceleration;
            if (!grounded && _movingPlatform is not null)
            {
                // If a moving side wall is already carrying/pushing the body,
                // constrain acceleration that would immediately re-enter it.
                // Steering AWAY still works. This also prevents endless t=0
                // collision events after a side impact in this same update.
                MovingPlatformSample wall = _movingPlatform.Evaluate(
                    _elapsedSeconds + elapsed);
                bool verticalOverlap = bounds.Bottom < wall.Bounds.Top &&
                    bounds.Top > wall.Bounds.Bottom;
                int normalX = verticalOverlap &&
                    Math.Abs(bounds.Right - wall.Bounds.Left) <= 1e-8 ? -1 :
                    verticalOverlap &&
                    Math.Abs(bounds.Left - wall.Bounds.Right) <= 1e-8 ? 1 : 0;
                if (normalX != 0)
                {
                    double relativeX = velocity.X - wall.Velocity.X;
                    if (relativeX * normalX < 0.0)
                    {
                        // The approach can reach an already-flush face exactly
                        // at a fixed-step boundary. Constraining inward velocity
                        // is a real side-contact response; publish the event
                        // even when a sweep in this step has zero travel to it.
                        Vector2D incomingAtBoundary = velocity;
                        velocity = new Vector2D(wall.Velocity.X, velocity.Y);
                        contacts.Add(new CharacterControlContact(
                            wall.PlatformId, elapsed, new Vector2D(normalX, 0.0),
                            incomingAtBoundary, velocity));
                        if (contacts.Count + supportDepartures > _maxContactEvents)
                            throw new InvalidOperationException(
                                "Controller event limit exceeded.");
                    }
                    if (ax * normalX < 0.0)
                        ax = 0.0;
                }
            }
            if (grounded) velocity = new Vector2D(velocity.X, 0.0);
            GroundSupportDeparture? departure = grounded
                ? GroundSupportDepartureSolver.FindFirst(
                    bounds, velocity.X, ax, phaseDuration, _obstacles)
                : null;
            double advance = departure?.TimeSeconds ?? phaseDuration;
            if (!(advance > 0.0) || advance > remaining || !double.IsFinite(advance))
                throw new InvalidOperationException(
                    "Movement event has an invalid analytical duration.");

            // A valid local motion-phase duration can be smaller than one ULP
            // of the absolute world clock. In that case it cannot be sent to
            // MovingPlatformCollisionDetector: start + duration == start.
            // Resolve the *known instantaneous phase transition* at this
            // boundary and re-plan, without removing the detector's strict
            // validation or integrating a physically significant interval.
            double worldNow = _elapsedSeconds + elapsed;
            if (elapsed + advance <= elapsed || worldNow + advance <= worldNow)
            {
                if (departure is { } subResolutionEdge &&
                    subResolutionEdge.TimeSeconds == advance)
                {
                    // A platform-edge event is the authoritative reason for
                    // this tiny phase. Snap to its analytically solved edge.
                    bounds = bounds.Translated(new Vector2D(
                        subResolutionEdge.BodyLeftAtDeparture - bounds.Left, 0.0));
                    grounded = false;
                    if (++supportDepartures + contacts.Count > _maxContactEvents)
                        throw new InvalidOperationException("Controller event limit exceeded.");
                    continue;
                }

                double reversalStop = direction != HorizontalDirection.Neutral &&
                    (int)direction * velocity.X < 0.0
                    ? Math.Abs(velocity.X) / profile.ReversalDecelerationRate
                    : 0.0;
                if (reversalStop > 0.0 && reversalStop == advance)
                {
                    velocity = new Vector2D(0.0, velocity.Y);
                    continue;
                }

                if (reference.TimeToCruiseSeconds > 0.0 &&
                    reference.TimeToCruiseSeconds == advance)
                {
                    velocity = new Vector2D(reference.CruiseVelocity, velocity.Y);
                    continue;
                }

                if (advance == remaining)
                {
                    // Only an unrepresentable final remainder remains; the
                    // absolute simulation clock has no distinct event in it.
                    elapsed = duration;
                    break;
                }

                throw new InvalidOperationException(
                    "Motion event cannot advance either the local or world clock.");
            }

            Vector2D acceleration = new(ax, grounded ? 0.0 : _gravity);
            if (!grounded && _movingPlatform is not null)
            {
                // Milestone 24 Phase 2B. The shared stepper owns the COMPLETE
                // accelerated segment: after a static contact it re-queries
                // BOTH static and moving bodies for every subsequent event.
                // This prevents a moving impact from being skipped after the
                // original static accelerated stepper consumed the remainder.
                int availableContacts = _maxContactEvents - contacts.Count - supportDepartures;
                if (availableContacts < 1)
                    throw new InvalidOperationException(
                        "Controller collision event budget exhausted.");

                UnifiedAcceleratedStepResult unified =
                    UnifiedAcceleratedKinematicStepper.Step(
                        bounds, velocity, acceleration, _elapsedSeconds + elapsed,
                        advance, _obstacles, [_movingPlatform],
                        CollisionResponseMode.Slide, availableContacts, faultSession);
                if (contacts.Count + unified.Contacts.Count + supportDepartures >
                    _maxContactEvents)
                    throw new InvalidOperationException(
                        "Controller collision event limit exceeded.");

                foreach (CharacterControlContact unifiedContact in unified.Contacts)
                    contacts.Add(new CharacterControlContact(
                        unifiedContact.ColliderId, elapsed + unifiedContact.TimeWithinStepSeconds,
                        unifiedContact.Normal, unifiedContact.IncomingVelocity,
                        unifiedContact.OutgoingVelocity));

                bounds = unified.Bounds;
                velocity = unified.Velocity;
                elapsed += unified.AdvancedSeconds;
                if (unified.MovingFloorId == _movingPlatform.Id)
                {
                    // Transfer ownership exactly once at the landing event.
                    // The next loop iteration carries only the remaining time
                    // in the platform-local reference frame.
                    onMoving = true;
                    grounded = true;
                }
                else
                {
                    onMoving = false;
                    grounded = HasFloorSupport(bounds, velocity);
                }
                continue;
            }
            AcceleratedKinematicStepResult result = AcceleratedKinematicStepper.Step(
                bounds, velocity, acceleration, advance, _obstacles,
                CollisionResponseMode.Slide,
                Math.Max(1, _maxContactEvents - contacts.Count - supportDepartures));

            if (contacts.Count + result.Contacts.Count + supportDepartures > _maxContactEvents)
                throw new InvalidOperationException("Controller event limit exceeded.");
            foreach (AcceleratedKinematicContact contact in result.Contacts)
                contacts.Add(new CharacterControlContact(
                    contact.ColliderId, elapsed + contact.TimeSeconds,
                    contact.Normal, contact.IncomingVelocity, contact.OutgoingVelocity));

            bounds = result.Bounds;
            velocity = result.Velocity;
            if (departure is { } edge &&
                Math.Abs(bounds.Left - edge.BodyLeftAtDeparture) <= 1e-9)
            {
                // Align the analytically located edge by at most 1e-9 world
                // units, avoiding a false extra grounded tick due to root
                // rounding. Never snap when a wall prevented reaching it.
                bounds = bounds.Translated(new Vector2D(
                    edge.BodyLeftAtDeparture - bounds.Left, 0.0));
            }
            bool nextGrounded = HasFloorSupport(bounds, velocity);
            if (grounded && !nextGrounded)
            {
                supportDepartures++;
                if (contacts.Count + supportDepartures > _maxContactEvents)
                    throw new InvalidOperationException("Controller event limit exceeded.");
            }
            grounded = nextGrounded;
            elapsed += advance;
        }
        // The moving platform is evaluated from the authoritative clock, while
        // the character has been integrated across one or more analytical
        // segments. A perfectly constrained side contact can differ by a few
        // ULPs at the END of the step even if it was separated at the start.
        // Reconcile only a precision-sized overlap BEFORE committing state.
        // This is NOT a general penetration-recovery budget and does not hide
        // a missed moving-collider contact or allow deep interpenetration.
        if (!onMoving && _movingPlatform is not null)
        {
            MovingPlatformSample platformAtEnd = _movingPlatform.Evaluate(
                _elapsedSeconds + duration);
            Aabb platformBounds = platformAtEnd.Bounds;
            if (AabbCollisionDetector.Overlaps(bounds, platformBounds))
            {
                double overlapX = Math.Min(bounds.Right, platformBounds.Right) -
                                  Math.Max(bounds.Left, platformBounds.Left);
                double overlapY = Math.Min(bounds.Top, platformBounds.Top) -
                                  Math.Max(bounds.Bottom, platformBounds.Bottom);
                const double maxPrecisionCorrection = 1e-9;
                if (Math.Min(overlapX, overlapY) > maxPrecisionCorrection)
                    throw new InvalidOperationException(
                        "A moving-platform collision step produced a real overlap; " +
                        "the motor will not commit intersecting bounds.");

                MovingPlatformOverlapCorrection? precisionCorrection =
                    MovingPlatformOverlapResolver.Resolve(
                        bounds, platformAtEnd, _obstacles, maxPrecisionCorrection);
                if (precisionCorrection is { } correction)
                {
                    bounds = correction.Bounds;
                    Vector2D relativeVelocityAtEnd = velocity - platformAtEnd.Velocity;
                    double inwardSpeed =
                        relativeVelocityAtEnd.X * correction.Normal.X +
                        relativeVelocityAtEnd.Y * correction.Normal.Y;
                    if (inwardSpeed < 0.0)
                    {
                        Vector2D beforePrecisionResponse = velocity;
                        velocity = CollisionResponse.Apply(
                            relativeVelocityAtEnd, correction.Normal,
                            CollisionResponseMode.Slide) + platformAtEnd.Velocity;
                        contacts.Add(new CharacterControlContact(
                            correction.PlatformId, duration, correction.Normal,
                            beforePrecisionResponse, velocity));
                        if (contacts.Count + supportDepartures > _maxContactEvents)
                            throw new InvalidOperationException(
                                "Controller event limit exceeded.");
                    }
                    if (correction.Normal.Y > 0.0 &&
                        relativeVelocityAtEnd.Y <= 0.0)
                    {
                        grounded = true;
                        onMoving = true;
                    }
                }
            }
        }
        return (bounds, velocity, grounded, onMoving, contacts.ToArray());
    }

    private bool HasFloorSupport(Aabb bounds, Vector2D velocity)
    {
        if (velocity.Y > 0.0) return false;
        foreach (StaticAabbCollider obstacle in _obstacles)
        {
            if (bounds.Bottom == obstacle.Bounds.Top &&
                bounds.Right > obstacle.Bounds.Left &&
                bounds.Left < obstacle.Bounds.Right)
                return true;
        }
        return false;
    }

    private CharacterControlStepResult Snapshot(
        bool jumpStarted, double duration, CharacterControlContact[] contacts) =>
        new(_bounds, _velocity, _grounded, _jumpMotor is not null,
            jumpStarted, _jumpsStarted, _elapsedSeconds, duration, contacts);
}
