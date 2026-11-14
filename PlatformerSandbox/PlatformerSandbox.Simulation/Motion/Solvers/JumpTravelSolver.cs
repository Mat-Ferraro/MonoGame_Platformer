using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.Motion.Solvers;

/// <summary>
/// Composes independent vertical and horizontal reference motion, then
/// optionally solves for the horizontal maximum speed producing a specified
/// displacement under a FIXED, explicit input schedule.
/// </summary>
public static class JumpTravelSolver
{
    /// <summary>
    /// Calculates a complete reference jump for a given horizontal profile.
    /// On each input event, construct a new analytical horizontal trajectory
    /// from the previous segment's exact position and velocity at that event.
    /// </summary>
    public static JumpTravelTrajectory Create(
        JumpFlightTrajectory verticalFlight,
        HorizontalMotionProfile horizontalProfile,
        IReadOnlyList<JumpTravelInputChange> inputSchedule,
        double initialHorizontalPosition = 0.0,
        double initialHorizontalVelocity = 0.0)
    {
        ArgumentNullException.ThrowIfNull(verticalFlight);
        ArgumentNullException.ThrowIfNull(horizontalProfile);
        if (!double.IsFinite(initialHorizontalPosition))
            throw new ArgumentOutOfRangeException(nameof(initialHorizontalPosition));
        if (!double.IsFinite(initialHorizontalVelocity))
            throw new ArgumentOutOfRangeException(nameof(initialHorizontalVelocity));

        JumpTravelInputChange[] schedule = ValidateAndCopySchedule(
            inputSchedule, verticalFlight.TotalDurationSeconds);
        double[] times = new double[schedule.Length];
        HorizontalMotionTrajectory[] segments = new HorizontalMotionTrajectory[schedule.Length];

        double position = initialHorizontalPosition;
        double velocity = initialHorizontalVelocity;

        for (int i = 0; i < schedule.Length; i++)
        {
            if (i > 0)
            {
                // Sample the PREVIOUS trajectory at the exact interrupt time.
                HorizontalMotionSample atEvent = segments[i - 1].Evaluate(
                    schedule[i].TimeSinceTakeoffSeconds - times[i - 1]);
                position = atEvent.Position;
                velocity = atEvent.Velocity;
            }

            times[i] = schedule[i].TimeSinceTakeoffSeconds;
            segments[i] = horizontalProfile.CreateTrajectory(
                position, velocity, schedule[i].Direction);
        }

        return new JumpTravelTrajectory(
            verticalFlight, horizontalProfile, times, segments, initialHorizontalPosition);
    }

    /// <summary>
    /// Inverse designer constraint: derive the maximum horizontal speed that
    /// exactly matches a signed jump distance under this fixed input schedule.
    /// Times and normalized initial speed remain unchanged.
    ///
    /// With all rates proportional to maximum speed and the initial velocity
    /// likewise proportional to maximum speed, displacement scales linearly:
    /// distance(S) = S * distance(1). This is NOT generally true for an
    /// arbitrary fixed world-space starting velocity.
    /// </summary>
    public static JumpTravelTrajectory SolveForDistance(
        JumpFlightTrajectory verticalFlight,
        JumpTravelDistanceConstraints constraints,
        IReadOnlyList<JumpTravelInputChange> inputSchedule,
        double initialHorizontalPosition = 0.0)
    {
        ArgumentNullException.ThrowIfNull(verticalFlight);
        if (!double.IsFinite(constraints.SignedTargetDistance) ||
            constraints.SignedTargetDistance == 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "SignedTargetDistance must be finite and nonzero; zero distance does not uniquely determine speed.");
        }
        if (!double.IsFinite(constraints.InitialVelocityFractionOfMaximumSpeed) ||
            constraints.InitialVelocityFractionOfMaximumSpeed < -1.0 ||
            constraints.InitialVelocityFractionOfMaximumSpeed > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "The initial horizontal velocity fraction must be between -1 and 1.");
        }
        if (!double.IsFinite(initialHorizontalPosition))
            throw new ArgumentOutOfRangeException(nameof(initialHorizontalPosition));

        // A one-unit-per-second reference determines displacement per unit
        // maximum speed, including all input reversals and neutral segments.
        HorizontalMotionConstraints normalizedConstraints = new(
            1.0,
            constraints.TimeToMaximumSpeedSeconds,
            constraints.TimeToStopSeconds,
            constraints.TimeToReverseSeconds);
        HorizontalMotionProfile normalizedProfile = HorizontalMotionSolver.Solve(normalizedConstraints);
        JumpTravelInputChange[] schedule = ValidateAndCopySchedule(
            inputSchedule, verticalFlight.TotalDurationSeconds);
        JumpTravelTrajectory reference = Create(
            verticalFlight, normalizedProfile, schedule,
            0.0, constraints.InitialVelocityFractionOfMaximumSpeed);
        double referenceDistance = reference.SignedTravelDistance;

        if (referenceDistance == 0.0 ||
            Math.Sign(referenceDistance) != Math.Sign(constraints.SignedTargetDistance))
        {
            throw new ArgumentException(
                "The input schedule cannot produce the requested signed displacement with a positive maximum speed.",
                nameof(constraints));
        }

        double solvedSpeed = constraints.SignedTargetDistance / referenceDistance;
        if (!double.IsFinite(solvedSpeed) || solvedSpeed <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "The required maximum horizontal speed is not positive and finite.");
        }

        HorizontalMotionProfile solvedProfile = HorizontalMotionSolver.Solve(
            normalizedConstraints with { MaximumSpeed = solvedSpeed });
        double solvedInitialVelocity =
            constraints.InitialVelocityFractionOfMaximumSpeed * solvedSpeed;
        if (!double.IsFinite(solvedInitialVelocity))
            throw new ArgumentOutOfRangeException(nameof(constraints));

        JumpTravelTrajectory solution = Create(verticalFlight, solvedProfile, schedule,
            initialHorizontalPosition, solvedInitialVelocity);

        // Never claim a designer constraint was satisfied if finite precision
        // (for example, a very large world-space X origin) erased the result.
        double error = Math.Abs(solution.SignedTravelDistance - constraints.SignedTargetDistance);
        if (!double.IsFinite(error) ||
            error > 1e-9 * Math.Abs(constraints.SignedTargetDistance))
        {
            throw new InvalidOperationException(
                "The target jump distance cannot be represented accurately with these inputs.");
        }

        return solution;
    }

    private static JumpTravelInputChange[] ValidateAndCopySchedule(
        IReadOnlyList<JumpTravelInputChange> inputSchedule,
        double totalDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(inputSchedule);
        if (inputSchedule.Count == 0)
            throw new ArgumentException("At least one input change is required at t=0.", nameof(inputSchedule));

        JumpTravelInputChange[] copy = new JumpTravelInputChange[inputSchedule.Count];
        double previous = -1.0;
        for (int i = 0; i < copy.Length; i++)
        {
            JumpTravelInputChange change = inputSchedule[i];
            double time = change.TimeSinceTakeoffSeconds;
            if (!double.IsFinite(time) || time < 0.0 || time >= totalDurationSeconds ||
                (i == 0 && time != 0.0) || (i > 0 && time <= previous))
            {
                throw new ArgumentException(
                    "Input changes must begin at zero, be strictly ordered, and occur before the reference flight ends.",
                    nameof(inputSchedule));
            }
            if (change.Direction is not (HorizontalDirection.Left or
                HorizontalDirection.Neutral or HorizontalDirection.Right))
            {
                throw new ArgumentException("Unknown horizontal direction in input schedule.", nameof(inputSchedule));
            }
            copy[i] = change;
            previous = time;
        }
        return copy;
    }
}
