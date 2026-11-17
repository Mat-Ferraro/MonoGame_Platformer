using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Abilities;

public sealed class CharacterAbilityWorldTests
{
    private static WorldSession World(long id = 500) =>
        AuthoredWorldGym.Create(worldInstanceId: id);

    private static CharacterAbilityRequest Start(int player, CharacterAbilityId id) =>
        new(player, id, CharacterAbilityRequestKind.Activate);

    private static CharacterAbilityRequest Cancel(int player, CharacterAbilityId id) =>
        new(player, id, CharacterAbilityRequestKind.Cancel);

    private static void Advance(WorldSession world, int ticks)
    {
        for (int i = 0; i < ticks; i++) world.Step([]);
    }

    [Fact]
    public void NewPlayersStartWithFullEnergyAndBothAbilitiesReady()
    {
        WorldSession world = World();
        foreach (int playerId in new[] { 1, 2 })
        {
            CharacterAbilitySnapshot a = world.GetPlayerAbilities(playerId);
            Assert.Equal(100, a.Energy);
            Assert.True(a.Pulse.IsReady);
            Assert.True(a.Focus.IsReady);
        }
    }

    [Fact]
    public void PulseStartsAtomicallyConsumesEnergyAndOccupiesActionLane()
    {
        WorldSession world = World();
        WorldStepResult step = world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        CharacterAbilitySnapshot a = world.GetPlayerAbilities(1);
        Assert.Equal(1, world.WorldTick);
        Assert.Equal(56, a.Energy); // 100 - 45, plus one end-of-tick regen
        Assert.Equal(15, a.Pulse.ActiveTicksRemaining);
        Assert.Equal(24, a.Pulse.CooldownTicksRemaining);
        Assert.Equal(CharacterActionState.Acting, world.GetPlayer(1).State.Action);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(2).State.Action);
        Assert.Single(step.Events.Where(e => e.Kind == WorldGameplayEventKind.AbilityActivated &&
            e.PlayerId == 1 && e.AbilityId == CharacterAbilityId.Pulse));
        Assert.DoesNotContain(step.Events, e => e.Kind == WorldGameplayEventKind.AbilityRejected);
    }

    [Fact]
    public void FocusRunsAlongsidePulseOnIndependentUtilitySlot()
    {
        WorldSession world = World();
        WorldStepResult result = world.Step([], [], [
            Start(1, CharacterAbilityId.Focus), Start(1, CharacterAbilityId.Pulse)]);
        CharacterAbilitySnapshot a = world.GetPlayerAbilities(1);
        Assert.True(a.Focus.IsActive);
        Assert.True(a.Pulse.IsActive);
        Assert.Equal(37, a.Energy); // 100 - 45 - 20 + 2 from Focus
        Assert.Equal(CharacterActionState.Acting, world.GetPlayer(1).State.Action);
        Assert.Equal(2, result.Events.Count(e => e.Kind == WorldGameplayEventKind.AbilityActivated));
    }

    [Fact]
    public void FocusDoublesEndOfTickEnergyRegenerationWhileActive()
    {
        WorldSession focus = World();
        WorldSession plain = World();
        focus.Step([], [], [Start(1, CharacterAbilityId.Focus)]);
        plain.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        // Initially Focus costs 20, Pulse costs 45: both have room to regenerate.
        int focusBefore = focus.GetPlayerAbilities(1).Energy;
        int plainBefore = plain.GetPlayerAbilities(1).Energy;
        Advance(focus, 4);
        Advance(plain, 4);
        Assert.Equal(focusBefore + 8, focus.GetPlayerAbilities(1).Energy);
        Assert.Equal(plainBefore + 4, plain.GetPlayerAbilities(1).Energy);
    }

    [Fact]
    public void RepeatedPulseRequestWhileActiveIsRejectedWithNoExtraCost()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        int before = world.GetPlayerAbilities(1).Energy;
        WorldStepResult again = world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        Assert.Equal(before + 1, world.GetPlayerAbilities(1).Energy);
        Assert.Single(again.Events.Where(e => e.Kind == WorldGameplayEventKind.AbilityRejected &&
            e.AbilityRejection == CharacterAbilityRejection.AlreadyActive));
    }

    [Fact]
    public void PulseCompletesAtExactTickAndEndsAction()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        Advance(world, 14);
        Assert.Equal(1, world.GetPlayerAbilities(1).Pulse.ActiveTicksRemaining);
        WorldStepResult last = world.Step([]);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Assert.Single(last.Events.Where(e => e.Kind == WorldGameplayEventKind.AbilityCompleted &&
            e.AbilityId == CharacterAbilityId.Pulse &&
            e.AbilityEndReason == CharacterAbilityEndReason.Completed));
        Assert.Single(last.Events.Where(e => e.Kind == WorldGameplayEventKind.CharacterStateChanged &&
            e.StateChange?.Lane == CharacterStateLane.Action &&
            e.StateChange?.Current == "None"));
    }

    [Fact]
    public void CooldownBeginsOnActivationAndEndsOnExactTick()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        Advance(world, 15);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Equal(9, world.GetPlayerAbilities(1).Pulse.CooldownTicksRemaining);
        WorldStepResult denied = world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        Assert.Contains(denied.Events, e => e.AbilityRejection == CharacterAbilityRejection.OnCooldown);
        Advance(world, 8); // Tick 25; cooldown = 0
        Assert.True(world.GetPlayerAbilities(1).Pulse.IsReady);
        WorldStepResult activated = world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        Assert.Contains(activated.Events, e => e.Kind == WorldGameplayEventKind.AbilityActivated);
    }

    [Fact]
    public void ManualCancellationStopsPulseButDoesNotRefundOrClearCooldown()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        int before = world.GetPlayerAbilities(1).Energy;
        WorldStepResult cancel = world.Step([], [], [Cancel(1, CharacterAbilityId.Pulse)]);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Assert.Equal(before + 1, world.GetPlayerAbilities(1).Energy);
        Assert.True(world.GetPlayerAbilities(1).Pulse.CooldownTicksRemaining > 0);
        Assert.Contains(cancel.Events, e => e.Kind == WorldGameplayEventKind.AbilityCancelled &&
            e.AbilityEndReason == CharacterAbilityEndReason.Cancelled);
        Assert.DoesNotContain(cancel.Events, e => e.Kind == WorldGameplayEventKind.AbilityCompleted);
    }

    [Fact]
    public void CancellingInactiveAbilityIsReportedWithoutMutatingEnergy()
    {
        WorldSession world = World();
        WorldStepResult result = world.Step([], [], [Cancel(1, CharacterAbilityId.Focus)]);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
        Assert.Contains(result.Events, e => e.AbilityId == CharacterAbilityId.Focus &&
            e.AbilityRejection == CharacterAbilityRejection.NotActive);
    }

    [Fact]
    public void StunInterruptsBothAbilitiesAtTheSameCommittedTick()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse), Start(1, CharacterAbilityId.Focus)]);
        WorldStepResult hit = world.Step([], [
            new CharacterStateRequest(1, CharacterStateRequestKind.ApplyStun, 90)], []);
        CharacterAbilitySnapshot a = world.GetPlayerAbilities(1);
        Assert.False(a.Pulse.IsActive);
        Assert.False(a.Focus.IsActive);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Assert.Equal(CharacterConditionState.Stunned, world.GetPlayer(1).State.Condition);
        Assert.Equal(2, hit.Events.Count(e => e.Kind == WorldGameplayEventKind.AbilityInterrupted));
        Assert.Equal(CharacterConditionState.Normal, world.GetPlayer(2).State.Condition);
    }

    [Fact]
    public void StunPreventsActivationAndCannotSpendEnergy()
    {
        WorldSession world = World();
        WorldStepResult result = world.Step([], [
            new CharacterStateRequest(1, CharacterStateRequestKind.ApplyStun, 5)],
            [Start(1, CharacterAbilityId.Pulse)]);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Contains(result.Events, e => e.AbilityRejection == CharacterAbilityRejection.Stunned);
    }

    [Fact]
    public void ClearingStunCanPermitAbilityActivationOnSameTick()
    {
        WorldSession world = World();
        world.Step([], [new CharacterStateRequest(1, CharacterStateRequestKind.ApplyStun, 10)]);
        WorldStepResult result = world.Step([], [
            new CharacterStateRequest(1, CharacterStateRequestKind.ClearStun)],
            [Start(1, CharacterAbilityId.Pulse)]);
        Assert.True(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Contains(result.Events, e => e.Kind == WorldGameplayEventKind.AbilityActivated);
    }

    [Fact]
    public void ExistingManualActionBlocksPrimaryAbilityButNotUtility()
    {
        WorldSession world = World();
        world.Step([], [new CharacterStateRequest(1, CharacterStateRequestKind.BeginAction)]);
        WorldStepResult result = world.Step([], [], [
            Start(1, CharacterAbilityId.Pulse), Start(1, CharacterAbilityId.Focus)]);
        Assert.Contains(result.Events, e => e.AbilityId == CharacterAbilityId.Pulse &&
            e.AbilityRejection == CharacterAbilityRejection.ActionBusy);
        Assert.True(world.GetPlayerAbilities(1).Focus.IsActive);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Equal(CharacterActionState.Acting, world.GetPlayer(1).State.Action);
    }

    [Fact]
    public void ManuallyEndingActionInterruptsPrimaryAbilityAndPreservesCooldown()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        WorldStepResult step = world.Step([], [
            new CharacterStateRequest(1, CharacterStateRequestKind.EndAction)], []);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.True(world.GetPlayerAbilities(1).Pulse.CooldownTicksRemaining > 0);
        Assert.Contains(step.Events, e => e.Kind == WorldGameplayEventKind.AbilityInterrupted &&
            e.AbilityId == CharacterAbilityId.Pulse);
    }

    [Fact]
    public void OtherPlayersCannotSpendOrInterruptEachOthersAbilities()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        CharacterAbilitySnapshot p1 = world.GetPlayerAbilities(1);
        WorldStepResult step = world.Step([], [], [Start(2, CharacterAbilityId.Focus)]);
        Assert.Equal(p1.Energy + 1, world.GetPlayerAbilities(1).Energy);
        Assert.True(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.True(world.GetPlayerAbilities(2).Focus.IsActive);
        Assert.Contains(step.Events, e => e.Kind == WorldGameplayEventKind.AbilityActivated &&
            e.PlayerId == 2 && e.AbilityId == CharacterAbilityId.Focus);
    }

    [Fact]
    public void RespawnResetsOnlyTargetedPlayersAbilitiesAndEnergy()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse),
            Start(2, CharacterAbilityId.Focus)]);
        long tick = world.WorldTick;
        world.RespawnPlayer(1);
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
        Assert.True(world.GetPlayerAbilities(1).Pulse.IsReady);
        Assert.True(world.GetPlayerAbilities(2).Focus.IsActive);
    }

    [Fact]
    public void LateJoiningPlayerHasFreshIndependentAbilityState()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        Advance(world, 50);
        AuthoredWorldGym.JoinGuest(world);
        Assert.Equal(100, world.GetPlayerAbilities(3).Energy);
        Assert.True(world.GetPlayerAbilities(3).Pulse.IsReady);
        Assert.True(world.GetPlayerAbilities(1).Pulse.IsReady);
        world.RemovePlayer(3);
        AuthoredWorldGym.JoinGuest(world);
        Assert.Equal(100, world.GetPlayerAbilities(3).Energy);
    }

    [Fact]
    public void InvalidPlayerAndAbilityIdentifiersRejectBeforeTick()
    {
        WorldSession world = World();
        long before = world.WorldTick;
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [], [
            new CharacterAbilityRequest(999, CharacterAbilityId.Pulse,
                CharacterAbilityRequestKind.Activate)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [], [
            new CharacterAbilityRequest(1, (CharacterAbilityId)55,
                CharacterAbilityRequestKind.Activate)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [], [
            new CharacterAbilityRequest(1, CharacterAbilityId.Pulse,
                (CharacterAbilityRequestKind)55)]));
        Assert.Equal(before, world.WorldTick);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
    }

    [Fact]
    public void DuplicateAndContradictoryRequestsRejectAtomically()
    {
        WorldSession world = World();
        Assert.Throws<ArgumentException>(() => world.Step([], [], [
            Start(1, CharacterAbilityId.Pulse), Start(1, CharacterAbilityId.Pulse)]));
        Assert.Throws<ArgumentException>(() => world.Step([], [], [
            Start(1, CharacterAbilityId.Pulse), Cancel(1, CharacterAbilityId.Pulse)]));
        Assert.Equal(0, world.WorldTick);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
    }

    [Fact]
    public void RequestOrderCannotChangeStateOrCommittedEvents()
    {
        WorldSession first = World();
        WorldSession second = World();
        var a = Start(1, CharacterAbilityId.Pulse);
        var b = Start(1, CharacterAbilityId.Focus);
        var c = Start(2, CharacterAbilityId.Focus);
        WorldStepResult one = first.Step([], [], [a, b, c]);
        WorldStepResult two = second.Step([], [], [c, b, a]);
        Assert.Equal(one.Events.ToArray(), two.Events.ToArray());
        Assert.Equal(first.GetPlayerAbilities(1), second.GetPlayerAbilities(1));
        Assert.Equal(first.GetPlayerAbilities(2), second.GetPlayerAbilities(2));
        Assert.Equal(one.Players.ToArray(), two.Players.ToArray());
    }

    [Fact]
    public void RejectingAbilityDoesNotMutateMotorOrDuplicateEvents()
    {
        WorldSession powered = World();
        WorldSession ordinary = World();
        powered.Step([], [], [Cancel(1, CharacterAbilityId.Pulse)]);
        ordinary.Step([]);
        Assert.Equal(ordinary.GetPlayer(1).Bounds, powered.GetPlayer(1).Bounds);
        Assert.Equal(ordinary.GetPlayer(1).Velocity, powered.GetPlayer(1).Velocity);
        Assert.Empty(powered.Snapshot().Events);
        Assert.DoesNotContain(powered.Step([]).Events, e =>
            e.Kind == WorldGameplayEventKind.AbilityRejected);
    }

    [Fact]
    public void FocusNeverChangesCollisionOrCharacterMotion()
    {
        WorldSession powered = World();
        WorldSession ordinary = World();
        for (int i = 0; i < 70; i++)
        {
            WorldPlayerCommand input = new(1,
                i % 30 < 15 ? HorizontalDirection.Right : HorizontalDirection.Left,
                JumpPressed: i == 5);
            CharacterAbilityRequest[] powers = i == 0
                ? new[] { Start(1, CharacterAbilityId.Focus) }
                : Array.Empty<CharacterAbilityRequest>();
            powered.Step([input], [], powers);
            ordinary.Step([input]);
            Assert.Equal(ordinary.GetPlayer(1).Bounds, powered.GetPlayer(1).Bounds);
            Assert.Equal(ordinary.GetPlayer(1).Velocity, powered.GetPlayer(1).Velocity);
            Assert.Equal(ordinary.GetPlayer(1).IsGrounded, powered.GetPlayer(1).IsGrounded);
        }
    }

    [Fact]
    public void InsufficientEnergyRejectsAndDoesNotGoNegative()
    {
        WorldSession world = World();
        bool rejectedForEnergy = false;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            WorldStepResult attemptResult = world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
            rejectedForEnergy |= attemptResult.Events.Any(e =>
                e.AbilityRejection == CharacterAbilityRejection.NotEnoughEnergy);
            Advance(world, 24); // 25 ticks since activation; cooldown is complete
        }
        Assert.True(rejectedForEnergy);
        Assert.InRange(world.GetPlayerAbilities(1).Energy, 0, 100);
    }

    [Fact]
    public void RejectedCommandBatchDoesNotSpendEnergyOrEmitEvents()
    {
        WorldSession world = World();
        // Invalid operations are rejected before world simulation; even a
        // failed command batch cannot publish ability events or mutate energy.
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([
            new WorldPlayerCommand(999, HorizontalDirection.Neutral)], [],
            [Start(1, CharacterAbilityId.Pulse)]));
        Assert.Equal(0, world.WorldTick);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
        Assert.Empty(world.Snapshot().Events);
    }

    [Fact]
    public void ExistingStepOverloadsKeepAbilityResourcesUntouchedAtCap()
    {
        WorldSession world = World();
        world.Step([]);
        world.Step([], [new CharacterStateRequest(1, CharacterStateRequestKind.BeginAction)]);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
        Assert.True(world.GetPlayerAbilities(1).Pulse.IsReady);
    }

    [Fact]
    public void FocusCompletionAndCooldownAreSeparateFromActionLane()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Focus)]);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Advance(world, 58);
        Assert.Equal(1, world.GetPlayerAbilities(1).Focus.ActiveTicksRemaining);
        WorldStepResult last = world.Step([]);
        Assert.False(world.GetPlayerAbilities(1).Focus.IsActive);
        Assert.Equal(40, world.GetPlayerAbilities(1).Focus.CooldownTicksRemaining);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Assert.Contains(last.Events, e => e.Kind == WorldGameplayEventKind.AbilityCompleted &&
            e.AbilityId == CharacterAbilityId.Focus);
    }

    [Fact]
    public void CancellingFocusStopsBonusRegenFromFollowingTick()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Focus)]);
        int before = world.GetPlayerAbilities(1).Energy;
        WorldStepResult cancellation = world.Step([], [], [Cancel(1, CharacterAbilityId.Focus)]);
        Assert.False(world.GetPlayerAbilities(1).Focus.IsActive);
        Assert.Equal(before + 1, world.GetPlayerAbilities(1).Energy);
        Assert.Contains(cancellation.Events, e => e.Kind == WorldGameplayEventKind.AbilityCancelled &&
            e.AbilityId == CharacterAbilityId.Focus);
        Assert.True(world.GetPlayerAbilities(1).Focus.CooldownTicksRemaining > 0);
    }

    [Fact]
    public void EveryAbilityEventHasStableWorldTickAndSequence()
    {
        WorldSession world = World(id: 9831);
        WorldStepResult tick = world.Step([], [], [Start(1, CharacterAbilityId.Focus),
            Start(1, CharacterAbilityId.Pulse), Start(2, CharacterAbilityId.Pulse)]);
        Assert.NotEmpty(tick.Events);
        Assert.All(tick.Events, e =>
        {
            Assert.Equal(9831, e.WorldInstanceId);
            Assert.Equal(1, e.WorldTick);
        });
        Assert.Equal(Enumerable.Range(0, tick.Events.Count),
            tick.Events.Select(e => e.Sequence));
        Assert.Empty(world.Snapshot().Events);
    }

    [Fact]
    public void WithoutSteppingWorldAbilityTimersDoNotAdvance()
    {
        WorldSession world = World();
        world.Step([], [], [Start(1, CharacterAbilityId.Pulse)]);
        CharacterAbilitySnapshot before = world.GetPlayerAbilities(1);
        long tick = world.WorldTick;
        for (int i = 0; i < 10; i++)
            _ = world.Snapshot();
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(before, world.GetPlayerAbilities(1));
    }

    [Fact]
    public void AbilityRequestsNullAndUnknownPlayersCannotPartiallyCommit()
    {
        WorldSession world = World();
        Assert.Throws<ArgumentNullException>(() => world.Step([], [], null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.GetPlayerAbilities(345));
        Assert.Equal(0, world.WorldTick);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
    }
}
