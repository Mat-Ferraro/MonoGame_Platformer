using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.Motion.Solvers;

/// <summary>
/// Solves a supported family of exact, piecewise analytical vertical jump curves.
/// The exponent p >= 2 keeps acceleration finite at the apex.
/// This initial family supports ballistic p=2 and longer apex-region dwell;
/// shorter-than-ballistic dwell requires another profile family.
/// </summary>
public static class JumpConstraintSolver
{
    public static JumpTrajectory Solve(JumpConstraints constraints)
    {
        RequirePositiveFinite(constraints.Height, nameof(constraints.Height));
        RequirePositiveFinite(constraints.RiseTimeSeconds, nameof(constraints.RiseTimeSeconds));
        RequirePositiveFinite(constraints.FallTimeSeconds, nameof(constraints.FallTimeSeconds));

        double fraction = constraints.ApexRegionFraction;
        if (!double.IsFinite(fraction) || fraction <= 0.0 || fraction >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(constraints.ApexRegionFraction),
                "The apex region must be strictly between zero and one.");
        }

        double total = constraints.RiseTimeSeconds + constraints.FallTimeSeconds;
        if (!double.IsFinite(total))
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "The total reference jump duration must be finite.");

        double exponent = 2.0; // Constant-acceleration rise and fall by default.
        if (constraints.TimeNearApexSeconds is double desiredHang)
        {
            if (!double.IsFinite(desiredHang) || desiredHang <= 0.0 || desiredHang >= total)
            {
                throw new ArgumentOutOfRangeException(nameof(constraints.TimeNearApexSeconds),
                    "Time near the apex must be positive and less than the total flight time.");
            }

            // y >= H(1-f) implies a time near the apex of
            // (Tr + Tf) * f^(1/p). Solve this equation for p.
            exponent = Math.Log(fraction) / Math.Log(desiredHang / total);

            // For this family, p >= 2 avoids singular acceleration at the apex.
            // Allow tiny round-off if designer requests precisely the p=2 result.
            if (!double.IsFinite(exponent) || exponent < 2.0 - 1e-12)
            {
                double shortestSupportedHang = total * Math.Sqrt(fraction);
                throw new ArgumentException(
                    $"The requested apex-region time is outside this profile family's " +
                    $"supported range. The minimum is {shortestSupportedHang:R} seconds " +
                    "for these other constraints. Choose another profile family for shorter dwell.",
                    nameof(constraints));
            }

            exponent = Math.Max(2.0, exponent);
        }

        // Reject extreme otherwise-finite inputs whose derived velocity or
        // acceleration would overflow double precision.
        double launchVelocity = constraints.Height * exponent / constraints.RiseTimeSeconds;
        double riseAcceleration = -constraints.Height * exponent * (exponent - 1.0)
            / (constraints.RiseTimeSeconds * constraints.RiseTimeSeconds);
        double landingVelocity = -constraints.Height * exponent / constraints.FallTimeSeconds;
        double fallAcceleration = -constraints.Height * exponent * (exponent - 1.0)
            / (constraints.FallTimeSeconds * constraints.FallTimeSeconds);

        if (!double.IsFinite(launchVelocity) ||
            !double.IsFinite(riseAcceleration) ||
            !double.IsFinite(landingVelocity) ||
            !double.IsFinite(fallAcceleration))
        {
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "The constraints produce motion values outside the finite double range.");
        }

        return new JumpTrajectory(
            constraints.Height,
            constraints.RiseTimeSeconds,
            constraints.FallTimeSeconds,
            fraction,
            exponent);
    }

    private static void RequirePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName,
                "Value must be finite and greater than zero.");
        }
    }
}
