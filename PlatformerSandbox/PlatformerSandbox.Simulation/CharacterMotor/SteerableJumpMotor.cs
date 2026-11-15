using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Collision.Contacts;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Authoritative, steerable jump motion with independent axis policies:
/// - Y follows the exact designer flight profile until ceiling/floor contact.
/// - X re-solves the air-control profile from the current velocity each update.
/// - An X-normal wall impact constrains X only; it never changes the Y law.
/// - Y-normal contact ends the reference and starts analytical gravity, or lands.
///
/// Each monotone motion interval uses exact analytical positions and a
/// continuous face-crossing collision query. No MonoGame or device dependency.
/// Static and multiple moving AABBs / slide response. An optional, one-time jump release
/// replaces the held reference with the analytically continuous cut profile.
/// No wall jump or same-update ground locomotion after landing.
/// </summary>
public sealed class SteerableJumpMotor
{
    private JumpFlightTrajectory _flight;
    private readonly JumpReleaseConstraints? _releaseConstraints;
    private readonly JumpDescentConstraints _descentConstraints;
    private readonly HorizontalMotionProfile _airMotion;
    private readonly StaticAabbCollider[] _obstacles;
    private readonly double _gravity;
    private readonly int _maxContactEvents;
    private readonly CollisionAssistancePolicy _assistancePolicy;
    private int _ceilingCorrectionsUsed;
    private readonly MovingPlatformTrajectory? _movingPlatform;
    private readonly MovingPlatformTrajectory[] _movingPlatformColliders;
    // Inherited at takeoff once. The airborne reference remains relative to
    // the launch frame; subsequent platform reversals cannot re-parent it.
    private readonly double _inheritedVerticalVelocity;
    private double _worldTimeSeconds;

    private Aabb _bounds;
    private Vector2D _velocity;
    private double _flightElapsedSeconds;
    private bool _referenceVertical = true;
    private bool _grounded;
    private int _blockedDirection;

    public SteerableJumpMotor(
        Aabb takeoffBounds,
        double inheritedHorizontalVelocity,
        HorizontalMotionProfile airMotion,
        JumpFlightTrajectory flight,
        IReadOnlyList<StaticAabbCollider> obstacles,
        double gravityAcceleration,
        int maxContactEvents = 8,
        JumpReleaseConstraints? releaseConstraints = null,
        JumpDescentConstraints descentConstraints = default,
        MovingPlatformTrajectory? movingPlatform = null, double startTimeSeconds = 0.0,
        double inheritedVerticalVelocity = 0.0,
        IReadOnlyList<MovingPlatformTrajectory>? additionalMovingPlatforms = null,
        CollisionAssistancePolicy? assistancePolicy = null)
    {
        if (!takeoffBounds.IsValid)
            throw new ArgumentException("Invalid initial bounds.", nameof(takeoffBounds));
        if (!double.IsFinite(inheritedHorizontalVelocity))
            throw new ArgumentOutOfRangeException(nameof(inheritedHorizontalVelocity));
        if (!double.IsFinite(inheritedVerticalVelocity))
            throw new ArgumentOutOfRangeException(nameof(inheritedVerticalVelocity));
        ArgumentNullException.ThrowIfNull(airMotion);
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!double.IsFinite(gravityAcceleration) || gravityAcceleration >= 0.0)
            throw new ArgumentOutOfRangeException(nameof(gravityAcceleration));
        if (maxContactEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxContactEvents));
        if (releaseConstraints is { } constraints &&
            (!double.IsFinite(constraints.MinimumHeight) ||
             constraints.MinimumHeight <= 0.0 ||
             constraints.MinimumHeight >= flight.PeakHeight ||
             flight.IsEarlyReleased))
        {
            throw new ArgumentOutOfRangeException(nameof(releaseConstraints),
                "Release requires an uncut held jump and a minimum height between zero and full height.");
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

        _obstacles = obstacles.ToArray();
        var ids = new HashSet<int>();
        foreach (StaticAabbCollider obstacle in _obstacles)
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!ids.Add(obstacle.Id))
                throw new ArgumentException("Collider IDs must be unique.", nameof(obstacles));
            if (AabbCollisionDetector.Overlaps(takeoffBounds, obstacle.Bounds))
                throw new InvalidOperationException("The jumping body starts in penetration.");
        }

        if (!double.IsFinite(startTimeSeconds) || startTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(startTimeSeconds));
        _movingPlatform = movingPlatform;
        var platforms = new List<MovingPlatformTrajectory>();
        if (movingPlatform is not null) platforms.Add(movingPlatform);
        if (additionalMovingPlatforms is not null)
            platforms.AddRange(additionalMovingPlatforms);
        foreach (MovingPlatformTrajectory platform in platforms)
        {
            ArgumentNullException.ThrowIfNull(platform);
            if (!ids.Add(platform.Id))
                throw new ArgumentException("Moving collider IDs must be globally unique.",
                    nameof(additionalMovingPlatforms));
            if (AabbCollisionDetector.Overlaps(takeoffBounds,
                    platform.Evaluate(startTimeSeconds).Bounds))
                throw new InvalidOperationException("Jump starts penetrating a moving platform.");
        }
        _movingPlatformColliders = platforms.ToArray();
        _inheritedVerticalVelocity = inheritedVerticalVelocity;
        _worldTimeSeconds = startTimeSeconds;
        _airMotion = airMotion;
        _flight = flight;
        _releaseConstraints = releaseConstraints;
        _descentConstraints = descentConstraints;
        _gravity = gravityAcceleration;
        _maxContactEvents = maxContactEvents;
        _assistancePolicy = assistancePolicy ?? CollisionAssistancePolicy.Disabled;
        _bounds = takeoffBounds;
        _velocity = new Vector2D(inheritedHorizontalVelocity,
            flight.Evaluate(0.0).VerticalVelocity + _inheritedVerticalVelocity);
    }

    public Aabb Bounds => _bounds;
    public Vector2D Velocity => _velocity;
    public double FlightElapsedSeconds => _flightElapsedSeconds;
    public bool IsGrounded => _grounded;
    public bool IsEarlyReleased => _flight.IsEarlyReleased;

    /// <summary>
    /// Copy the mutable jump progress before a parent-controller update.
    /// The shared dependencies and trajectory profiles are read-only. The
    /// copied motor commits its progress only when the parent step succeeds.
    /// </summary>
    internal SteerableJumpMotor Fork() => (SteerableJumpMotor)MemberwiseClone();

    /// <summary>
    /// The direction is a current input command, not a future fixed schedule.
    /// Local calculations are committed atomically after the full step succeeds.
    /// </summary>
    public SteerableJumpStepResult Step(
        double durationSeconds, HorizontalDirection direction, bool jumpReleasedThisStep = false)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (direction is not (HorizontalDirection.Left or
            HorizontalDirection.Neutral or HorizontalDirection.Right))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (durationSeconds == 0.0 || _grounded)
            return new SteerableJumpStepResult(_bounds, _velocity, _grounded, []);

        Aabb bounds = _bounds;
        Vector2D velocity = _velocity;
        double flightTime = _flightElapsedSeconds;
        JumpFlightTrajectory flight = _flight;

        // The host reports release as an input edge at a fixed-update boundary.
        // A cut is only meaningful while the held reference is still rising.
        // The existing JumpFlightSolver preserves displacement and velocity at
        // the release instant and derives the complete cut ascent/descent.
        // Keep this local until the entire collision step succeeds.
        if (jumpReleasedThisStep && _releaseConstraints is { } cutConfig &&
            _referenceVertical && !flight.IsEarlyReleased &&
            flightTime < flight.ApexTimeSinceTakeoffSeconds &&
            flight.Evaluate(flightTime).VerticalVelocity > 0.0)
        {
            flight = JumpFlightSolver.ForEarlyRelease(
                flight.HeldJump, cutConfig, flightTime, _descentConstraints);
        }
        bool verticalReference = _referenceVertical;
        bool grounded = _grounded;
        int blockedDirection = _blockedDirection;
        double elapsed = 0.0;
        var contacts = new List<CharacterControlContact>();
        var assists = new List<CollisionAssistEvent>();
        int ceilingCorrectionsUsed = _ceilingCorrectionsUsed;

        while (elapsed < durationSeconds && !grounded)
        {
            double remaining = durationSeconds - elapsed;
            // The sweep query must never silently accept initial penetration.
            // A *small* residual overlap may be repaired explicitly, within a
            // bounded distance, before querying the next analytical segment.
            // A side repair keeps the reference Y law unchanged.
            foreach (MovingPlatformTrajectory platform in _movingPlatformColliders)
            {
                MovingPlatformSample snapshot = platform.Evaluate(
                    _worldTimeSeconds + elapsed);
                MovingPlatformOverlapCorrection? repair =
                    MovingPlatformOverlapResolver.Resolve(bounds, snapshot, _obstacles);
                if (repair is { } correction)
                {
                    Vector2D velocityBeforeRecovery = velocity;
                    bounds = correction.Bounds;
                    velocity = CollisionResponse.Apply(
                        velocityBeforeRecovery - snapshot.Velocity, correction.Normal,
                        CollisionResponseMode.Slide) + snapshot.Velocity;
                    if (contacts.Count >= _maxContactEvents)
                        throw JumpEventLimitExceeded(
                            contacts, bounds, velocity, elapsed);
                    contacts.Add(new CharacterControlContact(
                        correction.PlatformId, elapsed, correction.Normal,
                        velocityBeforeRecovery, velocity));
                    if (correction.Normal.X != 0.0)
                        blockedDirection = correction.Normal.X < 0.0 ? 1 : -1;
                    else
                    {
                        verticalReference = false;
                        if (correction.Normal.Y > 0.0)
                            grounded = true;
                    }
                    continue;
                }
            }
            if (verticalReference && flightTime >= flight.TotalDurationSeconds)
                verticalReference = false;

            // A wall blocks only inward horizontal input, and only while the
            // character still touches the wall's vertical span. Steering away
            // or clearing the obstacle's top releases that constraint.
            double currentWorldTime = _worldTimeSeconds + elapsed;
            bool steeringAway = direction != HorizontalDirection.Neutral &&
                (int)direction != blockedDirection;
            if (blockedDirection != 0 &&
                (steeringAway ||
                 !TouchesBlockingWall(bounds, blockedDirection, currentWorldTime)))
                blockedDirection = 0;
            bool blocked = blockedDirection != 0;
            double carrierX = 0.0;
            if (blocked)
                foreach (MovingPlatformTrajectory platform in _movingPlatformColliders)
                    if (TouchesMovingWall(bounds, blockedDirection,
                        currentWorldTime, platform))
                    {
                        carrierX = platform.Evaluate(currentWorldTime).Velocity.X;
                        break;
                    }
            if (blocked)
                velocity = new Vector2D(carrierX, velocity.Y);

            HorizontalMotionTrajectory horizontal = blocked
                ? new HorizontalMotionTrajectory(0.0, carrierX, 0.0,
                    0.0, carrierX, 0.0, 0.0, carrierX, carrierX)
                : _airMotion.CreateTrajectory(0.0, velocity.X, direction);
            double segmentDuration = remaining;

            if (horizontal.TimeToCruiseSeconds > 0.0)
                segmentDuration = Math.Min(segmentDuration, horizontal.TimeToCruiseSeconds);
            if (!blocked && direction != HorizontalDirection.Neutral &&
                (int)direction * velocity.X < 0.0)
            {
                double reversalStopTime = Math.Abs(velocity.X) /
                                          _airMotion.ReversalDecelerationRate;
                segmentDuration = Math.Min(segmentDuration, reversalStopTime);
            }

            if (verticalReference)
            {
                segmentDuration = Math.Min(segmentDuration,
                    flight.TotalDurationSeconds - flightTime);
                if (flightTime < flight.ApexTimeSinceTakeoffSeconds)
                    segmentDuration = Math.Min(segmentDuration,
                        flight.ApexTimeSinceTakeoffSeconds - flightTime);
                if (flight.ReleaseTimeSinceTakeoffSeconds is double releaseMoment && flightTime < releaseMoment)
                    segmentDuration = Math.Min(segmentDuration, releaseMoment - flightTime);
            }
            else if (velocity.Y > 0.0)
            {
                // Free-flight Y reverses direction at v=0. Split there so
                // both position components remain monotone during each query.
                segmentDuration = Math.Min(segmentDuration, velocity.Y / -_gravity);
            }

            // A moving floor's turnaround is an exact event. Relative X
            // velocity may also change sign within an otherwise monotone
            // character segment, so split there for the analytical sweep.
            MovingPlatformSample? floorAtStart = null;
            double relativeTurnSeconds = double.PositiveInfinity;
            if (_movingPlatformColliders.Length > 0)
            {
                double worldNow = _worldTimeSeconds + elapsed;
                MovingPlatformTrajectory referencePlatform =
                    _movingPlatform ?? _movingPlatformColliders[0];
                floorAtStart = referencePlatform.Evaluate(worldNow);
                segmentDuration = Math.Min(segmentDuration,
                    referencePlatform.TimeUntilNextTurn(worldNow));
                foreach (MovingPlatformTrajectory other in _movingPlatformColliders)
                    segmentDuration = Math.Min(segmentDuration,
                        other.TimeUntilNextTurn(worldNow));
                double relativeVx = horizontal.Evaluate(0.0).Velocity -
                                    floorAtStart.Value.Velocity.X;
                double relativeAx = horizontal.Evaluate(0.0).Acceleration;
                relativeTurnSeconds = relativeAx == 0.0 ? double.PositiveInfinity :
                    -relativeVx / relativeAx;
                if (relativeTurnSeconds > 0.0 && relativeTurnSeconds < segmentDuration)
                    segmentDuration = relativeTurnSeconds;
            }
            // A vertical lift can contribute nonzero Y launch velocity.
            // The designer's apex is not necessarily the WORLD-space apex.
            // Split at actual world / moving-platform-relative Y turn events;
            // otherwise a monotone analytical sweep could miss a contact.
            if (verticalReference)
            {
                double beginningVertical = flight.Evaluate(flightTime).VerticalVelocity +
                    _inheritedVerticalVelocity;
                double endingVertical = flight.Evaluate(Math.Min(
                    flightTime + segmentDuration, flight.TotalDurationSeconds)).VerticalVelocity +
                    _inheritedVerticalVelocity;
                if (beginningVertical * endingVertical < 0.0)
                    segmentDuration = Math.Min(segmentDuration,
                        TimeToVerticalVelocity(flight, flightTime, segmentDuration,
                            _inheritedVerticalVelocity));

                if (floorAtStart is { } elevator)
                {
                    endingVertical = flight.Evaluate(Math.Min(
                        flightTime + segmentDuration, flight.TotalDurationSeconds)).VerticalVelocity +
                        _inheritedVerticalVelocity;
                    double relativeBeginning = beginningVertical - elevator.Velocity.Y;
                    double relativeEnding = endingVertical - elevator.Velocity.Y;
                    if (relativeBeginning * relativeEnding < 0.0)
                        segmentDuration = Math.Min(segmentDuration,
                            TimeToVerticalVelocity(flight, flightTime, segmentDuration,
                                _inheritedVerticalVelocity - elevator.Velocity.Y));
                }
            }
            if (!(segmentDuration > 0.0))
                throw new SimulationNoProgressException("Cannot advance an analytical motion event.");

            // A mathematically positive interval is not necessarily a
            // representable change to the absolute simulation clock. Near a
            // speed cap, reversal, or apex, the remaining phase may be smaller
            // than one ULP of world time. Complete that *known* event at this
            // boundary instead of submitting an impossible duration to the
            // unified collision scheduler. We do not approximate a sweep.
            double segmentWorldStart = _worldTimeSeconds + elapsed;
            if (segmentWorldStart + segmentDuration <= segmentWorldStart ||
                elapsed + segmentDuration <= elapsed)
            {
                bool normalizedEvent = false;

                // At cruise the solver specifies the exact final velocity.
                // Rebuilding the input trajectory from that value cannot
                // repeat the same tiny acceleration event indefinitely.
                if (!blocked && horizontal.TimeToCruiseSeconds > 0.0 &&
                    horizontal.TimeToCruiseSeconds <= segmentDuration)
                {
                    velocity = new Vector2D(horizontal.CruiseVelocity, velocity.Y);
                    normalizedEvent = true;
                }
                else if (!blocked && direction != HorizontalDirection.Neutral &&
                    (int)direction * velocity.X < 0.0 &&
                    Math.Abs(velocity.X) / _airMotion.ReversalDecelerationRate
                        <= segmentDuration)
                {
                    // The first phase of a reversal ends exactly at vX=0.
                    velocity = new Vector2D(0.0, velocity.Y);
                    normalizedEvent = true;
                }
                else if (!blocked && floorAtStart is { } nearbyPlatform &&
                    relativeTurnSeconds > 0.0 &&
                    relativeTurnSeconds <= segmentDuration)
                {
                    // Relative X motion reaches its extremum when the
                    // character's X velocity matches the moving surface.
                    velocity = new Vector2D(nearbyPlatform.Velocity.X, velocity.Y);
                    normalizedEvent = true;
                }

                if (verticalReference)
                {
                    if (flight.TotalDurationSeconds > flightTime &&
                        flight.TotalDurationSeconds - flightTime <= segmentDuration)
                    {
                        flightTime = flight.TotalDurationSeconds;
                        velocity = new Vector2D(velocity.X,
                            flight.Evaluate(flightTime).VerticalVelocity +
                            _inheritedVerticalVelocity);
                        verticalReference = false;
                        normalizedEvent = true;
                    }
                    else if (flightTime < flight.ApexTimeSinceTakeoffSeconds &&
                        flight.ApexTimeSinceTakeoffSeconds - flightTime <= segmentDuration)
                    {
                        flightTime = flight.ApexTimeSinceTakeoffSeconds;
                        velocity = new Vector2D(velocity.X,
                            flight.Evaluate(flightTime).VerticalVelocity +
                            _inheritedVerticalVelocity);
                        normalizedEvent = true;
                    }
                    else if (flight.ReleaseTimeSinceTakeoffSeconds is double releaseEvent &&
                        flightTime < releaseEvent &&
                        releaseEvent - flightTime <= segmentDuration)
                    {
                        flightTime = releaseEvent;
                        velocity = new Vector2D(velocity.X,
                            flight.Evaluate(flightTime).VerticalVelocity +
                            _inheritedVerticalVelocity);
                        normalizedEvent = true;
                    }
                }

                if (normalizedEvent)
                    continue;

                // A final remainder smaller than one representable clock
                // increment contains no schedulable movement interval.
                if (remaining == segmentDuration)
                {
                    elapsed = durationSeconds;
                    continue;
                }

                // Do not silently skip an unrecognized collision or platform
                // turnaround. It still needs an explicit event policy.
                throw new SimulationNoProgressException(
                    "An unclassified analytical jump event cannot advance world time.");
            }

            // Capture segment origin for a completely pure analytical curve.
            double startFlightTime = flightTime;
            double startVerticalVelocity = velocity.Y;
            double startVerticalPosition = verticalReference
                ? flight.Evaluate(startFlightTime).VerticalDisplacement : 0.0;
            bool isVerticalReference = verticalReference;

            MotionSample2D Evaluate(double t)
            {
                HorizontalMotionSample x = horizontal.Evaluate(t);
                if (isVerticalReference)
                {
                    JumpSample y = flight.Evaluate(Math.Min(
                        startFlightTime + t, flight.TotalDurationSeconds));
                    return new MotionSample2D(
                        new Vector2D(x.Position,
                            y.VerticalDisplacement - startVerticalPosition +
                            _inheritedVerticalVelocity * t),
                        new Vector2D(x.Velocity,
                            y.VerticalVelocity + _inheritedVerticalVelocity));
                }
                MotionSample yFree = ConstantAcceleration.Evaluate(
                    0.0, startVerticalVelocity, _gravity, t);
                return new MotionSample2D(
                    new Vector2D(x.Position, yFree.Position),
                    new Vector2D(x.Velocity, yFree.Velocity));
            }

            // An airborne player can still be trapped between horizontally
            // closing surfaces. Check the continuously planned motion segment
            // before collision arbitration: otherwise alternating opposing
            // zero-time contacts can exhaust the event budget instead of
            // reporting a physical crush. Like the ground path, require
            // vertical overlap on BOTH ends and an actually closing gap.
            // An upward jump that clears the pusher is not a crush.
            CharacterCrushEvent? horizontalCrush =
                OpposingSurfaceCrushDetector.AcrossClosingSideSurfaces(
                    bounds, bounds.Translated(Evaluate(segmentDuration).Position),
                    segmentWorldStart, segmentDuration,
                    _obstacles, _movingPlatformColliders);
            if (horizontalCrush is not null)
                throw new CharacterCrushException(horizontalCrush);

            // One authoritative query chooses between static geometry and
            // moving platforms. This motion phase was already split at X/Y
            // reversals and platform turnarounds above, satisfying the
            // monotonic analytical-query contract. The scheduler reports the
            // impact surface velocity for reference-frame collision response.
            IReadOnlyList<UnifiedCollisionHit> simultaneous =
                UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
                    bounds, Evaluate, _worldTimeSeconds + elapsed,
                    segmentDuration, _obstacles, _movingPlatformColliders);
            UnifiedCollisionHit? hit = simultaneous.Count == 0
                ? null : simultaneous[0];
            bool onMoving = hit is { IsMovingPlatform: true };
            if (hit is null)
            {
                MotionSample2D sample = Evaluate(segmentDuration);
                bounds = bounds.Translated(sample.Position);
                velocity = sample.Velocity;
                elapsed += segmentDuration;
                if (verticalReference)
                {
                    flightTime += segmentDuration;
                    if (flightTime >= flight.TotalDurationSeconds)
                    {
                        flightTime = flight.TotalDurationSeconds;
                        verticalReference = false;
                    }
                }
                continue;
            }

            // Milestone 27 intentional assistance: a single *static*
            // ceiling corner can be cleared by a designer-bounded lateral
            // shift along its underside. Genuine multi-contact manifolds,
            // moving ceilings, and downward motion are never assisted.
            // This is not collision precision repair and consumes a
            // separately counted assistance event, not a contact event.
            if (simultaneous.Count == 1 && verticalReference &&
                ceilingCorrectionsUsed < _assistancePolicy.MaximumCeilingCorrectionsPerJump)
            {
                UnifiedCollisionHit ceilingHit = simultaneous[0];
                double assistWorldTime = segmentWorldStart + ceilingHit.TimeSeconds;
                Aabb? corrected = CeilingCornerAssistPlanner.TryPlan(
                    ceilingHit, direction, _assistancePolicy,
                    _obstacles, _movingPlatformColliders, assistWorldTime);
                if (corrected is { } correctedBounds)
                {
                    // One explicit correction at the exact impact. Preserve
                    // the analytical flight's absolute Y law and velocity.
                    double impactDt = ceilingHit.TimeSeconds;
                    double deltaX = correctedBounds.Left - ceilingHit.BodyBoundsAtImpact.Left;
                    bounds = correctedBounds;
                    velocity = Evaluate(impactDt).Velocity;
                    elapsed += impactDt;
                    flightTime += impactDt;
                    assists.Add(new CollisionAssistEvent(ceilingHit.ColliderId,
                        assistWorldTime, deltaX));
                    ceilingCorrectionsUsed++;
                    continue;
                }
            }

            // Count ALL equal-time contacts against the same event budget.
            // A partially consumed manifold is not a valid transactional step.
            if (contacts.Count + simultaneous.Count > _maxContactEvents)
            {
                var attempted = contacts.Concat(simultaneous.Select(c =>
                    new CharacterControlContact(c.ColliderId,
                        elapsed + c.TimeSeconds, c.Normal,
                        c.BodyVelocityAtImpact, c.BodyVelocityAtImpact))).ToArray();
                throw JumpEventLimitExceeded(attempted, bounds, velocity, elapsed);
            }

            if (simultaneous.Count > 1)
            {
                double impactTimeGroup = simultaneous[0].TimeSeconds;
                double worldImpact = _worldTimeSeconds + elapsed + impactTimeGroup;
                Vector2D incomingGroup = Evaluate(impactTimeGroup).Velocity;
                Vector2D accelerationGroup = new(
                    horizontal.Evaluate(impactTimeGroup).Acceleration,
                    verticalReference
                        ? flight.Evaluate(Math.Min(flightTime + impactTimeGroup,
                            flight.TotalDurationSeconds)).VerticalAcceleration
                        : _gravity);

                // A verified multi-contact instant constrains both axes in
                // one operation; alternating ceiling/floor or wall/platform
                // impacts are never allowed to consume time-zero events.
                var resolved = SimultaneousContactResolver.Resolve(
                    incomingGroup, accelerationGroup, simultaneous,
                    CollisionResponseMode.Slide, worldImpact,
                    simultaneous[0].BodyBoundsAtImpact);

                Aabb impactBounds = simultaneous[0].BodyBoundsAtImpact;
                // Canonicalize each VERIFIED face at the same authoritative
                // time. This cannot fix initial penetration or translate an
                // arbitrary overlap, and retains the preexisting 1e-8 face
                // alignment bound used by the analytical jump detector.
                foreach (UnifiedCollisionHit contact in simultaneous)
                    impactBounds = AlignImpactFace(impactBounds, contact, worldImpact);

                bool wasVerticalReference = verticalReference;
                bool wasGrounded = grounded;
                int wasBlockedDirection = blockedDirection;
                elapsed += impactTimeGroup;
                if (verticalReference)
                    flightTime += impactTimeGroup;
                bounds = impactBounds;
                velocity = resolved.Velocity;
                foreach (UnifiedCollisionHit contact in simultaneous)
                    contacts.Add(new CharacterControlContact(
                        contact.ColliderId, elapsed, contact.Normal,
                        incomingGroup, velocity));

                foreach (UnifiedCollisionHit contact in simultaneous)
                {
                    if (contact.Normal.X != 0.0)
                    {
                        int towardWall = contact.Normal.X < 0.0 ? 1 : -1;
                        if (contact.IsMovingPlatform || (int)direction == towardWall)
                        {
                            // Direction of currently requested input wins
                            // when both horizontal sides are constrained.
                            if (blockedDirection == 0 || (int)direction == towardWall)
                                blockedDirection = towardWall;
                        }
                    }
                    else
                    {
                        verticalReference = false;
                        if (contact.Normal.Y > 0.0)
                            grounded = true;
                    }
                }
                if (impactTimeGroup == 0.0 && velocity == incomingGroup &&
                    wasVerticalReference == verticalReference &&
                    wasGrounded == grounded &&
                    wasBlockedDirection == blockedDirection)
                    throw new SimulationNoProgressException(
                        "Simultaneous analytical contacts at t=0 do not " +
                        "advance time or change the jump constraints.");
                continue;
            }

            double impactTime = hit.Value.TimeSeconds;
            elapsed += impactTime;
            if (verticalReference)
                flightTime += impactTime;
            // A verified analytical impact is geometrically face-aligned,
            // but reconstructing a moving-world coordinate can lose a few
            // binary64 bits. Canonicalize ONLY the contacted X face, within
            // the detector's existing alignment budget. A repeat query must
            // see exact wall contact rather than a sub-ULP gap and a fresh
            // inward contact at the same timestamp.
            bounds = AlignHorizontalImpact(
                hit.Value.BodyBoundsAtImpact, hit.Value,
                _worldTimeSeconds + elapsed);
            Vector2D incoming = hit.Value.BodyVelocityAtImpact;
            Vector2D outgoing;
            if (onMoving)
                outgoing = CollisionResponse.Apply(
                    incoming - hit.Value.SurfaceVelocityAtImpact, hit.Value.Normal,
                    CollisionResponseMode.Slide) + hit.Value.SurfaceVelocityAtImpact;
            else
                outgoing = CollisionResponse.Apply(
                    incoming, hit.Value.Normal, CollisionResponseMode.Slide);
            contacts.Add(new CharacterControlContact(
                hit.Value.ColliderId, elapsed, hit.Value.Normal, incoming, outgoing));
            velocity = outgoing;

            if (hit.Value.Normal.X != 0.0)
            {
                // Keep the designer's vertical law. Never fall back to a
                // weaker gravity merely because horizontal motion was blocked.
                int towardWall = hit.Value.Normal.X < 0.0 ? 1 : -1;
                if (onMoving || (int)direction == towardWall)
                    blockedDirection = towardWall;
            }
            else
            {
                // Ceiling: cancel upward motion and start gravity from vy=0.
                // Floor: terminate the jump, with zero inward vertical speed.
                verticalReference = false;
                if (hit.Value.Normal.Y > 0.0)
                    grounded = true;
            }
        }

        // The jump motor is the authoritative owner while airborne. Normalize
        // the same precision-sized moving SIDE contact here, before committing
        // its private bounds, so the controller and the next jump update see
        // exactly the same nonpenetrating geometry. Never change jump height,
        // flight time, or real (greater than budget) penetration.
        bounds = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
            bounds, _worldTimeSeconds + durationSeconds,
            _movingPlatformColliders, _obstacles);

        // Commit only after all calculations and contacts have succeeded.
        _bounds = bounds;
        _velocity = velocity;
        _flightElapsedSeconds = flightTime;
        _referenceVertical = verticalReference;
        _flight = flight;
        _grounded = grounded;
        _blockedDirection = blockedDirection;
        _ceilingCorrectionsUsed = ceilingCorrectionsUsed;
        _worldTimeSeconds += durationSeconds;
        return new SteerableJumpStepResult(bounds, velocity, grounded,
            contacts.ToArray(), assists.ToArray());
    }

    // Only called AFTER a real simultaneous analytical impact has been
    // identified. Preserve orthogonal corrections rather than reconstructing
    // the entire box from just the lowest-ID hit.
    private Aabb AlignImpactFace(Aabb reported, UnifiedCollisionHit hit,
        double worldTime)
    {
        if (hit.Normal.X != 0.0)
            return AlignHorizontalImpact(reported, hit, worldTime);

        Aabb surface = hit.IsMovingPlatform
            ? _movingPlatformColliders.First(p => p.Id == hit.ColliderId)
                .Evaluate(worldTime).Bounds
            : _obstacles.First(c => c.Id == hit.ColliderId).Bounds;
        double face = hit.Normal.Y > 0.0 ? surface.Top : surface.Bottom;
        double reportedFace = hit.Normal.Y > 0.0
            ? reported.Bottom : reported.Top;
        if (Math.Abs(reportedFace - face) > 1e-8)
            throw new SimulationNoProgressException(
                $"Jump simultaneous impact {hit.ColliderId} differs from " +
                $"its authoritative Y face by {Math.Abs(reportedFace - face):R}.");
        double bottom = hit.Normal.Y > 0.0 ? face : face - reported.Height;
        double top = hit.Normal.Y > 0.0 ? face + reported.Height : face;
        return new Aabb(new Vector2D(reported.Left, bottom),
            new Vector2D(reported.Right, top));
    }

    // Use the exact coordinate of the confirmed collider face, never a
    // tolerance-based touch test. The query has already certified the impact;
    // this only repairs the limited roundoff from transforming that result
    // back into the authoritative world frame.
    private Aabb AlignHorizontalImpact(
        Aabb reported, UnifiedCollisionHit hit, double worldTime)
    {
        if (hit.Normal.X == 0.0) return reported;

        Aabb colliderBounds;
        if (hit.IsMovingPlatform)
        {
            MovingPlatformTrajectory platform = _movingPlatformColliders.First(
                p => p.Id == hit.ColliderId);
            colliderBounds = platform.Evaluate(worldTime).Bounds;
        }
        else
        {
            colliderBounds = _obstacles.First(c => c.Id == hit.ColliderId).Bounds;
        }

        double face = hit.Normal.X < 0.0
            ? colliderBounds.Left : colliderBounds.Right;
        double reportedFace = hit.Normal.X < 0.0
            ? reported.Right : reported.Left;
        if (Math.Abs(reportedFace - face) > 1e-8)
            throw new SimulationNoProgressException(
                $"Jump horizontal impact {hit.ColliderId} differs from " +
                $"its authoritative face by {Math.Abs(reportedFace - face):R}.");

        double left = hit.Normal.X < 0.0 ? face - reported.Width : face;
        double right = hit.Normal.X < 0.0 ? face : face + reported.Width;
        return new Aabb(new Vector2D(left, reported.Bottom),
            new Vector2D(right, reported.Top));
    }

    private SimulationEventLimitException JumpEventLimitExceeded(
        IReadOnlyList<CharacterControlContact> contacts,
        Aabb bounds, Vector2D velocity, double elapsed)
    {
        string trace = string.Join("; ", contacts.Select(c =>
            $"id={c.ColliderId},t={c.TimeWithinStepSeconds:R},normal={c.Normal}"));
        return new SimulationEventLimitException(
            $"Jump collision event limit exceeded; state was not committed. " +
            $"WorldTime={_worldTimeSeconds + elapsed:R}, " +
            $"Bounds={bounds}, Velocity={velocity}, Contacts=[{trace}]",
            contacts.Select(c => new SimulationContactTrace(c.ColliderId,
                c.TimeWithinStepSeconds, c.Normal)).ToArray());
    }

    // Analytic-profile inversion by deterministic bisection. This solves an
    // EVENT TIME, not a numerical integration of the trajectory. We only call
    // it on a phase whose endpoint velocities have opposite signs.
    private static double TimeToVerticalVelocity(
        JumpFlightTrajectory flight, double start, double duration, double offset)
    {
        double lo = 0.0, hi = duration;
        double first = flight.Evaluate(start).VerticalVelocity + offset;
        for (int iteration = 0; iteration < 58; ++iteration)
        {
            double middle = lo + (hi - lo) * 0.5;
            double value = flight.Evaluate(start + middle).VerticalVelocity + offset;
            if ((first > 0.0) == (value > 0.0)) lo = middle;
            else hi = middle;
        }
        return hi;
    }

    private bool TouchesBlockingWall(Aabb body, int blockedDirection,
        double worldTimeSeconds)
    {
        if (TouchesMovingWall(body, blockedDirection, worldTimeSeconds))
            return true;
        foreach (StaticAabbCollider obstacle in _obstacles)
        {
            if (!(body.Bottom < obstacle.Bounds.Top && body.Top > obstacle.Bounds.Bottom))
                continue;
            if (blockedDirection > 0 && body.Right == obstacle.Bounds.Left)
                return true;
            if (blockedDirection < 0 && body.Left == obstacle.Bounds.Right)
                return true;
        }
        return false;
    }

    private bool TouchesMovingWall(Aabb body, int blockedDirection,
        double worldTimeSeconds)
    {
        foreach (MovingPlatformTrajectory platform in _movingPlatformColliders)
            if (TouchesMovingWall(body, blockedDirection, worldTimeSeconds, platform))
                return true;
        return false;
    }

    private static bool TouchesMovingWall(Aabb body, int blockedDirection,
        double worldTimeSeconds, MovingPlatformTrajectory candidate)
    {
        Aabb platform = candidate.Evaluate(worldTimeSeconds).Bounds;
        return body.Bottom < platform.Top && body.Top > platform.Bottom &&
            (blockedDirection > 0 ?
                Math.Abs(body.Right - platform.Left) <= 1e-8 :
                Math.Abs(body.Left - platform.Right) <= 1e-8);
    }
}