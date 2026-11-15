using PlatformerSandbox.Simulation.CharacterMotor;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>Catch only the explicit crush outcome; never arbitrary exceptions.</summary>
public static class MovementGymCrushHandler
{
    public static TResult Execute<TResult>(
        Func<TResult> attempt, Func<CharacterCrushEvent, TResult> respawn,
        MovementGymCrushPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(respawn);
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        try
        {
            return attempt();
        }
        catch (CharacterCrushException crushed) when (policy == MovementGymCrushPolicy.Respawn)
        {
            return respawn(crushed.Crush);
        }
    }
}
