using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Conservative analytical classifier for genuinely CLOSING vertical gaps.
/// Only a body already constrained to a lower support can be pinched by an
/// overhead moving platform; side collisions and ordinary landings are not
/// inherently crushing. Checks both start/end horizontal coverage so a body
/// escaping sideways during the phase is not prematurely classified.
///
/// This is not a solver for multi-normal contact or moving-platform obstruction.
/// Those require a full multi-contact manifold in the next M26 phase.
/// </summary>
public static class OpposingSurfaceCrushDetector
{
    private const double AlignmentTolerance = 1e-9;

    /// <summary>Supported rider contacts an immovable ceiling while being lifted.</summary>
    public static CharacterCrushEvent? AtSupportedCeilingContact(
        MovingPlatformTrajectory support,
        CharacterControlContact contact, Aabb bodyAtContact,
        double worldTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(support);
        if (!bodyAtContact.IsValid) throw new ArgumentException("Invalid body.", nameof(bodyAtContact));
        MovingPlatformSample lift = support.Evaluate(worldTimeSeconds);
        if (contact.ColliderId == support.Id || contact.Normal.Y >= 0 ||
            !(lift.Velocity.Y > 0.0))
            return null;
        if (Math.Abs(bodyAtContact.Bottom - lift.Bounds.Top) > AlignmentTolerance ||
            !HorizontalOverlap(bodyAtContact, lift.Bounds))
            return null;
        return new CharacterCrushEvent(support.Id, contact.ColliderId,
            CharacterCrushAxis.Vertical, worldTimeSeconds, bodyAtContact);
    }

    /// <summary>
    /// Grounded player has an immovable floor below and a moving underside
    /// closing from above. The supplied end body contains only HORIZONTAL
    /// locomotion for this phase (no vertical displacement on static support).
    /// </summary>
    public static CharacterCrushEvent? AboveStaticGround(
        Aabb body, Aabb projectedBodyAtEnd, double worldStartSeconds,
        double durationSeconds, IReadOnlyList<StaticAabbCollider> statics,
        IReadOnlyList<MovingPlatformTrajectory> platforms)
    {
        Validate(body, projectedBodyAtEnd, worldStartSeconds, durationSeconds);
        ArgumentNullException.ThrowIfNull(statics);
        ArgumentNullException.ThrowIfNull(platforms);
        double worldEnd = worldStartSeconds + durationSeconds;
        foreach (StaticAabbCollider floor in statics)
        {
            if (body.Bottom != floor.Bounds.Top ||
                !HorizontalOverlap(body, floor.Bounds) ||
                !HorizontalOverlap(projectedBodyAtEnd, floor.Bounds)) continue;
            foreach (MovingPlatformTrajectory upper in platforms)
            {
                MovingPlatformSample start = upper.Evaluate(worldStartSeconds);
                MovingPlatformSample end = upper.Evaluate(worldEnd);
                CharacterCrushEvent? crush = ClosingGap(body, projectedBodyAtEnd,
                    floor.Bounds, floor.Bounds, start.Bounds, end.Bounds,
                    upper.Id, floor.Id, worldEnd);
                if (crush is not null) return crush;
            }
        }
        return null;
    }

    /// <summary>
    /// A second moving platform closes from above a supported rider.
    /// The support's exact vertical displacement is inherited; the projected
    /// horizontal position includes ground-relative velocity/acceleration.
    /// </summary>
    public static CharacterCrushEvent? AboveMovingSupport(
        MovingPlatformTrajectory support, Aabb body,
        double worldStartSeconds, double durationSeconds,
        double relativeHorizontalVelocity, double relativeHorizontalAcceleration,
        IReadOnlyList<MovingPlatformTrajectory> platforms)
    {
        ArgumentNullException.ThrowIfNull(support);
        ArgumentNullException.ThrowIfNull(platforms);
        Validate(body, body, worldStartSeconds, durationSeconds);
        if (!double.IsFinite(relativeHorizontalVelocity) ||
            !double.IsFinite(relativeHorizontalAcceleration))
            throw new ArgumentOutOfRangeException(nameof(relativeHorizontalVelocity));
        double endTime = worldStartSeconds + durationSeconds;
        Aabb lowerStart = support.Evaluate(worldStartSeconds).Bounds;
        Aabb lowerEnd = support.Evaluate(endTime).Bounds;
        if (Math.Abs(body.Bottom - lowerStart.Top) > AlignmentTolerance ||
            !HorizontalOverlap(body, lowerStart)) return null;
        double relativeDx = relativeHorizontalVelocity * durationSeconds +
            0.5 * relativeHorizontalAcceleration * durationSeconds * durationSeconds;
        Aabb projected = body.Translated(new Vector2D(
            lowerEnd.Left - lowerStart.Left + relativeDx,
            lowerEnd.Top - lowerStart.Top));
        foreach (MovingPlatformTrajectory upper in platforms)
        {
            ArgumentNullException.ThrowIfNull(upper);
            if (upper.Id == support.Id) continue;
            CharacterCrushEvent? crush = ClosingGap(body, projected,
                lowerStart, lowerEnd,
                upper.Evaluate(worldStartSeconds).Bounds,
                upper.Evaluate(endTime).Bounds,
                upper.Id, support.Id, endTime);
            if (crush is not null) return crush;
        }
        return null;
    }

    /// <summary>
    /// Detect an actual CLOSING horizontal corridor between a moving face
    /// and an immovable wall, or two approaching moving faces. No wall means
    /// no crush: an ordinary moving-side contact must still be resolved by
    /// the unified collision scheduler.
    /// </summary>
    public static CharacterCrushEvent? AcrossClosingSideSurfaces(
        Aabb body, Aabb projectedBodyAtEnd, double worldStartSeconds,
        double durationSeconds, IReadOnlyList<StaticAabbCollider> statics,
        IReadOnlyList<MovingPlatformTrajectory> platforms)
    {
        Validate(body, projectedBodyAtEnd, worldStartSeconds, durationSeconds);
        ArgumentNullException.ThrowIfNull(statics);
        ArgumentNullException.ThrowIfNull(platforms);
        double endTime = worldStartSeconds + durationSeconds;
        CharacterCrushEvent? earliestById = null;

        foreach (MovingPlatformTrajectory moving in platforms)
        {
            ArgumentNullException.ThrowIfNull(moving);
            Aabb moveStart = moving.Evaluate(worldStartSeconds).Bounds;
            Aabb moveEnd = moving.Evaluate(endTime).Bounds;
            foreach (StaticAabbCollider wall in statics)
            {
                ArgumentNullException.ThrowIfNull(wall);
                CharacterCrushEvent? match = HorizontalClosingGap(body,
                    projectedBodyAtEnd, moveStart, moveEnd,
                    wall.Bounds, wall.Bounds, moving.Id, wall.Id, endTime);
                earliestById = PreferStableId(earliestById, match);
            }
            foreach (MovingPlatformTrajectory other in platforms)
            {
                ArgumentNullException.ThrowIfNull(other);
                if (other.Id == moving.Id) continue;
                CharacterCrushEvent? match = HorizontalClosingGap(body,
                    projectedBodyAtEnd, moveStart, moveEnd,
                    other.Evaluate(worldStartSeconds).Bounds,
                    other.Evaluate(endTime).Bounds, moving.Id, other.Id, endTime);
                earliestById = PreferStableId(earliestById, match);
            }
        }
        return earliestById;
    }

    /// <summary>
    /// Classify a closing side pinch at an actual motion/contact boundary.
    /// A resting opposite face need not produce a new sweep hit, and two
    /// impacts can have distinct binary64 times. Do not wait for the next
    /// strict sweep to discover that there is no legal horizontal velocity.
    /// This is only a contact-boundary query, not initial-overlap recovery.
    /// </summary>
    internal static void ThrowIfClosingSideContact(Aabb body, double time,
        IReadOnlyList<StaticAabbCollider> statics,
        IReadOnlyList<MovingPlatformTrajectory> platforms)
    {
        const double faceTolerance = 1e-8; // Existing sweep face alignment budget.
        CharacterCrushEvent? crush = null;
        foreach (var platform in platforms)
        {
            var sample = platform.Evaluate(time);
            double vx = sample.Velocity.X;
            if (vx == 0 || Math.Min(body.Top, sample.Bounds.Top) -
                Math.Max(body.Bottom, sample.Bounds.Bottom) <= AlignmentTolerance) continue;
            double pushingFace = vx > 0 ? sample.Bounds.Right : sample.Bounds.Left;
            double playerFace = vx > 0 ? body.Left : body.Right;
            if (Math.Abs(pushingFace - playerFace) > faceTolerance) continue;
            foreach (var wall in statics)
            {
                if (Math.Min(body.Top, wall.Bounds.Top) -
                    Math.Max(body.Bottom, wall.Bounds.Bottom) <= AlignmentTolerance) continue;
                double opposingFace = vx > 0 ? wall.Bounds.Left : wall.Bounds.Right;
                double oppositePlayerFace = vx > 0 ? body.Right : body.Left;
                if (Math.Abs(opposingFace - oppositePlayerFace) > faceTolerance) continue;
                double corridor = vx > 0 ? opposingFace - pushingFace : pushingFace - opposingFace;
                // Two nearby faces are not enough: the gap must actually be
                // at the body's width (or a precision-sized residual below it).
                if (Math.Abs(corridor - body.Width) > faceTolerance) continue;
                crush = PreferStableId(crush, new CharacterCrushEvent(platform.Id,
                    wall.Id, CharacterCrushAxis.Horizontal, time, body));
            }
        }
        if (crush is not null) throw new CharacterCrushException(crush);
    }

    /// <summary>
    /// Classify a rejected endpoint only when a moving horizontal face has
    /// actually closed an initially valid corridor below the character width.
    /// Unlike the predictive query, the resolved endpoint may have entered
    /// the opposing surface's vertical span during this interval (landing).
    /// Never treats an isolated overlap, a retreat, or an escaped rider as crush.
    /// The caller must discard this endpoint and retry or apply its crush policy.
    /// </summary>
    public static CharacterCrushEvent? AtPenetratingSideEndpoint(
        Aabb bodyStart, Aabb bodyEnd, double startTime, double endTime,
        IReadOnlyList<StaticAabbCollider> statics,
        IReadOnlyList<MovingPlatformTrajectory> platforms)
    {
        Validate(bodyStart, bodyEnd, startTime, endTime - startTime);
        CharacterCrushEvent? result = null;
        foreach (var pusher in platforms)
        {
            // Solver-predicted props do not own authored playback.
            if (pusher.IsTickMotion) continue;
            Aabb a0 = pusher.Evaluate(startTime).Bounds;
            Aabb a1 = pusher.Evaluate(endTime).Bounds;
            if (!AabbCollisionDetector.Overlaps(bodyEnd, a1) ||
                AabbCollisionDetector.Overlaps(bodyStart, a0)) continue;
            double dx = a1.Left - a0.Left;
            if (dx == 0.0) continue;

            void Consider(int id, Aabb b0, Aabb b1)
            {
                if (id == pusher.Id ||
                    Math.Min(bodyEnd.Top, a1.Top) - Math.Max(bodyEnd.Bottom, a1.Bottom) <= AlignmentTolerance ||
                    Math.Min(bodyEnd.Top, b1.Top) - Math.Max(bodyEnd.Bottom, b1.Bottom) <= AlignmentTolerance)
                    return;
                bool movingRight = dx > 0.0;
                bool initiallyBetween = movingRight
                    ? a0.Right <= bodyStart.Left + AlignmentTolerance && b0.Left >= bodyStart.Right - AlignmentTolerance
                    : b0.Right <= bodyStart.Left + AlignmentTolerance && a0.Left >= bodyStart.Right - AlignmentTolerance;
                // The rejected player must still be on the pushed side and
                // reach into the corridor, rather than have crossed behind it.
                bool stillBetween = movingRight
                    ? bodyEnd.Center.X > a1.Center.X && bodyEnd.Right > a1.Right && bodyEnd.Left < b1.Left
                    : bodyEnd.Center.X < a1.Center.X && bodyEnd.Left < a1.Left && bodyEnd.Right > b1.Right;
                double before = movingRight ? b0.Left - a0.Right : a0.Left - b0.Right;
                double after = movingRight ? b1.Left - a1.Right : a1.Left - b1.Right;
                if (!initiallyBetween || !stillBetween || before < bodyStart.Width - AlignmentTolerance ||
                    after >= before || after > bodyEnd.Width + AlignmentTolerance) return;
                result = PreferStableId(result, new CharacterCrushEvent(pusher.Id,
                    id, CharacterCrushAxis.Horizontal, endTime, bodyStart));
            }

            foreach (var wall in statics) Consider(wall.Id, wall.Bounds, wall.Bounds);
            foreach (var other in platforms)
                if (other.Id != pusher.Id)
                    Consider(other.Id, other.Evaluate(startTime).Bounds, other.Evaluate(endTime).Bounds);
        }
        return result;
    }

    private static CharacterCrushEvent? HorizontalClosingGap(
        Aabb bodyStart, Aabb bodyEnd, Aabb aStart, Aabb aEnd,
        Aabb bStart, Aabb bEnd, int movingId, int opposingId,
        double worldTime)
    {
        if (movingId == opposingId ||
            !VerticalOverlap(bodyStart, aStart) ||
            !VerticalOverlap(bodyStart, bStart) ||
            !VerticalOverlap(bodyEnd, aEnd) ||
            !VerticalOverlap(bodyEnd, bEnd)) return null;

        // a is to the LEFT of the character and b to the RIGHT.
        if (aStart.Right <= bodyStart.Left + AlignmentTolerance &&
            bStart.Left >= bodyStart.Right - AlignmentTolerance)
        {
            double initialGap = bStart.Left - aStart.Right;
            double endGap = bEnd.Left - aEnd.Right;
            if (initialGap >= bodyStart.Width - AlignmentTolerance &&
                endGap < initialGap - AlignmentTolerance &&
                endGap <= bodyStart.Width + AlignmentTolerance)
                return new CharacterCrushEvent(movingId, opposingId,
                    CharacterCrushAxis.Horizontal, worldTime, bodyStart);
        }
        // b is to the LEFT and a to the RIGHT.
        if (bStart.Right <= bodyStart.Left + AlignmentTolerance &&
            aStart.Left >= bodyStart.Right - AlignmentTolerance)
        {
            double initialGap = aStart.Left - bStart.Right;
            double endGap = aEnd.Left - bEnd.Right;
            if (initialGap >= bodyStart.Width - AlignmentTolerance &&
                endGap < initialGap - AlignmentTolerance &&
                endGap <= bodyStart.Width + AlignmentTolerance)
                return new CharacterCrushEvent(movingId, opposingId,
                    CharacterCrushAxis.Horizontal, worldTime, bodyStart);
        }
        return null;
    }

    private static CharacterCrushEvent? PreferStableId(
        CharacterCrushEvent? current, CharacterCrushEvent? next)
    {
        if (next is null) return current;
        if (current is null || next.PushingColliderId < current.PushingColliderId ||
            (next.PushingColliderId == current.PushingColliderId &&
             next.OpposingColliderId < current.OpposingColliderId)) return next;
        return current;
    }

    private static bool VerticalOverlap(Aabb a, Aabb b) =>
        a.Top > b.Bottom && a.Bottom < b.Top;

    private static CharacterCrushEvent? ClosingGap(
        Aabb bodyStart, Aabb bodyEnd, Aabb lowerStart, Aabb lowerEnd,
        Aabb upperStart, Aabb upperEnd, int pusherId, int oppositionId,
        double worldTime)
    {
        // A few ULPs of side overlap at a corner cannot physically support
        // vertical compression. Treat it as a side graze, not a floor/ceiling
        // crush. The sweep validator is still strict about genuine penetration.
        if (pusherId == oppositionId ||
            !HasHorizontalCrushCoverage(bodyStart, lowerStart) ||
            !HasHorizontalCrushCoverage(bodyStart, upperStart) ||
            !HasHorizontalCrushCoverage(bodyEnd, lowerEnd) ||
            !HasHorizontalCrushCoverage(bodyEnd, upperEnd)) return null;

        double gapStart = upperStart.Bottom - lowerStart.Top;
        double gapEnd = upperEnd.Bottom - lowerEnd.Top;
        // A closing gap must begin without positive-area overlap. Merely
        // touching at the end is an impending crush if still closing.
        if (gapStart < bodyStart.Height - AlignmentTolerance ||
            !(gapEnd < gapStart - AlignmentTolerance) ||
            gapEnd > bodyStart.Height + AlignmentTolerance) return null;

        return new CharacterCrushEvent(pusherId, oppositionId,
            CharacterCrushAxis.Vertical, worldTime, bodyStart);
    }

    // Crush requires more than a precision-sized X graze; unlike the
    // ordinary collision detector this is a gameplay contact classification.
    private static bool HasHorizontalCrushCoverage(Aabb a, Aabb b) =>
        Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > AlignmentTolerance;

    private static bool HorizontalOverlap(Aabb a, Aabb b) =>
        a.Right > b.Left && a.Left < b.Right;

    private static void Validate(Aabb start, Aabb end,
        double worldStartSeconds, double durationSeconds)
    {
        if (!start.IsValid || !end.IsValid)
            throw new ArgumentException("Invalid body bounds.");
        if (!double.IsFinite(worldStartSeconds) || worldStartSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(worldStartSeconds));
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 ||
            !double.IsFinite(worldStartSeconds + durationSeconds) ||
            worldStartSeconds + durationSeconds <= worldStartSeconds)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
    }
}