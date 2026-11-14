using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Finds the first loss of static floor support during one constant-horizontal-
/// acceleration interval. This is a geometric boundary event, not a collision
/// or a gameplay assistance rule. No fixed-distance or per-frame sampling.
///
/// A floor supports a body when its top exactly equals the body's bottom and
/// their horizontal projections have positive overlap. Nearby floors on the
/// same elevation are treated as continuous support if the body can span the
/// intervening gap; a wider gap causes a departure at the first floor's edge.
/// The solver also considers a velocity sign change within the interval.
/// </summary>
public static class GroundSupportDepartureSolver
{
    public static GroundSupportDeparture? FindFirst(
        Aabb body, double horizontalVelocity, double horizontalAcceleration,
        double durationSeconds, IReadOnlyList<StaticAabbCollider> obstacles)
    {
        if (!body.IsValid)
            throw new ArgumentException("Body bounds must be valid.", nameof(body));
        if (!double.IsFinite(horizontalVelocity))
            throw new ArgumentOutOfRangeException(nameof(horizontalVelocity));
        if (!double.IsFinite(horizontalAcceleration))
            throw new ArgumentOutOfRangeException(nameof(horizontalAcceleration));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        ArgumentNullException.ThrowIfNull(obstacles);
        foreach (StaticAabbCollider obstacle in obstacles)
            ArgumentNullException.ThrowIfNull(obstacle);

        if (durationSeconds == 0.0 ||
            !HasSupport(body, obstacles))
            return null;

        // A constant-acceleration segment can turn around once. Examine the
        // outward and return halves separately to preserve the FIRST event.
        if (horizontalVelocity != 0.0 && horizontalAcceleration != 0.0)
        {
            double turn = -horizontalVelocity / horizontalAcceleration;
            if (turn > 0.0 && turn < durationSeconds)
            {
                GroundSupportDeparture? before = FindMonotone(
                    body, horizontalVelocity, horizontalAcceleration, turn, obstacles);
                if (before.HasValue)
                    return before;

                double displacement = Displacement(horizontalVelocity,
                    horizontalAcceleration, turn);
                Aabb turned = body.Translated(new Vector2D(displacement, 0.0));
                if (!HasSupport(turned, obstacles))
                {
                    // Can only happen at a boundary from floating-point
                    // arithmetic, since the first half had no earlier exit.
                    return new GroundSupportDeparture(turn, turned.Left);
                }

                GroundSupportDeparture? after = FindMonotone(
                    turned, 0.0, horizontalAcceleration,
                    durationSeconds - turn, obstacles);
                if (after is { } departure)
                    return departure with { TimeSeconds = turn + departure.TimeSeconds };
                return null;
            }
        }

        return FindMonotone(body, horizontalVelocity, horizontalAcceleration,
            durationSeconds, obstacles);
    }

    private static GroundSupportDeparture? FindMonotone(
        Aabb body, double velocity, double acceleration, double duration,
        IReadOnlyList<StaticAabbCollider> obstacles)
    {
        double displacement = Displacement(velocity, acceleration, duration);
        if (displacement == 0.0)
            return null;

        bool right = displacement > 0.0;
        double? boundary = ConnectedSupportBoundary(body, right, obstacles);
        if (!boundary.HasValue)
            return null;

        double distance = boundary.Value - body.Left;
        // A departure event is needed only if the intended motion actually
        // reaches the last connected support edge, including at the endpoint.
        if (right ? displacement < distance : displacement > distance)
            return null;
        if (right ? distance <= 0.0 : distance >= 0.0)
            return null;

        double time;
        if (acceleration == 0.0)
        {
            if (velocity == 0.0) return null;
            time = distance / velocity;
        }
        else
        {
            double discriminant = velocity * velocity + 2.0 * acceleration * distance;
            if (!double.IsFinite(discriminant))
                throw new OverflowException("Support-boundary root cannot be represented.");
            if (discriminant < 0.0)
                return null;
            double squareRoot = Math.Sqrt(discriminant);
            // Stable small-root expression. Both denominator and distance
            // have the same sign for the chosen monotonic direction.
            double denominator = velocity + (right ? squareRoot : -squareRoot);
            if (denominator == 0.0)
                throw new InvalidOperationException("Degenerate support-boundary root.");
            time = (2.0 * distance) / denominator;
        }

        if (!double.IsFinite(time) || time < 0.0)
            throw new InvalidOperationException("Support-boundary time is not representable.");
        // The reachable-endpoint test above can differ from the quadratic
        // root by a few ulps. Snap only a root that is within floating-point
        // representation error of the requested interval endpoint.
        if (time > duration)
        {
            if (time - duration > 8.0 * 2.220446049250313e-16 * Math.Max(1.0, duration))
                return null;
            time = duration;
        }
        return new GroundSupportDeparture(time, boundary.Value);
    }

    private static double? ConnectedSupportBoundary(
        Aabb body, bool right, IReadOnlyList<StaticAabbCollider> obstacles)
    {
        double edge = body.Left;
        bool supported = false;
        foreach (StaticAabbCollider collider in obstacles)
        {
            Aabb floor = collider.Bounds;
            if (floor.Top != body.Bottom ||
                floor.Right <= body.Left || floor.Left >= body.Right)
                continue;
            supported = true;
            edge = right ? Math.Max(edge, floor.Right) :
                Math.Min(edge, floor.Left - body.Width);
        }
        if (!supported) return null;

        // A finite connected-component closure: each successful iteration
        // strictly expands toward a new platform boundary; no dependency on
        // collider enumeration order or obstacle IDs.
        bool expanded;
        do
        {
            expanded = false;
            foreach (StaticAabbCollider collider in obstacles)
            {
                Aabb floor = collider.Bounds;
                if (floor.Top != body.Bottom) continue;
                if (right && floor.Left - body.Width <= edge && floor.Right > edge)
                {
                    edge = floor.Right;
                    expanded = true;
                }
                else if (!right && floor.Right >= edge &&
                         floor.Left - body.Width < edge)
                {
                    edge = floor.Left - body.Width;
                    expanded = true;
                }
            }
        } while (expanded);
        return edge;
    }

    private static bool HasSupport(
        Aabb body, IReadOnlyList<StaticAabbCollider> obstacles)
    {
        foreach (StaticAabbCollider obstacle in obstacles)
        {
            Aabb floor = obstacle.Bounds;
            if (floor.Top == body.Bottom &&
                body.Right > floor.Left && body.Left < floor.Right)
                return true;
        }
        return false;
    }

    private static double Displacement(double velocity, double acceleration, double time)
    {
        double displacement = velocity * time + 0.5 * acceleration * time * time;
        if (!double.IsFinite(displacement))
            throw new OverflowException("Horizontal displacement is not representable.");
        return displacement;
    }
}
