using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>One immutable fixed-timestep input edge set for deterministic replay.</summary>
public readonly record struct MovementReplayInput(
    HorizontalDirection Direction, bool JumpPressed = false,
    bool JumpReleased = false);

/// <summary>Stable fingerprint of completed authoritative movement steps.</summary>
public sealed record MovementReplayResult(int Steps, ulong StateHash,
    int MovingPlatformRideFrames, int JumpFrames, int CheckpointFallbacks);

/// <summary>
/// Drives exactly the supplied input schedule headlessly. On failure the
/// exception identifies the frame and PRE-step state so the sequence can be
/// reproduced in a debugger without guessing from live keyboard input.
/// </summary>
public static class MovementGymReplay
{
    public static MovementReplayResult Run(
        MovementGymSession gym, IReadOnlyList<MovementReplayInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(gym);
        ArgumentNullException.ThrowIfNull(inputs);
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        ulong hash = fnvOffset;
        int riding = 0;
        int jumps = 0;
        for (int frame = 0; frame < inputs.Count; frame++)
        {
            MovementReplayInput input = inputs[frame];
            double before = gym.ElapsedSeconds;
            var boundsBefore = gym.PlayerBounds;
            var velocityBefore = gym.PlayerVelocity;
            try
            {
                gym.Step(MovementGymSession.FixedStepSeconds,
                    input.Direction, input.JumpPressed, input.JumpReleased);
            }
            catch (Exception error)
            {
                throw new MovementReplayFailureException(
                    frame, before, boundsBefore.Left, boundsBefore.Bottom,
                    velocityBefore.X, velocityBefore.Y, input, error);
            }
            if (gym.IsRidingMovingPlatform) riding++;
            if (gym.IsJumping) jumps++;

            // Hash binary64 bits rather than formatted decimal text. Include
            // state flags and the input to disambiguate equivalent positions.
            void Mix(ulong bits)
            {
                hash ^= bits;
                hash *= fnvPrime;
            }
            Mix(unchecked((ulong)BitConverter.DoubleToInt64Bits(gym.PlayerBounds.Left)));
            Mix(unchecked((ulong)BitConverter.DoubleToInt64Bits(gym.PlayerBounds.Bottom)));
            Mix(unchecked((ulong)BitConverter.DoubleToInt64Bits(gym.PlayerVelocity.X)));
            Mix(unchecked((ulong)BitConverter.DoubleToInt64Bits(gym.PlayerVelocity.Y)));
            Mix(unchecked((ulong)BitConverter.DoubleToInt64Bits(gym.ElapsedSeconds)));
            Mix(gym.IsGrounded ? 1UL : 0UL);
            Mix(gym.IsRidingMovingPlatform ? 1UL : 0UL);
            Mix((ulong)(int)input.Direction + 2UL);
            Mix(input.JumpPressed ? 1UL : 0UL);
            Mix(input.JumpReleased ? 1UL : 0UL);
        }
        return new MovementReplayResult(inputs.Count, hash, riding, jumps,
            gym.CheckpointFallbackCount);
    }
}

public sealed class MovementReplayFailureException : InvalidOperationException
{
    public MovementReplayFailureException(int frame, double time,
        double x, double y, double vx, double vy,
        MovementReplayInput input, Exception inner)
        : base($"Replay failed at frame {frame}, world time {time:R}, " +
            $"position ({x:R},{y:R}), velocity ({vx:R},{vy:R}), " +
            $"direction {input.Direction}, press={input.JumpPressed}, " +
            $"release={input.JumpReleased}: {inner.Message}", inner)
    {
        Frame = frame;
        SimulationTimeSeconds = time;
    }
    public int Frame { get; }
    public double SimulationTimeSeconds { get; }
}
