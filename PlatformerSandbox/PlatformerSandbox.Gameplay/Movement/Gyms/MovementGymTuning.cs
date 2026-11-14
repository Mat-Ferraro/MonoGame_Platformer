using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// Designer-facing parameters for the movement testing gym, not engine-wide
/// defaults. Durations describe outcomes; the simulation solver derives rates.
/// </summary>
public readonly record struct MovementGymTuning(
    HorizontalMotionConstraints Ground,
    HorizontalMotionConstraints Air)
{
    // Exactly the values used by the gym before Milestone 19.
    public static MovementGymTuning Baseline => new(
        new HorizontalMotionConstraints(9.0, 0.18, 0.13, 0.38),
        new HorizontalMotionConstraints(9.0, 0.22, 0.16, 0.43));

    // Examples only; neither is imposed on game content outside this gym.
    public static MovementGymTuning Responsive => new(
        new HorizontalMotionConstraints(9.0, 0.14, 0.09, 0.30),
        new HorizontalMotionConstraints(9.0, 0.16, 0.12, 0.34));

    public static MovementGymTuning Snappy => new(
        new HorizontalMotionConstraints(9.0, 0.10, 0.065, 0.23),
        new HorizontalMotionConstraints(9.0, 0.12, 0.085, 0.27));

    public void Validate()
    {
        _ = HorizontalMotionSolver.Solve(Ground);
        _ = HorizontalMotionSolver.Solve(Air);
    }

    public double Get(MovementGymParameter parameter) => parameter switch
    {
        MovementGymParameter.GroundMaximumSpeed => Ground.MaximumSpeed,
        MovementGymParameter.GroundAccelerationTime => Ground.TimeToMaximumSpeedSeconds,
        MovementGymParameter.GroundStoppingTime => Ground.TimeToStopSeconds,
        MovementGymParameter.GroundReversalTime => Ground.TimeToReverseSeconds,
        MovementGymParameter.AirMaximumSpeed => Air.MaximumSpeed,
        MovementGymParameter.AirAccelerationTime => Air.TimeToMaximumSpeedSeconds,
        MovementGymParameter.AirStoppingTime => Air.TimeToStopSeconds,
        MovementGymParameter.AirReversalTime => Air.TimeToReverseSeconds,
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };

    /// <summary>
    /// Optional gym editor bounds; they aren't engine/physics constraints.
    /// A rejected edit does not alter the existing tuning.
    /// Each step changes a duration by 0.01 seconds or speed by 0.25 units/s.
    /// </summary>
    public bool TryAdjust(MovementGymParameter parameter, int step,
        out MovementGymTuning adjusted)
    {
        if (step is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(step));

        double original = Get(parameter);
        bool isSpeed = parameter is MovementGymParameter.GroundMaximumSpeed
            or MovementGymParameter.AirMaximumSpeed;
        double increment = isSpeed ? 0.25 : 0.01;
        double proposed = Math.Round(original + step * increment, 6);
        double lowerBound = isSpeed ? 1.0 : 0.03;
        double upperBound = isSpeed ? 20.0 : 1.20;
        if (proposed < lowerBound || proposed > upperBound)
        {
            adjusted = this;
            return false;
        }

        MovementGymTuning candidate = parameter switch
        {
            MovementGymParameter.GroundMaximumSpeed => this with
            {
                Ground = Ground with { MaximumSpeed = proposed }
            },
            MovementGymParameter.GroundAccelerationTime => this with
            {
                Ground = Ground with { TimeToMaximumSpeedSeconds = proposed }
            },
            MovementGymParameter.GroundStoppingTime => this with
            {
                Ground = Ground with { TimeToStopSeconds = proposed }
            },
            MovementGymParameter.GroundReversalTime => this with
            {
                Ground = Ground with { TimeToReverseSeconds = proposed }
            },
            MovementGymParameter.AirMaximumSpeed => this with
            {
                Air = Air with { MaximumSpeed = proposed }
            },
            MovementGymParameter.AirAccelerationTime => this with
            {
                Air = Air with { TimeToMaximumSpeedSeconds = proposed }
            },
            MovementGymParameter.AirStoppingTime => this with
            {
                Air = Air with { TimeToStopSeconds = proposed }
            },
            MovementGymParameter.AirReversalTime => this with
            {
                Air = Air with { TimeToReverseSeconds = proposed }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(parameter))
        };

        try
        {
            candidate.Validate();
            adjusted = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            adjusted = this;
            return false;
        }
    }
}

public enum MovementGymParameter
{
    GroundMaximumSpeed,
    GroundAccelerationTime,
    GroundStoppingTime,
    GroundReversalTime,
    AirMaximumSpeed,
    AirAccelerationTime,
    AirStoppingTime,
    AirReversalTime
}
