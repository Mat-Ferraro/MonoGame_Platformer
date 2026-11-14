using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Immutable analytical collision-search plan for ONE unobstructed reference
/// jump and its fixed horizontal input schedule. Breakpoints delimit intervals
/// on which both coordinate functions are monotone. A contact interrupts this
/// plan: it is NOT a collision-aware continuation of the reference jump.
/// </summary>
public sealed class JumpReferenceCollisionPlan
{
    private readonly double[] _monotonicBreakpoints;

    private JumpReferenceCollisionPlan(
        JumpTravelTrajectory reference, double[] monotonicBreakpoints)
    {
        Reference = reference;
        _monotonicBreakpoints = monotonicBreakpoints;
    }

    public JumpTravelTrajectory Reference { get; }
    public double TotalDurationSeconds => Reference.TotalDurationSeconds;
    internal IReadOnlyList<double> MonotonicBreakpoints => _monotonicBreakpoints;

    /// <summary>
    /// Reuses the existing analytical solvers; does not approximate motion by
    /// a sequence of linear sweeps or rely on per-frame simulation samples.
    /// The reference begins at zero X displacement. Its Y is relative to
    /// takeoff, so the caller supplies the starting world-space AABB separately.
    /// </summary>
    public static JumpReferenceCollisionPlan Create(
        JumpFlightTrajectory verticalFlight,
        HorizontalMotionProfile horizontalProfile,
        IReadOnlyList<JumpTravelInputChange> inputSchedule,
        double initialHorizontalVelocity = 0.0)
    {
        // JumpTravelSolver performs full schedule and input validation.
        JumpTravelTrajectory reference = JumpTravelSolver.Create(
            verticalFlight, horizontalProfile, inputSchedule,
            initialHorizontalPosition: 0.0,
            initialHorizontalVelocity: initialHorizontalVelocity);

        double duration = reference.TotalDurationSeconds;
        var times = new List<double> { 0.0, duration, verticalFlight.ApexTimeSinceTakeoffSeconds };
        if (verticalFlight.ReleaseTimeSinceTakeoffSeconds is double release)
            times.Add(release);

        // Every input segment is a solution of at most two constant-acceleration
        // phases followed by cruising. Its only possible interior velocity
        // turning point is the zero-speed event during a reversal.
        for (int i = 0; i < inputSchedule.Count; i++)
        {
            JumpTravelInputChange change = inputSchedule[i];
            double begin = change.TimeSinceTakeoffSeconds;
            double end = i + 1 < inputSchedule.Count
                ? inputSchedule[i + 1].TimeSinceTakeoffSeconds
                : duration;
            times.Add(begin);

            JumpTravelSample atBegin = reference.Evaluate(begin);
            HorizontalMotionTrajectory segment = horizontalProfile.CreateTrajectory(
                atBegin.HorizontalPosition, atBegin.HorizontalVelocity, change.Direction);
            AddInterior(times, begin + segment.TimeToCruiseSeconds, begin, end);

            if (change.Direction != HorizontalDirection.Neutral &&
                (int)change.Direction * atBegin.HorizontalVelocity < 0.0)
            {
                double timeToReverse = Math.Abs(atBegin.HorizontalVelocity) /
                    horizontalProfile.ReversalDecelerationRate;
                AddInterior(times, begin + timeToReverse, begin, end);
            }
        }

        times.Sort();
        var unique = new List<double>(times.Count);
        foreach (double time in times)
        {
            if (!double.IsFinite(time) || time < 0.0 || time > duration)
                throw new OverflowException("An analytical phase boundary is not representable.");
            if (unique.Count == 0 || unique[^1] != time)
                unique.Add(time);
        }
        return new JumpReferenceCollisionPlan(reference, unique.ToArray());
    }

    private static void AddInterior(List<double> times, double candidate, double begin, double end)
    {
        // Rates and phase durations are already validated by their own solver;
        // still reject a non-finite derived absolute event time.
        if (!double.IsFinite(candidate))
            throw new OverflowException("Horizontal motion phase time is not finite.");
        if (candidate > begin && candidate < end)
            times.Add(candidate);
    }
}
