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
/// Static and moving AABBs / slide response. An optional, one-time jump release
/// replaces the held reference with the analytically continuous cut profile.
/// No wall jump, moving world, or same-update ground locomotion after landing.
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
    private readonly MovingPlatformTrajectory? _movingPlatform;
    private readonly MovingPlatformTrajectory[] _movingPlatformColliders;
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
        MovingPlatformTrajectory? movingPlatform = null, double startTimeSeconds = 0.0)
    {
        if (!takeoffBounds.IsValid)
            throw new ArgumentException("Invalid initial bounds.", nameof(takeoffBounds));
        if (!double.IsFinite(inheritedHorizontalVelocity))
            throw new ArgumentOutOfRangeException(nameof(inheritedHorizontalVelocity));
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
        if (movingPlatform is not null &&
            AabbCollisionDetector.Overlaps(takeoffBounds,
                movingPlatform.Evaluate(startTimeSeconds).Bounds))
            throw new InvalidOperationException("Jump starts penetrating the moving platform.");
        _movingPlatform = movingPlatform;
        _movingPlatformColliders = movingPlatform is null ? [] : [movingPlatform];
        _worldTimeSeconds = startTimeSeconds;
        _airMotion = airMotion;
        _flight = flight;
        _releaseConstraints = releaseConstraints;
        _descentConstraints = descentConstraints;
        _gravity = gravityAcceleration;
        _maxContactEvents = maxContactEvents;
        _bounds = takeoffBounds;
        _velocity = new Vector2D(inheritedHorizontalVelocity, flight.Evaluate(0.0).VerticalVelocity);
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
            flightTime < flight.ApexTimeSinceTakeoffSeconds && velocity.Y > 0.0)
        {
            flight = JumpFlightSolver.ForEarlyRelease(
                flight.HeldJump, cutConfig, flightTime, _descentConstraints);
        }
        bool verticalReference = _referenceVertical;
        bool grounded = _grounded;
        int blockedDirection = _blockedDirection;
        double elapsed = 0.0;
        var contacts = new List<CharacterControlContact>();

        while (elapsed < durationSeconds && !grounded)
        {
            double remaining = durationSeconds - elapsed;
            // The sweep query must never silently accept initial penetration.
            // A *small* residual overlap may be repaired explicitly, within a
            // bounded distance, before querying the next analytical segment.
            // A side repair keeps the reference Y law unchanged.
            if (_movingPlatform is not null)
            {
                MovingPlatformSample snapshot = _movingPlatform.Evaluate(
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
                        throw new InvalidOperationException(
                            "Jump collision event limit exceeded. State was not committed.");
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
            double carrierX = blocked && _movingPlatform is not null &&
                TouchesMovingWall(bounds, blockedDirection, currentWorldTime)
                ? _movingPlatform.Evaluate(currentWorldTime).Velocity.X : 0.0;
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
            if (_movingPlatform is not null)
            {
                double worldNow = _worldTimeSeconds + elapsed;
                floorAtStart = _movingPlatform.Evaluate(worldNow);
                segmentDuration = Math.Min(segmentDuration,
                    _movingPlatform.TimeUntilNextTurn(worldNow));
                double relativeVx = horizontal.Evaluate(0.0).Velocity -
                                    floorAtStart.Value.Velocity.X;
                double relativeAx = horizontal.Evaluate(0.0).Acceleration;
                relativeTurnSeconds = relativeAx == 0.0 ? double.PositiveInfinity :
                    -relativeVx / relativeAx;
                if (relativeTurnSeconds > 0.0 && relativeTurnSeconds < segmentDuration)
                    segmentDuration = relativeTurnSeconds;
            }
            if (!(segmentDuration > 0.0))
                throw new InvalidOperationException("Cannot advance an analytical motion event.");

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
                            flight.Evaluate(flightTime).VerticalVelocity);
                        verticalReference = false;
                        normalizedEvent = true;
                    }
                    else if (flightTime < flight.ApexTimeSinceTakeoffSeconds &&
                        flight.ApexTimeSinceTakeoffSeconds - flightTime <= segmentDuration)
                    {
                        flightTime = flight.ApexTimeSinceTakeoffSeconds;
                        velocity = new Vector2D(velocity.X,
                            flight.Evaluate(flightTime).VerticalVelocity);
                        normalizedEvent = true;
                    }
                    else if (flight.ReleaseTimeSinceTakeoffSeconds is double releaseEvent &&
                        flightTime < releaseEvent &&
                        releaseEvent - flightTime <= segmentDuration)
                    {
                        flightTime = releaseEvent;
                        velocity = new Vector2D(velocity.X,
                            flight.Evaluate(flightTime).VerticalVelocity);
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
                throw new InvalidOperationException(
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
                        new Vector2D(x.Position, y.VerticalDisplacement - startVerticalPosition),
                        new Vector2D(x.Velocity, y.VerticalVelocity));
                }
                MotionSample yFree = ConstantAcceleration.Evaluate(
                    0.0, startVerticalVelocity, _gravity, t);
                return new MotionSample2D(
                    new Vector2D(x.Position, yFree.Position),
                    new Vector2D(x.Velocity, yFree.Velocity));
            }

            // One authoritative query chooses between static geometry and
            // moving platforms. This motion phase was already split at X/Y
            // reversals and platform turnarounds above, satisfying the
            // monotonic analytical-query contract. The scheduler reports the
            // impact surface velocity for reference-frame collision response.
            UnifiedCollisionHit? hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
                bounds, Evaluate, _worldTimeSeconds + elapsed,
                segmentDuration, _obstacles, _movingPlatformColliders);
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

            if (contacts.Count >= _maxContactEvents)
                throw new InvalidOperationException(
                    "Jump collision event limit exceeded. State was not committed.");

            double impactTime = hit.Value.TimeSeconds;
            elapsed += impactTime;
            if (verticalReference)
                flightTime += impactTime;
            bounds = hit.Value.BodyBoundsAtImpact;
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

        // Commit only after all calculations and contacts have succeeded.
        _bounds = bounds;
        _velocity = velocity;
        _flightElapsedSeconds = flightTime;
        _referenceVertical = verticalReference;
        _flight = flight;
        _grounded = grounded;
        _blockedDirection = blockedDirection;
        _worldTimeSeconds += durationSeconds;
        return new SteerableJumpStepResult(bounds, velocity, grounded, contacts.ToArray());
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
        if (_movingPlatform is null) return false;
        Aabb platform = _movingPlatform.Evaluate(worldTimeSeconds).Bounds;
        return body.Bottom < platform.Top && body.Top > platform.Bottom &&
            (blockedDirection > 0 ?
                Math.Abs(body.Right - platform.Left) <= 1e-8 :
                Math.Abs(body.Left - platform.Right) <= 1e-8);
    }
}
