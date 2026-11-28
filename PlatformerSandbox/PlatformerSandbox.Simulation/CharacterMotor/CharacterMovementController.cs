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
/// Authoritative, headless locomotion controller. Ground motion and ordinary
/// free-fall use the shared static/moving event scheduler; supported riding
/// arbitrates static collision with support departure in platform-local space;
/// custom analytical jumps use the same unified collision query.
///
/// A step builds all next-state values locally and forks mutable jump state;
/// no failed step commits partial position, velocity, support or jump data.
/// Gameplay supplies discrete input edges, checkpoints and fault policy.
/// Tracks multiple moving collision candidates and one authoritative support
/// at a time. Coincident contacts and classified crushes use the shared
/// constraint resolver; moving world geometry stays on authored trajectories.
/// </summary>
public sealed class CharacterMovementController
{
    private IReadOnlyList<StaticAabbCollider> _obstacles;
    private readonly HorizontalMotionProfile _groundMotion;
    private readonly HorizontalMotionProfile _airMotion;
    private readonly JumpFlightTrajectory _heldFlight;
    private readonly JumpReleaseConstraints? _releaseConstraints;
    private readonly JumpDescentConstraints _descentConstraints;
    private readonly double _gravity;
    private readonly int _maxContactEvents;
    private MovingPlatformTrajectory[] _movingPlatforms;
    private readonly PlatformObstructionPolicy _obstructionPolicy;
    private readonly CollisionAssistancePolicy _assistancePolicy;
    private MovingPlatformTrajectory? _supportPlatform;
    private SimulationFaultPolicy _faultPolicy;
    private bool _onMovingPlatform;

    private Aabb _bounds;
    private Vector2D _velocity;
    private bool _grounded;
    private SteerableJumpMotor? _jumpMotor;
    private double _elapsedSeconds;
    // Persistent world-space horizontal transport contribution, separate from
    // locomotion speed and its designer maximum. Set only on a staged motor.
    private double _externalHorizontalSpeed;
    private int _jumpsStarted;
    private bool _ledgeCatchUsed;

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
        SimulationFaultPolicy? faultPolicy = null,
        IReadOnlyList<MovingPlatformTrajectory>? additionalMovingPlatforms = null,
        PlatformObstructionPolicy obstructionPolicy =
            PlatformObstructionPolicy.FollowAuthoredTrajectories,
        CollisionAssistancePolicy? assistancePolicy = null,
        double initialWorldTimeSeconds = 0.0)
    {
        if (!double.IsFinite(initialWorldTimeSeconds) ||
            initialWorldTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialWorldTimeSeconds));
        if (!Enum.IsDefined(obstructionPolicy))
            throw new ArgumentOutOfRangeException(nameof(obstructionPolicy));
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

        _obstacles = SpatialColliderSet.Wrap(staticObstacles);
        var ids = new HashSet<int>();
        foreach (StaticAabbCollider obstacle in _obstacles)
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!ids.Add(obstacle.Id))
                throw new ArgumentException("Collider IDs must be unique.", nameof(staticObstacles));
            if (AabbCollisionDetector.Overlaps(initialBounds, obstacle.Bounds))
                throw new InvalidOperationException("Initial body overlaps a static collider.");
        }

        var moving = new List<MovingPlatformTrajectory>();
        if (movingPlatform is not null) moving.Add(movingPlatform);
        if (additionalMovingPlatforms is not null)
            moving.AddRange(additionalMovingPlatforms);
        foreach (MovingPlatformTrajectory platform in moving)
        {
            ArgumentNullException.ThrowIfNull(platform);
            if (!ids.Add(platform.Id))
                throw new ArgumentException(
                    "Moving and static collider IDs must be globally unique.",
                    nameof(additionalMovingPlatforms));
            if (AabbCollisionDetector.Overlaps(initialBounds,
                    platform.Evaluate(initialWorldTimeSeconds).Bounds))
                throw new InvalidOperationException(
                    $"Initial body overlaps moving platform {platform.Id}.");
        }
        _movingPlatforms = moving.ToArray();
        _obstructionPolicy = obstructionPolicy;
        _assistancePolicy = assistancePolicy ?? CollisionAssistancePolicy.Disabled;
        if (obstructionPolicy == PlatformObstructionPolicy.RejectWorldObstructions &&
            MovingPlatformObstructionDetector.FindFirst(
                initialWorldTimeSeconds, 0.0, _obstacles, _movingPlatforms) is { } initialObstruction)
            throw new PlatformObstructionException(initialObstruction);
        _faultPolicy = faultPolicy ?? SimulationFaultPolicy.Development;
        _supportPlatform = _movingPlatforms.FirstOrDefault(platform =>
            MovingPlatformRideSolver.IsSupported(initialBounds,
                platform.Evaluate(initialWorldTimeSeconds).Bounds));
        _onMovingPlatform = _supportPlatform is not null;
        _bounds = initialBounds;
        _velocity = _supportPlatform is not null
            ? _supportPlatform.Evaluate(initialWorldTimeSeconds).Velocity : Vector2D.Zero;
        _groundMotion = groundMotion;
        _airMotion = airMotion;
        _heldFlight = heldFlight;
        _releaseConstraints = releaseConstraints;
        _descentConstraints = descentConstraints;
        _gravity = gravityAcceleration;
        _maxContactEvents = maxContactEvents;
        _grounded = _onMovingPlatform || HasFloorSupport(initialBounds, _velocity);
        _elapsedSeconds = initialWorldTimeSeconds;
    }

    public Aabb Bounds => _bounds;
    public Vector2D Velocity => _velocity;
    public bool IsGrounded => _grounded;
    public bool IsJumping => _jumpMotor is not null;
    public bool IsJumpEarlyReleased => _jumpMotor?.IsEarlyReleased ?? false;
    public int JumpsStarted => _jumpsStarted;
    public double ElapsedSeconds => _elapsedSeconds;
    public double ExternalHorizontalSpeed => _externalHorizontalSpeed;
    public int? MovingSupportId => _supportPlatform?.Id;
    public bool HasTransientMovingGeometry => _movingPlatforms.Any(p=>p.IsTickMotion);
    public Vector2D MovingSupportVelocity(double time)=>_supportPlatform?.Evaluate(time).Velocity??Vector2D.Zero;
    public PlatformObstructionPolicy ObstructionPolicy => _obstructionPolicy;
    public CollisionAssistancePolicy AssistancePolicy => _assistancePolicy;
    public IReadOnlyList<CollisionAssistEvent> LastStepAssists { get; private set; } =
        Array.Empty<CollisionAssistEvent>();
    public SimulationFaultSensitivity FaultSensitivity => _faultPolicy.Sensitivity;
    public IReadOnlyList<SimulationRecoveryRecord> LastStepRecoveries { get; private set; } =
        Array.Empty<SimulationRecoveryRecord>();

    /// <summary>
    /// Last recognized failure contains the immutable PRE-update state. The
    /// state itself is never partially committed when a step fails.
    /// </summary>
    public SimulationFaultSnapshot? LastFault { get; private set; }
    public IReadOnlyList<SimulationContactTrace> LastStepContactTrace { get; private set; } =
        Array.Empty<SimulationContactTrace>();


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
    /// Create an independent working copy for transactional multi-character
    /// world updates. Immutable geometry/profiles are shared; the mutable
    /// analytical jump motor is independently forked.
    /// </summary>
    public CharacterMovementController Fork()
    {
        var copy = (CharacterMovementController)MemberwiseClone();
        copy._jumpMotor = _jumpMotor?.Fork();
        return copy;
    }

    /// <summary>Replace the immutable obstacle view on a staged motor at a tick boundary.
    /// The analytical jump phase is retained; no position or velocity is overwritten.
    /// </summary>
    public void UseTickGeometry(IReadOnlyList<StaticAabbCollider> geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (ReferenceEquals(_obstacles, geometry)) return;
        var next=SpatialColliderSet.Wrap(geometry);
        if(next.Any(c=>AabbCollisionDetector.Overlaps(_bounds,c.Bounds)))
            throw new InvalidOperationException("Character starts inside a solid tick collider.");
        _obstacles=next;
        _jumpMotor?.UseTickGeometry(next);
        if(_jumpMotor is null && _supportPlatform is null)
            _grounded=HasFloorSupport(_bounds,_velocity);
    }

    /// <summary>Install a staged moving-collider view, including any dynamic support
    /// whose chronological motion was predicted by the prop solver.</summary>
    public void UseTickMovingGeometry(IReadOnlyList<StaticAabbCollider> geometry,
        MovingPlatformTrajectory[] moving, int? dynamicSupportId)
    {
        var prior=_supportPlatform;
        var priorVelocity=prior?.Evaluate(_elapsedSeconds).Velocity??Vector2D.Zero;
        _movingPlatforms=moving;
        _supportPlatform=prior is null?null:moving.FirstOrDefault(p=>p.Id==prior.Id);
        if(_supportPlatform is null&&_jumpMotor is null&&dynamicSupportId is int id)
            _supportPlatform=moving.FirstOrDefault(p=>p.Id==id&&MovingPlatformRideSolver.IsSupported(_bounds,p.Evaluate(_elapsedSeconds).Bounds));
        _onMovingPlatform=_supportPlatform is not null;
        if(_onMovingPlatform&&_supportPlatform is { } support)
        {
            var velocity=support.Evaluate(_elapsedSeconds).Velocity;
            _velocity=new(_velocity.X-priorVelocity.X+velocity.X,velocity.Y);
            _grounded=true;
        }
        _jumpMotor?.UseTickMovingGeometry(moving);
        UseTickGeometry(geometry);
        if(_supportPlatform is null&&_jumpMotor is null)_grounded=HasFloorSupport(_bounds,_velocity);
    }

    /// <summary>
    /// Execute exactly the world-owned fixed tick interval. This is opt-in;
    /// legacy single-player callers retain the duration-based Step API.
    /// Recalculate the duration from world timestamps to prevent independent
    /// floating-point clock drift across characters or late spawns.
    /// </summary>
    public CharacterControlStepResult StepOnWorldTimeline(
        double worldStartSeconds, double worldEndSeconds,
        HorizontalDirection direction, bool jumpPressedThisStep,
        bool jumpReleasedThisStep = false, bool jumpGraceAuthorized = false,
        double externalHorizontalSpeed = 0.0)
    {
        if (!double.IsFinite(worldStartSeconds) ||
            !double.IsFinite(worldEndSeconds) ||
            worldStartSeconds < 0.0 || worldEndSeconds <= worldStartSeconds)
            throw new ArgumentOutOfRangeException(nameof(worldEndSeconds));
        if (_elapsedSeconds != worldStartSeconds)
            throw new InvalidOperationException(
                "Character world clock is not aligned to scheduled tick start.");
        double durationSeconds = worldEndSeconds - worldStartSeconds;
        if (_elapsedSeconds + durationSeconds != worldEndSeconds)
            throw new InvalidOperationException(
                "Scheduled interval cannot be represented on character clock.");
        return Step(durationSeconds, direction, jumpPressedThisStep,
            jumpReleasedThisStep, jumpGraceAuthorized, externalHorizontalSpeed);
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
        bool jumpGraceAuthorized = false, double externalHorizontalSpeed = 0.0)
    {
        if (!double.IsFinite(externalHorizontalSpeed) ||
            Math.Abs(externalHorizontalSpeed) > 10000.0)
            throw new ArgumentOutOfRangeException(nameof(externalHorizontalSpeed));
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

        try
        {
        if (_obstructionPolicy == PlatformObstructionPolicy.RejectWorldObstructions &&
            MovingPlatformObstructionDetector.FindFirst(
                _elapsedSeconds, durationSeconds, _obstacles, _movingPlatforms)
                is { } obstruction)
            throw new PlatformObstructionException(obstruction);

        // Change the external transport channel at the tick boundary. Only
        // the incremental difference is applied: ordinary locomotion remains
        // limited by its own profile while the *combined* speed may exceed it.
        Vector2D startVelocity = new(
            _velocity.X + externalHorizontalSpeed - _externalHorizontalSpeed,
            _velocity.Y);
        if (!double.IsFinite(startVelocity.X))
            throw new OverflowException("Combined motion exceeds finite velocity.");
        Aabb nextBounds;
        Vector2D nextVelocity;
        bool nextGrounded;
        // A child jump motor is mutable. Advance a private working copy so a
        // later failure (including a landing carry failure) cannot publish
        // a partially advanced jump while the parent update rolls back.
        SteerableJumpMotor? nextJump = _jumpMotor?.Fork();
        bool started = false;
        CharacterControlContact[] contacts;
        MovingPlatformTrajectory? nextSupport = _supportPlatform;
        SimulationFaultSession faultSession = _faultPolicy.StartStep();
        IReadOnlyList<CollisionAssistEvent> stepAssists = Array.Empty<CollisionAssistEvent>();
        bool nextLedgeCatchUsed = _ledgeCatchUsed;


        if (jumpPressedThisStep && nextJump is null &&
            (_grounded || jumpGraceAuthorized))
        {
            // The held vertical flight starts anew at each takeoff, while X
            // inherits current ground speed and remains steerable in the air.
            // The current support is preferred as the jump reference frame.
            // All other platforms remain collision candidates, not parents.
            MovingPlatformTrajectory? jumpReference =
                nextSupport ?? _movingPlatforms.FirstOrDefault();
            nextJump = new SteerableJumpMotor(
                StaticFloorPrecisionAlignment.Align(_bounds, _obstacles),
                startVelocity.X, _airMotion, _heldFlight,
                _obstacles, _gravity, _maxContactEvents,
                _releaseConstraints, _descentConstraints,
                jumpReference, _elapsedSeconds,
                inheritedVerticalVelocity: nextSupport is not null
                    ? nextSupport.Evaluate(_elapsedSeconds).Velocity.Y : 0.0,
                additionalMovingPlatforms: _movingPlatforms.Where(
                    p => p != jumpReference).ToArray(),
                assistancePolicy: _assistancePolicy,
                externalHorizontalSpeed: externalHorizontalSpeed);
            started = true;
            nextSupport = null;
        }

        if (nextJump is not null)
        {
            SteerableJumpStepResult result = nextJump.Step(
                durationSeconds, direction, jumpReleasedThisStep,
                externalHorizontalSpeed);
            stepAssists = result.Assists;
            nextBounds = result.Bounds;
            nextVelocity = result.Velocity;
            nextGrounded = result.IsGrounded;
            contacts = result.Contacts.ToArray();
            if (nextGrounded)
            {
                nextJump = null;
                double processed = result.AdvancedSeconds;
                // A simultaneous landing may include a wall/ceiling contact
                // with a greater stable collider ID. The last contact in
                // sorted order is not necessarily the supporting platform.
                // Select the latest valid moving top-contact instead.
                CharacterControlContact? movingLanding = contacts
                    .Where(c => c.Normal.Y > 0.0 &&
                        FindPlatform(c.ColliderId) is not null)
                    .OrderByDescending(c => c.TimeWithinStepSeconds)
                    .ThenBy(c => c.ColliderId)
                    .Select(c => (CharacterControlContact?)c)
                    .FirstOrDefault();
                if (movingLanding is { } finalContact &&
                    FindPlatform(finalContact.ColliderId) is { } landingPlatform)
                {
                    nextSupport = landingPlatform;
                    // The jump motor ends at the landing event. Carry for
                    // the remaining part of this fixed update, not one tick
                    // later, so there is no visual gap or lost displacement.
                    double landingTime = finalContact.TimeWithinStepSeconds;
                    double remainder = durationSeconds - landingTime;
                    double landingWorldTime = _elapsedSeconds + landingTime;
                    // The analytical sweep can report the exact impact surface
                    // while the body coordinate differs from that surface by
                    // one or two representable increments. The ride solver is
                    // deliberately strict: it requires exact support at its
                    // starting time. Canonicalize only that bounded precision
                    // error before handing off the supported remainder.
                    nextBounds = CanonicalizeMovingSupport(
                        nextBounds, landingPlatform, landingWorldTime);
                    // A landing at the very end of the fixed update can leave
                    // a positive remainder smaller than one ULP of the world
                    // clock. The rider is already at the contact position;
                    // there is no representable carry interval to submit.
                    if (remainder > 0.0 &&
                        landingWorldTime + remainder > landingWorldTime)
                    {
                        // The landing event can happen BEFORE this update ends.
                        // Carry must arbitrate static obstacles exactly as an
                        // ordinary supported ride does. Calling the pure
                        // MovingPlatformRideSolver here would ignore wall 12
                        // (or any other static obstacle), commit a penetrating
                        // end box, and make the NEXT update fail validation.
                        // Preserve the existing zero-relative-acceleration
                        // landing carry; respond to each static contact and
                        // re-query the remainder in the new velocity frame.
                        var landingContacts = new List<CharacterControlContact>(contacts);
                        double carried = 0.0;
                        while (carried < remainder && nextSupport is not null)
                        {
                            double now = landingWorldTime + carried;
                            double pending = remainder - carried;
                            if (now + pending <= now)
                            {
                                // A post-event terminal fragment with no
                                // representable absolute time cannot contain
                                // another independently schedulable event.
                                break;
                            }

                            // Use the CURRENT support rather than assuming
                            // the original landing platform still owns the
                            // rider after an exact-time platform handoff.
                            MovingPlatformTrajectory currentSupport = nextSupport;
                            MovingPlatformSample floor = currentSupport.Evaluate(now);
                            MovingPlatformRideEventResult ride =
                                MovingPlatformRideEventScheduler.Step(
                                    currentSupport, now, pending, nextBounds,
                                    nextVelocity.X - floor.Velocity.X, 0.0,
                                    _obstacles,
                                    _movingPlatforms.Where(p => p.Id != currentSupport.Id).ToArray(),
                                    Math.Max(1, _maxContactEvents - landingContacts.Count));

                            foreach (CharacterControlContact contact in ride.Contacts)
                                landingContacts.Add(new CharacterControlContact(
                                    contact.ColliderId,
                                    landingTime + carried + contact.TimeWithinStepSeconds,
                                    contact.Normal, contact.IncomingVelocity,
                                    contact.OutgoingVelocity));
                            if (landingContacts.Count > _maxContactEvents)
                                throw EventBudgetExceeded(
                                    "Controller event limit exceeded during " +
                                    "post-landing carry.", landingContacts);
                            if (ride.AdvancedSeconds <= 0.0 && ride.Contacts.Count == 0)
                                throw new SimulationNoProgressException(
                                    "Post-landing ride failed to advance or resolve contact.");

                            nextBounds = ride.Bounds;
                            nextVelocity = ride.WorldVelocity;
                            nextSupport = ride.SupportPlatformId is int nextSupportId
                                ? FindPlatform(nextSupportId) ?? throw new SimulationNoProgressException(
                                    "Unknown moving support after landing carry.")
                                : null;
                            nextGrounded = nextSupport is not null ||
                                HasFloorSupport(nextBounds, nextVelocity);
                            carried += ride.AdvancedSeconds;
                        }
                        contacts = landingContacts.ToArray();
                        processed = Math.Min(durationSeconds, landingTime + carried);
                    }
                }
                // Static landing (or walking off the moving support during
                // landing carry) must still arbitrate moving walls for the
                // rest of this tick. The jump result is at impact time.
                // Retain the legacy static-only landing behavior.
                if (nextSupport is null && _movingPlatforms.Length > 0)
                {
                    double resumeTime = _elapsedSeconds + processed;
                    double remaining = endTime - resumeTime;
                    if (remaining > 0.0 && resumeTime + remaining > resumeTime)
                    {
                        var continuation = Fork();
                        continuation._bounds = nextBounds;
                        continuation._velocity = nextVelocity;
                        continuation._elapsedSeconds = resumeTime;
                        continuation._grounded = nextGrounded;
                        continuation._supportPlatform = null;
                        continuation._jumpMotor = null;
                        var tail = continuation.StepFreeOrGround(remaining, direction,
                            faultSession, nextLedgeCatchUsed, nextVelocity, externalHorizontalSpeed);
                        var combinedContacts = contacts.Concat(tail.Contacts.Select(c => new CharacterControlContact(
                            c.ColliderId, processed + c.TimeWithinStepSeconds, c.Normal,
                            c.IncomingVelocity, c.OutgoingVelocity))).ToList();
                        if (combinedContacts.Count > _maxContactEvents)
                            throw EventBudgetExceeded("Controller event limit exceeded during landing continuation.",
                                combinedContacts);
                        contacts = combinedContacts.ToArray();
                        nextBounds = tail.Bounds;
                        nextVelocity = tail.Velocity;
                        nextGrounded = tail.Grounded;
                        nextSupport = tail.Support;
                        stepAssists = stepAssists.Concat(tail.Assists).ToArray();
                        nextLedgeCatchUsed = tail.LedgeCatchUsed;
                    }
                }
            }
        }
        else
        {
            var free = StepFreeOrGround(durationSeconds, direction,
                faultSession, nextLedgeCatchUsed, startVelocity,
                externalHorizontalSpeed);
            nextBounds = free.Bounds;
            nextVelocity = free.Velocity;
            nextGrounded = free.Grounded;
            nextSupport = free.Support;
            contacts = free.Contacts;
            stepAssists = free.Assists;
            nextLedgeCatchUsed = free.LedgeCatchUsed;
        }

        // Every supported body must end on the support plane at the SAME
        // authoritative world time that the controller commits. Individual
        // analytical ride phases evaluate (_elapsedSeconds + elapsed), which
        // can round differently from (_elapsedSeconds + durationSeconds) by a
        // few ULPs after cruise events or platform turnarounds. Without this
        // reconciliation, the next update may incorrectly lose exact support.
        //
        // This only canonicalizes a precision-sized discrepancy. A meaningful
        // separation or penetration is an actual simulation fault, not a
        // license to teleport the rider to its moving floor.
        if (nextSupport is not null)
        {
            MovingPlatformSample supportAtEnd = nextSupport.Evaluate(endTime);
            double supportY = supportAtEnd.Bounds.Top;
            double gap = Math.Abs(nextBounds.Bottom - supportY);
            double clockUlp = Math.BitIncrement(endTime) - endTime;
            double positionUlp = Math.Max(
                Math.BitIncrement(nextBounds.Bottom) - nextBounds.Bottom,
                Math.BitIncrement(supportY) - supportY);
            double roundingBudget = Math.Min(1e-9,
                16.0 * (positionUlp +
                    Math.Abs(supportAtEnd.Velocity.Y) * clockUlp));
            if (!double.IsFinite(roundingBudget) || gap > roundingBudget)
                throw new SimulationNoProgressException(
                    $"Supported rider differs from the authoritative platform plane " +
                    $"by {gap:R} world units (budget {roundingBudget:R}).");

            if (gap != 0.0)
            {
                Aabb aligned = new(
                    new Vector2D(nextBounds.Left, supportY),
                    new Vector2D(nextBounds.Right, supportY + nextBounds.Height));
                foreach (StaticAabbCollider obstacle in _obstacles)
                {
                    if (AabbCollisionDetector.Overlaps(aligned, obstacle.Bounds))
                        throw new SimulationNoProgressException(
                            "Authoritative moving-support alignment would penetrate " +
                            $"static collider {obstacle.Id}.");
                }
                nextBounds = aligned;
            }
        }

        if (OpposingSurfaceCrushDetector.AtPenetratingSideEndpoint(_bounds, nextBounds,
                _elapsedSeconds, endTime, _obstacles, _movingPlatforms) is { } endpointCrush)
            throw new CharacterCrushException(endpointCrush);

        // A static top contact can finish one or two binary64 ULPs below the
        // authored grid surface after multiple analytical motion phases.
        // Align ONLY such representational floor drift before committing.
        // This does not loosen the unified scheduler's strict start validator
        // or convert a meaningful penetration into a successful step.
        if (nextJump is null)
        {
            nextBounds = StaticFloorPrecisionAlignment.Align(nextBounds, _obstacles);
            foreach (StaticAabbCollider obstacle in _obstacles)
                if (AabbCollisionDetector.Overlaps(nextBounds, obstacle.Bounds))
                    throw new SimulationNoProgressException(
                        $"Motor ended penetrating static collider {obstacle.Id}; " +
                        "the failed step was not committed.");
        }

        // All motor paths (including analytical jump and rider handoffs)
        // must publish a box valid against the authoritative moving geometry.
        // Never defer a bad endpoint to the next tick's strict validator.
        OpposingSurfaceCrushDetector.ThrowIfClosingSideContact(nextBounds,
            endTime, _obstacles, _movingPlatforms);
        foreach (var platform in _movingPlatforms)
        {
            var endSample = platform.Evaluate(endTime);
            if (!AabbCollisionDetector.Overlaps(nextBounds, endSample.Bounds)) continue;
            var startSample = platform.Evaluate(_elapsedSeconds);
            string contactTrace = string.Join("; ", contacts.Select(c =>
                $"id={c.ColliderId},t={c.TimeWithinStepSeconds:R},normal={c.Normal}"));
            throw new SimulationNoProgressException(
                $"Motor ended penetrating moving platform {platform.Id}; the failed step was not committed. " +
                $"time=[{_elapsedSeconds:R},{endTime:R}], " +
                $"bodyStart={_bounds}, bodyEnd={nextBounds}, " +
                $"platformStart={startSample.Bounds}, platformEnd={endSample.Bounds}, " +
                $"velocity={nextVelocity}, grounded={nextGrounded}, jumping={nextJump is not null}, " +
                $"support={nextSupport?.Id.ToString() ?? "none"}, contacts=[{contactTrace}]");
        }

        // An inward external transport has been consumed by a hard side
        // contact. Do not retain its *unrealized* velocity as a reverse kick
        // when the wind or dash ends on the next tick. The continuing source
        // can reapply its velocity through collision arbitration next tick.
        double realizedExternalSpeed = externalHorizontalSpeed;
        // Even if a persistent face constraint did not emit a NEW contact
        // this tick, its outward normal proves that inward transport was
        // fully suppressed. Account for the actually realized component
        // rather than inventing a recoil when that source is later removed.
        bool blockedByStaticFace = externalHorizontalSpeed != 0.0 &&
            _obstacles.Any(c =>
            nextBounds.Bottom < c.Bounds.Top && nextBounds.Top > c.Bounds.Bottom &&
            ((externalHorizontalSpeed > 0.0 &&
                Math.Abs(nextBounds.Right - c.Bounds.Left) <= 1e-8) ||
             (externalHorizontalSpeed < 0.0 &&
                Math.Abs(nextBounds.Left - c.Bounds.Right) <= 1e-8)));
        bool blockedByMovingFace = externalHorizontalSpeed != 0.0 &&
            _movingPlatforms.Any(p =>
        {
            MovingPlatformSample sample = p.Evaluate(endTime);
            return nextBounds.Bottom < sample.Bounds.Top &&
                nextBounds.Top > sample.Bounds.Bottom &&
                Math.Abs(nextVelocity.X - sample.Velocity.X) < 1e-8 &&
                ((externalHorizontalSpeed > 0.0 &&
                    Math.Abs(nextBounds.Right - sample.Bounds.Left) <= 1e-8) ||
                 (externalHorizontalSpeed < 0.0 &&
                    Math.Abs(nextBounds.Left - sample.Bounds.Right) <= 1e-8));
        });
        if (externalHorizontalSpeed != 0.0 &&
            ((Math.Abs(nextVelocity.X) < 1e-8 && blockedByStaticFace) ||
             blockedByMovingFace ||
             contacts.Any(c => c.Normal.X * externalHorizontalSpeed < 0.0 &&
                 Math.Abs(nextVelocity.X) < 1e-8)))
        {
            realizedExternalSpeed = 0.0;
            nextJump?.NormalizeBlockedExternalTransport();
        }

        // Never publish a partially completed update: all potentially failing
        // collision calculations above execute before committing these fields.
        _bounds = nextBounds;
        _velocity = nextVelocity;
        _externalHorizontalSpeed = realizedExternalSpeed;
        _grounded = nextGrounded;
        _supportPlatform = nextSupport;
        _onMovingPlatform = nextSupport is not null;
        _jumpMotor = nextJump;
        _ledgeCatchUsed = nextGrounded ? false : nextLedgeCatchUsed;
        _elapsedSeconds = endTime;
        if (started) _jumpsStarted++;
        LastStepRecoveries = faultSession.Recoveries;
        LastStepAssists = Array.AsReadOnly(stepAssists.ToArray());
        LastStepContactTrace = Array.AsReadOnly(contacts.Select(c =>
            new SimulationContactTrace(c.ColliderId,
                c.TimeWithinStepSeconds, c.Normal)).ToArray());
        return Snapshot(started, durationSeconds, contacts);
        }
        catch (Exception error) when (error is SimulationFaultException or
            SimulationEventLimitException or SimulationNoProgressException or
            CharacterCrushException or PlatformObstructionException)
        {
            // All live motion was computed in locals and the jump was forked.
            // Keep the pre-update state; only diagnostic metadata changes.
            LastFault = new SimulationFaultSnapshot(
                _elapsedSeconds, _bounds, _velocity, _grounded,
                MovingSupportId, error.GetType().Name, error.Message,
                error is SimulationEventLimitException budget ? budget.Contacts :
                    Array.Empty<SimulationContactTrace>());
            throw;
        }
    }

    private (Aabb Bounds, Vector2D Velocity, bool Grounded,
        MovingPlatformTrajectory? Support, CharacterControlContact[] Contacts,
        CollisionAssistEvent[] Assists, bool LedgeCatchUsed) StepFreeOrGround(
        double duration, HorizontalDirection direction,
        SimulationFaultSession faultSession, bool ledgeCatchUsed,
        Vector2D startingVelocity, double externalHorizontalSpeed)
    {
        Aabb bounds = _bounds;
        Vector2D velocity = startingVelocity;
        bool grounded = _grounded;
        MovingPlatformTrajectory? support = _supportPlatform;
        double elapsed = 0.0;
        int supportDepartures = 0;
        var contacts = new List<CharacterControlContact>();
        var assists = new List<CollisionAssistEvent>();

        // Diagnostic-only loop watchdog. Analytical phase boundaries may be
        // smaller than one representable clock increment; a re-plan must
        // eventually advance time, change the constraint state, or fail.
        // This guard does not alter position, velocity, collision policy, or
        // normal step results. It only reports a suspected infinite loop.
        const int maxFreeGroundIterations = 16384;
        const int maxSameTimeIterations = 128;
        int freeGroundIterations = 0;
        int sameTimeIterations = 0;
        double previousLoopElapsed = double.NaN;
        string lastPath = "entry";

        // Re-plan at horizontal acceleration phases and support departures.
        // Walking off a ledge switches to AIR acceleration in the same update.
        // The accelerated stepper may process an internal landing; ground
        // profile selection after that landing occurs at the next phase/step.
        while (elapsed < duration)
        {
            ++freeGroundIterations;
            if (elapsed == previousLoopElapsed)
                ++sameTimeIterations;
            else
            {
                previousLoopElapsed = elapsed;
                sameTimeIterations = 0;
            }
            if (freeGroundIterations > maxFreeGroundIterations ||
                sameTimeIterations > maxSameTimeIterations)
            {
                double selfVelocity = velocity.X - externalHorizontalSpeed;
                double speedLimit = grounded ?
                    _groundMotion.MaximumSpeed : _airMotion.MaximumSpeed;
                throw new SimulationNoProgressException(
                    $"Free/ground analytical no-progress watchdog: " +
                    $"iterations={freeGroundIterations}, " +
                    $"sameTimeIterations={sameTimeIterations}, lastPath={lastPath}, " +
                    $"elapsed={elapsed:R}, duration={duration:R}, " +
                    $"worldTime={_elapsedSeconds + elapsed:R}, " +
                    $"direction={direction}, grounded={grounded}, " +
                    $"supportId={(support is null ? "none" : support.Id.ToString())}, " +
                    $"velocityX={velocity.X:R}, velocityY={velocity.Y:R}, " +
                    $"personalVelocityX={selfVelocity:R}, " +
                    $"speedLimit={speedLimit:R}, externalX={externalHorizontalSpeed:R}, " +
                    $"bounds=({bounds.Left:R}, {bounds.Bottom:R}, " +
                    $"{bounds.Right:R}, {bounds.Top:R}), " +
                    $"contacts={contacts.Count}, supportDepartures={supportDepartures}.");
            }

            double remaining = duration - elapsed;
            if (support is null)
                bounds = StaticFloorPrecisionAlignment.Align(bounds, _obstacles);
            // Last-resort repair for a small overlap inherited from a previous
            // contact event. The pure sweep detector remains strict; deep or
            // ambiguous intersections still fail with useful diagnostics.
            if (support is null)
            {
                if (contacts.Count > 0)
                    OpposingSurfaceCrushDetector.ThrowIfClosingSideContact(bounds,
                        _elapsedSeconds + elapsed, _obstacles, _movingPlatforms);
                bool corrected = false;
                foreach (MovingPlatformTrajectory candidate in _movingPlatforms)
                {
                    MovingPlatformSample snapshot = candidate.Evaluate(
                        _elapsedSeconds + elapsed);
                    MovingPlatformOverlapCorrection? repair =
                        MovingPlatformOverlapResolver.Resolve(bounds, snapshot, _obstacles);
                    if (repair is not { } correction) continue;
                    Vector2D incoming = velocity;
                    bounds = correction.Bounds;
                    velocity = CollisionResponse.Apply(
                        velocity - snapshot.Velocity, correction.Normal,
                        CollisionResponseMode.Slide) + snapshot.Velocity;
                    contacts.Add(new CharacterControlContact(
                        correction.PlatformId, elapsed, correction.Normal,
                        incoming, velocity));
                    if (contacts.Count + supportDepartures > _maxContactEvents)
                        throw EventBudgetExceeded("Controller event limit exceeded.", contacts);
                    if (correction.Normal.Y > 0.0)
                    {
                        support = candidate;
                        grounded = true;
                    }
                    else
                    {
                        // A side contact does not erase an existing static
                        // floor constraint; walking into a descending elevator
                        // must not suddenly turn grounded locomotion into fall.
                        grounded = HasFloorSupport(bounds, velocity);
                    }
                    corrected = true;
                    break;
                }
                if (corrected)
                {
                    lastPath = "moving-overlap-repair";
                    continue;
                }
            }
            if (support is not null)
            {
                double movingFloorWorldTime = _elapsedSeconds + elapsed;
                // Internal support/turn events can sum to a slightly different
                // absolute timestamp than the controller's committed clock.
                // Preserve exact ride-solver preconditions at each handoff;
                // anything larger than representational drift remains a fault.
                bounds = CanonicalizeMovingSupport(
                    bounds, support, movingFloorWorldTime);
                MovingPlatformSample floor = support.Evaluate(movingFloorWorldTime);
                double localV = velocity.X - floor.Velocity.X - externalHorizontalSpeed;
                // On a moving support with wind, the stored world velocity may
                // already be the exact representable sum of platform + current
                // + requested cruise, while subtracting those contributions
                // reconstructs a relative speed a few ULPs outside the limit.
                // Reassigning the cruise world velocity then changes NO bits,
                // and the sub-ULP phase is selected forever without clock
                // progress. Use the exact relative cruise only when the stored
                // world velocity ALREADY equals that representable sum. A real
                // overspeed remains on the normal analytical deceleration path.
                if (direction != HorizontalDirection.Neutral)
                {
                    double cruiseLocal = (int)direction * _groundMotion.MaximumSpeed;
                    double cruiseWorld = floor.Velocity.X + externalHorizontalSpeed + cruiseLocal;
                    if (velocity.X == cruiseWorld)
                        localV = cruiseLocal;
                }
                HorizontalMotionTrajectory relative = _groundMotion.CreateTrajectory(
                    0.0, localV, direction);
                double rideDuration = remaining;
                if (relative.TimeToCruiseSeconds > 0.0)
                    rideDuration = Math.Min(rideDuration, relative.TimeToCruiseSeconds);
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
                        velocity = new Vector2D(
                            floor.Velocity.X + externalHorizontalSpeed, velocity.Y);
                        lastPath = "moving-support-subresolution-reversal";
                        continue;
                    }
                    if (relative.TimeToCruiseSeconds > 0.0 &&
                        rideDuration == relative.TimeToCruiseSeconds)
                    {
                        velocity = new Vector2D(
                            floor.Velocity.X + externalHorizontalSpeed +
                            relative.CruiseVelocity, velocity.Y);
                        lastPath = "moving-support-subresolution-cruise";
                        continue;
                    }
                    if (rideDuration == remaining)
                    {
                        // A terminal remainder with no representable clock
                        // progress has no additional platform displacement.
                        elapsed = duration;
                        break;
                    }
                    throw new SimulationNoProgressException(
                        "Moving-floor event cannot advance either simulation clock.");
                }

                // Check static obstructions during the SUPPORTED portion of
                // this ride. A wall or ceiling can be encountered while the
                // floor carries the player. The support platform itself is
                // excluded from sweeps; it is an existing constraint.
                double relativeAcceleration = relative.Evaluate(0.0).Acceleration;
                foreach (StaticAabbCollider obstacle in _obstacles)
                {
                    Aabb wall = obstacle.Bounds;
                    bool verticalOverlap = bounds.Bottom < wall.Top && bounds.Top > wall.Bottom;
                    int wallNormalX = verticalOverlap &&
                        Math.Abs(bounds.Right - wall.Left) <= 1e-8 ? -1 :
                        verticalOverlap && Math.Abs(bounds.Left - wall.Right) <= 1e-8 ? 1 : 0;
                    if (wallNormalX != 0 && relativeAcceleration * wallNormalX < 0.0)
                        relativeAcceleration = 0.0;
                }

                // A second moving side can constrain the supported rider's
                // horizontal acceleration just like a static wall. Without
                // this pre-constraint, a fresh neutral-input profile can
                // repeatedly accelerate into an already flush moving side
                // at t=0 on each supported ride leg.
                foreach (MovingPlatformTrajectory candidate in _movingPlatforms)
                {
                    if (candidate.Id == support.Id) continue;
                    MovingPlatformSample movingSide = candidate.Evaluate(movingFloorWorldTime);
                    Aabb side = movingSide.Bounds;
                    bool verticallyOverlapping = bounds.Bottom < side.Top &&
                        bounds.Top > side.Bottom;
                    int normalX = verticallyOverlapping &&
                        Math.Abs(bounds.Right - side.Left) <= 1e-8 ? -1 :
                        verticallyOverlapping &&
                        Math.Abs(bounds.Left - side.Right) <= 1e-8 ? 1 : 0;
                    if (normalX != 0 &&
                        (velocity.X - movingSide.Velocity.X) * normalX <= 0.0 &&
                        relativeAcceleration * normalX < 0.0)
                        relativeAcceleration = 0.0;
                }

                // Two moving platforms can also squeeze the supported
                // rider. Classify a closing vertical gap BEFORE computing
                // a trajectory that would have to penetrate the upper one.
                CharacterCrushEvent? movingSqueeze =
                    OpposingSurfaceCrushDetector.AboveMovingSupport(
                        support, bounds, movingFloorWorldTime, rideDuration,
                        localV + externalHorizontalSpeed, relativeAcceleration, _movingPlatforms);
                if (movingSqueeze is not null)
                    throw new CharacterCrushException(movingSqueeze);

                MovingPlatformRideEventResult ride = MovingPlatformRideEventScheduler.Step(
                    support, movingFloorWorldTime, rideDuration, bounds,
                    localV + externalHorizontalSpeed, relativeAcceleration, _obstacles,
                    _movingPlatforms.Where(p => p.Id != support.Id).ToArray(),
                    Math.Max(1, _maxContactEvents - contacts.Count - supportDepartures));
                foreach (CharacterControlContact contact in ride.Contacts)
                    contacts.Add(new CharacterControlContact(
                        contact.ColliderId,
                        elapsed + contact.TimeWithinStepSeconds,
                        contact.Normal, contact.IncomingVelocity,
                        contact.OutgoingVelocity));
                if (contacts.Count + supportDepartures > _maxContactEvents)
                    throw EventBudgetExceeded(
                        "Controller event limit exceeded during moving-platform riding.",
                        contacts);
                if (!(ride.AdvancedSeconds > 0.0) && ride.Contacts.Count == 0)
                    throw new SimulationNoProgressException(
                        "Moving-floor segment did not advance or resolve a collision.");
                bounds = ride.Bounds;
                velocity = ride.WorldVelocity;
                support = ride.SupportPlatformId is int newSupportId
                    ? FindPlatform(newSupportId) ?? throw new SimulationNoProgressException(
                        "Unknown moving support after ride collision.")
                    : null;
                grounded = support is not null || HasFloorSupport(bounds, velocity);
                elapsed += ride.AdvancedSeconds;
                lastPath = "moving-support-ride";
                if (support is null)
                {
                    if (++supportDepartures + contacts.Count > _maxContactEvents)
                        throw EventBudgetExceeded("Controller event limit exceeded.", contacts);
                }
                continue;
            }
            HorizontalMotionProfile profile = grounded ? _groundMotion : _airMotion;
            HorizontalMotionTrajectory reference = profile.CreateTrajectory(
                0.0, velocity.X - externalHorizontalSpeed, direction);

            double phaseDuration = remaining;
            if (reference.TimeToCruiseSeconds > 0.0 &&
                reference.TimeToCruiseSeconds < phaseDuration)
                phaseDuration = reference.TimeToCruiseSeconds;
            if (direction != HorizontalDirection.Neutral &&
                (int)direction * (velocity.X - externalHorizontalSpeed) < 0.0)
            {
                double reversalStop = Math.Abs(velocity.X - externalHorizontalSpeed) /
                    profile.ReversalDecelerationRate;
                if (reversalStop > 0.0 && reversalStop < phaseDuration)
                    phaseDuration = reversalStop;
            }

            double ax = reference.Evaluate(0.0).Acceleration;
            // A constant external current can leave the body exactly flush
            // with a static side face while its personal neutral-braking
            // profile still requests inward acceleration. Constrain that
            // acceleration BEFORE sending a t=0 wall event to the scheduler.
            // This does not alter legacy (no external transport) motion.
            if (externalHorizontalSpeed != 0.0)
            {
                foreach (StaticAabbCollider wallCollider in _obstacles)
                {
                    Aabb wallBounds = wallCollider.Bounds;
                    bool verticalOverlap = bounds.Bottom < wallBounds.Top &&
                        bounds.Top > wallBounds.Bottom;
                    int normalX = verticalOverlap &&
                        Math.Abs(bounds.Right - wallBounds.Left) <= 1e-8 ? -1 :
                        verticalOverlap &&
                        Math.Abs(bounds.Left - wallBounds.Right) <= 1e-8 ? 1 : 0;
                    if (normalX != 0 && velocity.X * normalX <= 0.0 &&
                        ax * normalX < 0.0)
                        ax = 0.0;
                }
            }
            if (_movingPlatforms.Length > 0)
            {
                // Both static-grounded and airborne bodies must arbitrate
                // against moving platforms. Ignoring them while grounded was
                // the cause of the descending-elevator side penetration.
                foreach (MovingPlatformTrajectory candidate in _movingPlatforms)
                {
                MovingPlatformSample wall = candidate.Evaluate(
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
                            throw EventBudgetExceeded(
                                "Controller event limit exceeded.", contacts);
                    }
                    if (ax * normalX < 0.0)
                        ax = 0.0;
                }
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
                    lastPath = "static-subresolution-ledge-departure";
                    if (++supportDepartures + contacts.Count > _maxContactEvents)
                        throw EventBudgetExceeded("Controller event limit exceeded.", contacts);
                    continue;
                }

                double reversalStop = direction != HorizontalDirection.Neutral &&
                    (int)direction * (velocity.X - externalHorizontalSpeed) < 0.0
                    ? Math.Abs(velocity.X - externalHorizontalSpeed) / profile.ReversalDecelerationRate
                    : 0.0;
                if (reversalStop > 0.0 && reversalStop == advance)
                {
                    velocity = new Vector2D(externalHorizontalSpeed, velocity.Y);
                    lastPath = "static-subresolution-reversal";
                    continue;
                }

                if (reference.TimeToCruiseSeconds > 0.0 &&
                    reference.TimeToCruiseSeconds == advance)
                {
                    velocity = new Vector2D(
                        reference.CruiseVelocity + externalHorizontalSpeed, velocity.Y);
                    lastPath = "static-subresolution-cruise";
                    continue;
                }

                if (advance == remaining)
                {
                    // Only an unrepresentable final remainder remains; the
                    // absolute simulation clock has no distinct event in it.
                    elapsed = duration;
                    break;
                }

                throw new SimulationNoProgressException(
                    "Motion event cannot advance either the local or world clock.");
            }

            // The crush classifiers evaluate moving bodies at both endpoints.
            // Only submit a query after the known sub-resolution phase events
            // above have been normalized. In particular, the absolute world
            // clock must represent worldNow + advance as a distinct time.
            // A descending overhead platform can pin a character to a
            // STATIC floor. Classify the opposing surfaces before the unified
            // stepper tries to drive the character through that floor.
            if (_movingPlatforms.Length > 0)
            {
                double projectedDx = velocity.X * advance + 0.5 * ax * advance * advance;
                // Horizontal crushing is possible in free fall as well as on
                // static ground. Use the actual uncollided vertical motion
                // in the airborne case so a character escaping the pinch
                // vertically is not falsely classified as crushed.
                double projectedDy = grounded ? 0.0 :
                    velocity.Y * advance + 0.5 * _gravity * advance * advance;
                Aabb projectedBody = bounds.Translated(
                    new Vector2D(projectedDx, projectedDy));

                // A ceiling-to-floor crush requires static floor support.
                // Preserve that stricter precondition for the vertical case.
                if (grounded)
                {
                    CharacterCrushEvent? overheadCrush =
                        OpposingSurfaceCrushDetector.AboveStaticGround(
                            bounds, projectedBody, _elapsedSeconds + elapsed,
                            advance, _obstacles, _movingPlatforms);
                    if (overheadCrush is not null)
                        throw new CharacterCrushException(overheadCrush);
                }

                CharacterCrushEvent? horizontalCrush =
                    OpposingSurfaceCrushDetector.AcrossClosingSideSurfaces(
                        bounds, projectedBody, _elapsedSeconds + elapsed,
                        advance, _obstacles, _movingPlatforms);
                if (horizontalCrush is not null)
                    throw new CharacterCrushException(horizontalCrush);
            }

            Vector2D acceleration = new(ax, grounded ? 0.0 : _gravity);
            if (_movingPlatforms.Length > 0)
            {
                // All unsupported motion uses the SAME unified scheduler,
                // whether grounded on static scenery or airborne. A grounded
                // body must not ignore a descending moving side wall; doing
                // so formerly created a positive end-of-step overlap.
                int availableContacts = _maxContactEvents - contacts.Count - supportDepartures;
                if (availableContacts < 1)
                    throw EventBudgetExceeded(
                        "Controller collision event budget exhausted.", contacts);

                UnifiedAcceleratedStepResult unified =
                    UnifiedAcceleratedKinematicStepper.Step(
                        bounds, velocity, acceleration, _elapsedSeconds + elapsed,
                        advance, _obstacles, _movingPlatforms,
                        CollisionResponseMode.Slide, availableContacts, faultSession);
                if (contacts.Count + unified.Contacts.Count + supportDepartures >
                    _maxContactEvents)
                    throw EventBudgetExceeded(
                        "Controller collision event limit exceeded.", contacts);

                foreach (CharacterControlContact unifiedContact in unified.Contacts)
                    contacts.Add(new CharacterControlContact(
                        unifiedContact.ColliderId, elapsed + unifiedContact.TimeWithinStepSeconds,
                        unifiedContact.Normal, unifiedContact.IncomingVelocity,
                        unifiedContact.OutgoingVelocity));

                bounds = unified.Bounds;
                velocity = unified.Velocity;
                elapsed += unified.AdvancedSeconds;
                lastPath = "unified-moving-static-collision-step";
                if (unified.MovingFloorId is int landingId)
                {
                    // Transfer to the specific platform that was hit.
                    support = FindPlatform(landingId) ?? throw new
                        SimulationNoProgressException("Unknown moving support ID.");
                    grounded = true;
                }
                else
                {
                    support = null;
                    grounded = HasFloorSupport(bounds, velocity);
                }
                continue;
            }
            // Ground and static-only free motion now use the SAME unified
            // event stepper as the airborne static/moving branch. Support
            // departure still truncates the phase at its analytical boundary.
            int staticEventBudget = _maxContactEvents - contacts.Count - supportDepartures;
            if (staticEventBudget < 1)
                throw EventBudgetExceeded(
                    "Controller event limit exceeded during ground motion.", contacts);
            UnifiedAcceleratedStepResult result = UnifiedAcceleratedKinematicStepper.Step(
                bounds, velocity, acceleration, _elapsedSeconds + elapsed,
                advance, _obstacles, Array.Empty<MovingPlatformTrajectory>(),
                CollisionResponseMode.Slide, staticEventBudget, faultSession);

            if (contacts.Count + result.Contacts.Count + supportDepartures > _maxContactEvents)
                throw EventBudgetExceeded("Controller event limit exceeded.", contacts);
            foreach (CharacterControlContact contact in result.Contacts)
                contacts.Add(new CharacterControlContact(
                    contact.ColliderId, elapsed + contact.TimeWithinStepSeconds,
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
                    throw EventBudgetExceeded("Controller event limit exceeded.", contacts);
            }
            grounded = nextGrounded;
            elapsed += advance;
            lastPath = "unified-static-collision-step";
        }
        // Normalize a precision-sized moving SIDE corner overlap at the
        // authoritative end time before the general bounded overlap resolver.
        // A lift brushing the player's far edge must not become a false
        // head-on vertical collision merely because X drifted a few ULPs.
        if (OpposingSurfaceCrushDetector.AtPenetratingSideEndpoint(_bounds, bounds,
                _elapsedSeconds, _elapsedSeconds + duration, _obstacles, _movingPlatforms) is { } endpointCrush)
            throw new CharacterCrushException(endpointCrush);
        OpposingSurfaceCrushDetector.ThrowIfClosingSideContact(bounds,
            _elapsedSeconds + duration, _obstacles, _movingPlatforms);
        bounds = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
            bounds, _elapsedSeconds + duration, _movingPlatforms, _obstacles);

        // The moving platform is evaluated from the authoritative clock, while
        // the character has been integrated across one or more analytical
        // segments. A perfectly constrained side contact can differ by a few
        // ULPs at the END of the step even if it was separated at the start.
        // Reconcile only a precision-sized overlap BEFORE committing state.
        // This is NOT a general penetration-recovery budget and does not hide
        // a missed moving-collider contact or allow deep interpenetration.
        foreach (MovingPlatformTrajectory candidate in _movingPlatforms)
        {
            if (support == candidate) continue;
            MovingPlatformSample platformAtEnd = candidate.Evaluate(
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
                            throw EventBudgetExceeded(
                                "Controller event limit exceeded.", contacts);
                    }
                    if (correction.Normal.Y > 0.0 &&
                        relativeVelocityAtEnd.Y <= 0.0)
                    {
                        grounded = true;
                        support = candidate;
                    }
                }
            }
        }
        // Optional gameplay assistance occurs after the entire continuous
        // collision update and end-of-step precision validation. It cannot
        // skip a collision or alter the stepper's event budget. Each complete
        // correction path is checked against both collider sets by the planner.
        double assistTime = _elapsedSeconds + duration;
        if (support is null && grounded)
        {
            GroundContactAssistPlan? step = GroundContactAssistPlanner.TryStepUp(
                bounds, velocity, direction, _assistancePolicy,
                _obstacles, _movingPlatforms, assistTime);
            if (step is { } plan)
            {
                bounds = plan.Bounds;
                // The step's top must be exact static support. Do not install
                // artificial upward velocity or change horizontal acceleration.
                if (!HasFloorSupport(bounds, velocity))
                    throw new SimulationNoProgressException(
                        "Ground step assistance did not establish exact support.");
                assists.Add(plan.Event);
                grounded = true;
            }
        }
        else if (support is null && !grounded && !ledgeCatchUsed)
        {
            GroundContactAssistPlan? catchPlan =
                GroundContactAssistPlanner.TryLedgeCatch(
                    bounds, velocity, direction, _assistancePolicy,
                    _obstacles, _movingPlatforms, assistTime);
            if (catchPlan is { } plan)
            {
                bounds = plan.Bounds;
                assists.Add(plan.Event);
                ledgeCatchUsed = true;
                grounded = HasFloorSupport(bounds, velocity);
            }
        }
        return (bounds, velocity, grounded, support,
            contacts.ToArray(), assists.ToArray(), ledgeCatchUsed);
    }

    /// <summary>
    /// Enforce the exact moving-support entry invariant at an analytically
    /// resolved landing or ride-phase boundary. A swept contact is calculated
    /// with one sequence of floating-point additions; the support sample may
    /// be evaluated with another. Only a precision-sized discrepancy may be
    /// normalized. Never reinterpret a real gap, lost lateral coverage, or
    /// overlap with another collider as a valid ride.
    /// </summary>
    private Aabb CanonicalizeMovingSupport(
        Aabb body, MovingPlatformTrajectory platform, double worldTime)
    {
        Aabb floor = platform.Evaluate(worldTime).Bounds;
        if (MovingPlatformRideSolver.IsSupported(body, floor))
            return body;

        if (body.Left >= floor.Right || body.Right <= floor.Left)
            throw new SimulationNoProgressException(
                $"Moving support {platform.Id} no longer overlaps the rider horizontally.");

        double clockUlp = Math.BitIncrement(worldTime) - worldTime;
        double bodyUlp = Math.BitIncrement(body.Bottom) - body.Bottom;
        double floorUlp = Math.BitIncrement(floor.Top) - floor.Top;
        double gap = Math.Abs(body.Bottom - floor.Top);
        double budget = Math.Min(1e-9, 16.0 *
            (bodyUlp + floorUlp +
             Math.Abs(platform.Evaluate(worldTime).Velocity.Y) * clockUlp));
        if (!double.IsFinite(budget) || gap > budget)
            throw new SimulationNoProgressException(
                $"Moving support {platform.Id} has a real support-plane gap " +
                $"({gap:R} world units; precision budget {budget:R}).");

        Aabb aligned = new(
            new Vector2D(body.Left, floor.Top),
            new Vector2D(body.Right, floor.Top + body.Height));
        foreach (StaticAabbCollider obstacle in _obstacles)
            if (AabbCollisionDetector.Overlaps(aligned, obstacle.Bounds))
                throw new SimulationNoProgressException(
                    $"Moving support alignment intersects static collider {obstacle.Id}.");
        foreach (MovingPlatformTrajectory other in _movingPlatforms)
            if (other != platform && AabbCollisionDetector.Overlaps(
                aligned, other.Evaluate(worldTime).Bounds))
                throw new SimulationNoProgressException(
                    $"Moving support alignment intersects moving collider {other.Id}.");

        return aligned;
    }

    private MovingPlatformTrajectory? FindPlatform(int id) =>
        _movingPlatforms.FirstOrDefault(platform => platform.Id == id);

    private static SimulationEventLimitException EventBudgetExceeded(
        string reason, List<CharacterControlContact> contacts) =>
        new(reason, contacts.Select(c => new SimulationContactTrace(
            c.ColliderId, c.TimeWithinStepSeconds, c.Normal)).ToArray());

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
            jumpStarted, _jumpsStarted, _elapsedSeconds, duration, contacts,
            LastStepAssists.ToArray());
}