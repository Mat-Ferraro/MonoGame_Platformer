using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Gameplay.States;

/// <summary>Three independent, small gameplay-state lanes. Locomotion is observational;
/// action is a guarded future-ability hook; condition may gate player commands.</summary>
public enum CharacterLocomotionState { GroundedIdle, GroundedMoving, Rising, Falling }
public enum CharacterActionState { None, Acting }
public enum CharacterConditionState { Normal, Stunned }
public enum CharacterStateLane { Locomotion, Action, Condition }
public enum CharacterStateRequestKind { BeginAction, EndAction, ApplyStun, ClearStun }
public enum CharacterStateRejection { None, Stunned, AlreadyActing, NotActing, NotStunned }

/// <summary>State requests are logical gameplay operations, never raw physical buttons.</summary>
public readonly record struct CharacterStateRequest(
    int PlayerId, CharacterStateRequestKind Kind, int DurationTicks = 0);

public readonly record struct CharacterStateSnapshot(
    CharacterLocomotionState Locomotion,
    CharacterActionState Action,
    CharacterConditionState Condition,
    int StunTicksRemaining)
{
    public bool BlocksPlayerInput => Condition == CharacterConditionState.Stunned;
}

public readonly record struct WorldCharacterStateChange(
    CharacterStateLane Lane, string Previous, string Current);

/// <summary>
/// Explicit, forkable state policy. No movement integration, collision, device
/// polling, effects, ability cooldowns, or hidden global state lives here.
/// Mutations happen only on the WorldSession's staged copy each tick.
/// </summary>
internal sealed class CharacterStateMachine
{
    private const double MotionEpsilon = 1e-5;
    private CharacterLocomotionState _locomotion;
    private CharacterActionState _action;
    private int _stunTicks;

    public CharacterStateMachine(bool grounded, Vector2D velocity)
    {
        _locomotion = Classify(grounded, velocity);
        _action = CharacterActionState.None;
    }

    private CharacterStateMachine(CharacterStateMachine original)
    {
        _locomotion = original._locomotion;
        _action = original._action;
        _stunTicks = original._stunTicks;
    }

    public CharacterStateMachine Fork() => new(this);

    public CharacterStateSnapshot Snapshot => new(
        _locomotion, _action,
        _stunTicks > 0 ? CharacterConditionState.Stunned : CharacterConditionState.Normal,
        _stunTicks);

    public CharacterStateRejection Apply(CharacterStateRequest request)
    {
        switch (request.Kind)
        {
            case CharacterStateRequestKind.BeginAction:
                if (_stunTicks > 0) return CharacterStateRejection.Stunned;
                if (_action != CharacterActionState.None) return CharacterStateRejection.AlreadyActing;
                _action = CharacterActionState.Acting;
                return CharacterStateRejection.None;
            case CharacterStateRequestKind.EndAction:
                if (_action == CharacterActionState.None) return CharacterStateRejection.NotActing;
                _action = CharacterActionState.None;
                return CharacterStateRejection.None;
            case CharacterStateRequestKind.ApplyStun:
                // Longer stun replaces shorter; shorter applications cannot reduce it.
                _stunTicks = Math.Max(_stunTicks, request.DurationTicks);
                _action = CharacterActionState.None; // condition interrupts action
                return CharacterStateRejection.None;
            case CharacterStateRequestKind.ClearStun:
                if (_stunTicks == 0) return CharacterStateRejection.NotStunned;
                _stunTicks = 0;
                return CharacterStateRejection.None;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    /// <summary>Tick at most once after an accepted motor step. An N-tick stun
    /// blocks exactly N authoritative commands, including the application tick.</summary>
    public void CompleteTick(bool grounded, Vector2D velocity)
    {
        _locomotion = Classify(grounded, velocity);
        if (_stunTicks > 0) _stunTicks--;
    }

    private static CharacterLocomotionState Classify(bool grounded, Vector2D velocity)
    {
        if (grounded)
            return Math.Abs(velocity.X) <= MotionEpsilon
                ? CharacterLocomotionState.GroundedIdle
                : CharacterLocomotionState.GroundedMoving;
        return velocity.Y > MotionEpsilon
            ? CharacterLocomotionState.Rising
            : CharacterLocomotionState.Falling;
    }
}
