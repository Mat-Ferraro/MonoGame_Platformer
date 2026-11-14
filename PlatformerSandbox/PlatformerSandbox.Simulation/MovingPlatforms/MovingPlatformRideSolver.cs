using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// Composes locomotion relative to a moving floor with its analytical world
/// displacement. Detects exact lateral support departure while the floor
/// translates and reverses. Pure result: the caller owns state commitment.
/// This is not a complete moving-collider collision solver.
/// </summary>
public static class MovingPlatformRideSolver
{
    /// <summary>
    /// Advance only while a body is supported by the specified moving floor.
    /// X velocity and acceleration are RELATIVE to the platform. Y movement
    /// while supported comes exclusively from the platform. The result's
    /// velocity is WORLD-space (relative velocity + platform velocity).
    /// A support-loss event stops this method immediately without guessing
    /// the following free-fall or collision response.
    /// </summary>
    public static MovingPlatformRideResult Step(
        MovingPlatformTrajectory platform,
        double startTimeSeconds,
        double durationSeconds,
        Aabb initialBody,
        double relativeHorizontalVelocity,
        double relativeHorizontalAcceleration,
        int maxTurns = 64)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (!initialBody.IsValid)
            throw new ArgumentException("Body AABB must be valid.", nameof(initialBody));
        if (!double.IsFinite(startTimeSeconds) || startTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(startTimeSeconds));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(startTimeSeconds + durationSeconds) ||
            (durationSeconds > 0.0 && startTimeSeconds + durationSeconds <= startTimeSeconds))
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(relativeHorizontalVelocity))
            throw new ArgumentOutOfRangeException(nameof(relativeHorizontalVelocity));
        if (!double.IsFinite(relativeHorizontalAcceleration))
            throw new ArgumentOutOfRangeException(nameof(relativeHorizontalAcceleration));
        if (maxTurns <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTurns));

        // Compare against the rounded absolute end as well as the relative
        // duration. Near the end of an update, the relative clock can have a
        // tiny positive residual even though the absolute clock is already
        // exactly at its representable endpoint.
        double absoluteEndTime = startTimeSeconds + durationSeconds;
        MovingPlatformSample start = platform.Evaluate(startTimeSeconds);
        if (!IsSupported(initialBody, start.Bounds))
            throw new InvalidOperationException(
                "The body must start supported by this platform. " +
                "Landing onto a moving platform requires a separate relative collision query.");

        // All horizontal support queries operate in a platform-local frame.
        // The sole authoritative world displacement is added on output.
        double localLeft = initialBody.Left - start.Bounds.Left;
        double relativeVelocity = relativeHorizontalVelocity;
        double advanced = 0.0;
        int turns = 0;
        Aabb localFloor = new(Vector2D.Zero,
            new Vector2D(start.Bounds.Width, start.Bounds.Height));
        StaticAabbCollider[] floor = [new StaticAabbCollider(platform.Id, localFloor)];

        while (advanced < durationSeconds)
        {
            double now = startTimeSeconds + advanced;
            double remaining = durationSeconds - advanced;
            bool atRepresentableEnd = now >= absoluteEndTime;
            // If the absolute clock has reached the rounded endpoint, the
            // final relative-time residual cannot contain another distinct
            // platform event. Consume it analytically instead of throwing.
            double segment = atRepresentableEnd ? remaining :
                Math.Min(remaining, platform.TimeUntilNextTurn(now));
            // A terminal relative-time remainder can be smaller than one ULP
            // of now even when the requested absolute end is one ULP ahead.
            // It is safe to integrate that remainder: no interior platform
            // event precedes it, and the full requested update is finite.
            bool terminalSegment = segment == remaining;
            if (!(segment > 0.0) ||
                (!terminalSegment && now + segment <= now))
                throw new InvalidOperationException("Moving platform event cannot advance time.");

            Aabb localBody = new(
                new Vector2D(localLeft, localFloor.Top),
                new Vector2D(localLeft + initialBody.Width,
                    localFloor.Top + initialBody.Height));
            GroundSupportDeparture? exit = GroundSupportDepartureSolver.FindFirst(
                localBody, relativeVelocity, relativeHorizontalAcceleration,
                segment, floor);
            double dt = exit?.TimeSeconds ?? segment;
            if (!(dt > 0.0) || dt > segment)
                throw new InvalidOperationException("Invalid analytical support-loss time.");

            double travel = relativeVelocity * dt +
                            0.5 * relativeHorizontalAcceleration * dt * dt;
            localLeft = exit?.BodyLeftAtDeparture ?? localLeft + travel;
            relativeVelocity += relativeHorizontalAcceleration * dt;
            // Assign the exact requested duration on a terminal segment.
            // Repeated += can otherwise leave a sub-ULP remainder forever.
            advanced = dt == remaining ? durationSeconds : advanced + dt;

            MovingPlatformSample atEvent = platform.Evaluate(startTimeSeconds + advanced);
            Aabb worldBody = BuildWorldBody(
                atEvent.Bounds, localLeft, initialBody.Width, initialBody.Height);
            Vector2D worldVelocity = atEvent.Velocity +
                                     new Vector2D(relativeVelocity, 0.0);
            if (exit.HasValue)
                return new MovingPlatformRideResult(
                    worldBody, worldVelocity, advanced, false, advanced);

            if (advanced >= durationSeconds)
                return new MovingPlatformRideResult(
                    worldBody, worldVelocity, advanced, true, null);

            // An exact turnaround is an analytical velocity event, not a
            // teleport. The new platform velocity applies right-continuously.
            if (++turns > maxTurns)
                throw new InvalidOperationException("Moving platform turn-event limit exceeded.");
        }

        // A zero-time query is observational and leaves the rider on support.
        return new MovingPlatformRideResult(initialBody,
            start.Velocity + new Vector2D(relativeVelocity, 0.0), 0.0, true, null);
    }

    /// <summary>
    /// Compose the jump's RELATIVE launch velocity with the platform velocity
    /// exactly once. Gameplay/abilities supply the jump's relative vertical
    /// speed; this solver does not launch or mutate the character motor.
    /// </summary>
    public static Vector2D InheritLaunchVelocity(
        Vector2D relativeLaunchVelocity, MovingPlatformSample support) =>
        relativeLaunchVelocity + support.Velocity;

    public static bool IsSupported(Aabb body, Aabb platform) =>
        body.IsValid && platform.IsValid &&
        body.Bottom == platform.Top &&
        body.Right > platform.Left && body.Left < platform.Right;

    private static Aabb BuildWorldBody(
        Aabb floor, double localLeft, double width, double height)
    {
        double left = floor.Left + localLeft;
        double bottom = floor.Top;
        return new Aabb(new Vector2D(left, bottom),
            new Vector2D(left + width, bottom + height));
    }
}
