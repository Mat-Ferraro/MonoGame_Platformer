using PlatformerSandbox.Gameplay.States;

namespace PlatformerSandbox.Gameplay.Abilities;

/// <summary>
/// Typed, device-independent ability commands. The world validates and sorts them
/// before staging any authoritative simulation work.
/// </summary>
public enum CharacterAbilityId { Pulse = 1, Focus = 2 }
public enum CharacterAbilitySlot { Primary, Utility }
public enum CharacterAbilityRequestKind { Activate, Cancel }
public enum CharacterAbilityRejection
{
    None, Stunned, AlreadyActive, OnCooldown, NotEnoughEnergy,
    ActionBusy, NotActive
}
public enum CharacterAbilityEndReason { Completed, Cancelled, Interrupted }

public readonly record struct CharacterAbilityRequest(
    int PlayerId, CharacterAbilityId AbilityId, CharacterAbilityRequestKind Kind);

public readonly record struct CharacterAbilityStatus(
    CharacterAbilityId Id, CharacterAbilitySlot Slot,
    int ActiveTicksRemaining, int CooldownTicksRemaining)
{
    public bool IsActive => ActiveTicksRemaining > 0;
    public bool IsReady => ActiveTicksRemaining == 0 && CooldownTicksRemaining == 0;
}

public readonly record struct CharacterAbilitySnapshot(
    int Energy, CharacterAbilityStatus Pulse, CharacterAbilityStatus Focus)
{
    public CharacterAbilityStatus Get(CharacterAbilityId id) => id switch
    {
        CharacterAbilityId.Pulse => Pulse,
        CharacterAbilityId.Focus => Focus,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}

/// <summary>
/// Headless per-player ability lifecycle. Pulse (primary) emits a semantic
/// gameplay event and reserves the A09 action lane. WorldSession now maps
/// an accepted Pulse to a collision-swept directional dash for its duration.
/// Focus (utility) doubles energy regeneration while active. This coordinator
/// never directly edits geometry, velocity, or collision resolution.
///
/// Durations and cooldowns are integer world ticks, including activation tick.
/// Energy regenerates at tick end (+1 normally; +2 while Focus is active).
/// Cooldowns begin at activation; cancellation/interruption never refunds cost.
/// </summary>
internal sealed class CharacterAbilityCoordinator
{
    public const int MaximumEnergy = 100;
    public const int PulseCost = 45;
    public const int PulseDurationTicks = 16;
    public const int PulseCooldownTicks = 25;
    public const int FocusCost = 20;
    public const int FocusDurationTicks = 60;
    public const int FocusCooldownTicks = 100;

    private int _energy = MaximumEnergy;
    private int _pulseActive;
    private int _focusActive;
    private int _pulseCooldown;
    private int _focusCooldown;

    public CharacterAbilityCoordinator() { }

    private CharacterAbilityCoordinator(CharacterAbilityCoordinator original)
    {
        _energy = original._energy;
        _pulseActive = original._pulseActive;
        _focusActive = original._focusActive;
        _pulseCooldown = original._pulseCooldown;
        _focusCooldown = original._focusCooldown;
    }

    public CharacterAbilityCoordinator Fork() => new(this);
    public bool HasPrimaryAction => _pulseActive > 0;
    public bool HasAnyActive => _pulseActive > 0 || _focusActive > 0;

    public CharacterAbilitySnapshot Snapshot => new(_energy,
        new CharacterAbilityStatus(CharacterAbilityId.Pulse,
            CharacterAbilitySlot.Primary, _pulseActive, _pulseCooldown),
        new CharacterAbilityStatus(CharacterAbilityId.Focus,
            CharacterAbilitySlot.Utility, _focusActive, _focusCooldown));

    public CharacterAbilityRejection Activate(CharacterAbilityId id,
        CharacterStateMachine states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Snapshot.BlocksPlayerInput)
            return CharacterAbilityRejection.Stunned;
        switch (id)
        {
            case CharacterAbilityId.Pulse:
                if (_pulseActive > 0) return CharacterAbilityRejection.AlreadyActive;
                if (_pulseCooldown > 0) return CharacterAbilityRejection.OnCooldown;
                if (states.Snapshot.Action != CharacterActionState.None)
                    return CharacterAbilityRejection.ActionBusy;
                if (_energy < PulseCost) return CharacterAbilityRejection.NotEnoughEnergy;
                // Guarded by the checks above. All mutations are on forked
                // ability/state objects, committed only with the world step.
                if (states.Apply(new CharacterStateRequest(0,
                        CharacterStateRequestKind.BeginAction)) != CharacterStateRejection.None)
                    throw new InvalidOperationException("Pulse/action guard diverged.");
                _energy -= PulseCost;
                _pulseActive = PulseDurationTicks;
                _pulseCooldown = PulseCooldownTicks;
                return CharacterAbilityRejection.None;
            case CharacterAbilityId.Focus:
                if (_focusActive > 0) return CharacterAbilityRejection.AlreadyActive;
                if (_focusCooldown > 0) return CharacterAbilityRejection.OnCooldown;
                if (_energy < FocusCost) return CharacterAbilityRejection.NotEnoughEnergy;
                _energy -= FocusCost;
                _focusActive = FocusDurationTicks;
                _focusCooldown = FocusCooldownTicks;
                return CharacterAbilityRejection.None;
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    public CharacterAbilityRejection Cancel(CharacterAbilityId id,
        CharacterStateMachine states)
    {
        ArgumentNullException.ThrowIfNull(states);
        switch (id)
        {
            case CharacterAbilityId.Pulse:
                if (_pulseActive <= 0) return CharacterAbilityRejection.NotActive;
                _pulseActive = 0;
                EndPrimaryAction(states);
                return CharacterAbilityRejection.None;
            case CharacterAbilityId.Focus:
                if (_focusActive <= 0) return CharacterAbilityRejection.NotActive;
                _focusActive = 0;
                return CharacterAbilityRejection.None;
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    /// <summary>
    /// Called after A09 requests. State conditions override active abilities;
    /// manually ending the placeholder action also interrupts a primary ability.
    /// </summary>
    public IReadOnlyList<CharacterAbilityId> InterruptForState(CharacterStateMachine states)
    {
        bool stunned = states.Snapshot.BlocksPlayerInput;
        var interrupted = new List<CharacterAbilityId>(2);
        if (_pulseActive > 0 &&
            (stunned || states.Snapshot.Action == CharacterActionState.None))
        {
            _pulseActive = 0;
            if (!stunned) EndPrimaryAction(states);
            interrupted.Add(CharacterAbilityId.Pulse);
        }
        if (_focusActive > 0 && stunned)
        {
            _focusActive = 0;
            interrupted.Add(CharacterAbilityId.Focus);
        }
        return interrupted;
    }

    /// <summary>Advance exactly one successful fixed world tick.</summary>
    public IReadOnlyList<CharacterAbilityId> CompleteTick(CharacterStateMachine states)
    {
        var completed = new List<CharacterAbilityId>(2);
        bool focusBoost = _focusActive > 0;
        if (_pulseActive > 0 && --_pulseActive == 0)
        {
            EndPrimaryAction(states);
            completed.Add(CharacterAbilityId.Pulse);
        }
        if (_focusActive > 0 && --_focusActive == 0)
            completed.Add(CharacterAbilityId.Focus);
        if (_pulseCooldown > 0) _pulseCooldown--;
        if (_focusCooldown > 0) _focusCooldown--;
        _energy = Math.Min(MaximumEnergy, _energy + (focusBoost ? 2 : 1));
        return completed;
    }

    private static void EndPrimaryAction(CharacterStateMachine states)
    {
        if (states.Snapshot.Action == CharacterActionState.Acting &&
            states.Apply(new CharacterStateRequest(0,
                CharacterStateRequestKind.EndAction)) != CharacterStateRejection.None)
            throw new InvalidOperationException("Pulse action could not end.");
    }
}
