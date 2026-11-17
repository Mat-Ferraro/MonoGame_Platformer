using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.States;

public sealed class CharacterStateWorldTests
{
    private static WorldSession World(long id = 777) =>
        AuthoredWorldGym.Create(worldInstanceId: id);

    private static CharacterStateRequest Request(int id, CharacterStateRequestKind kind,
        int ticks = 0) => new(id, kind, ticks);

    private static WorldPlayerCommand Move(int id, HorizontalDirection direction,
        bool jump = false) => new(id, direction, JumpPressed: jump);

    [Fact]
    public void InitialStateHasIndependentLanesAndNoActiveAction()
    {
        WorldSession world = World();
        CharacterStateSnapshot state = world.GetPlayer(1).State;
        Assert.Equal(CharacterActionState.None, state.Action);
        Assert.Equal(CharacterConditionState.Normal, state.Condition);
        Assert.Equal(0, state.StunTicksRemaining);
        Assert.False(state.BlocksPlayerInput);
        Assert.Empty(world.Snapshot().Events);
    }

    [Fact]
    public void MovingPlatformTransportIsNotMistakenForPlayerWalking()
    {
        WorldSession world = World();
        Assert.Equal(CharacterLocomotionState.GroundedIdle, world.GetPlayer(1).State.Locomotion);
        for (int i = 0; i < 20; i++)
            world.Step([]);
        Assert.Equal(CharacterLocomotionState.GroundedIdle, world.GetPlayer(1).State.Locomotion);
    }

    [Fact]
    public void LocomotionTracksCommittedTakeoffAndDescent()
    {
        WorldSession world = World();
        WorldStepResult takeoff = world.Step([Move(1, HorizontalDirection.Neutral, jump: true)]);
        Assert.Contains(takeoff.Events, e => e.Kind == WorldGameplayEventKind.JumpStarted && e.PlayerId == 1);
        Assert.Equal(CharacterLocomotionState.Rising, world.GetPlayer(1).State.Locomotion);
        bool sawFall = false;
        for (int i = 0; i < 150; i++)
        {
            world.Step([]);
            sawFall |= world.GetPlayer(1).State.Locomotion == CharacterLocomotionState.Falling;
            if (world.GetPlayer(1).IsGrounded && sawFall) break;
        }
        Assert.True(sawFall);
        Assert.True(world.GetPlayer(1).IsGrounded);
    }

    [Fact]
    public void NormalActionStartAndEndAreCommittedOnce()
    {
        WorldSession world = World();
        WorldStepResult start = world.Step([], [Request(1, CharacterStateRequestKind.BeginAction)]);
        Assert.Equal(CharacterActionState.Acting, world.GetPlayer(1).State.Action);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(2).State.Action);
        Assert.Single(start.Events.Where(e => e.Kind == WorldGameplayEventKind.CharacterStateChanged &&
            e.PlayerId == 1 && e.StateChange?.Lane == CharacterStateLane.Action &&
            e.StateChange?.Current == "Acting"));
        Assert.Empty(world.Snapshot().Events);
        Assert.DoesNotContain(world.Step([]).Events,
            e => e.Kind == WorldGameplayEventKind.CharacterStateChanged &&
                e.StateChange?.Lane == CharacterStateLane.Action);
        WorldStepResult end = world.Step([], [Request(1, CharacterStateRequestKind.EndAction)]);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Assert.Single(end.Events.Where(e => e.Kind == WorldGameplayEventKind.CharacterStateChanged &&
            e.PlayerId == 1 && e.StateChange?.Lane == CharacterStateLane.Action &&
            e.StateChange?.Current == "None"));
    }

    [Fact]
    public void ActionLaneDoesNotModifyPhysicsOrSuppressOrdinaryMovement()
    {
        WorldSession control = World();
        WorldSession acting = World();
        for (int i = 0; i < 45; i++)
        {
            WorldPlayerCommand command = Move(1, HorizontalDirection.Right, jump: i == 5);
            control.Step([command]);
            acting.Step([command], i == 0
                ? [Request(1, CharacterStateRequestKind.BeginAction)] : []);
            Assert.Equal(control.GetPlayer(1).Bounds, acting.GetPlayer(1).Bounds);
            Assert.Equal(control.GetPlayer(1).Velocity, acting.GetPlayer(1).Velocity);
        }
        Assert.Equal(CharacterActionState.Acting, acting.GetPlayer(1).State.Action);
    }

    [Fact]
    public void StunInterruptsActionAndEmitsBothTransitionsOnCommit()
    {
        WorldSession world = World();
        world.Step([], [Request(1, CharacterStateRequestKind.BeginAction)]);
        WorldStepResult hit = world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 90)]);
        CharacterStateSnapshot state = world.GetPlayer(1).State;
        Assert.Equal(CharacterActionState.None, state.Action);
        Assert.Equal(CharacterConditionState.Stunned, state.Condition);
        Assert.Equal(89, state.StunTicksRemaining);
        Assert.Contains(hit.Events, e => e.Kind == WorldGameplayEventKind.CharacterStateChanged &&
            e.StateChange?.Lane == CharacterStateLane.Action && e.StateChange?.Current == "None");
        Assert.Contains(hit.Events, e => e.Kind == WorldGameplayEventKind.CharacterStateChanged &&
            e.StateChange?.Lane == CharacterStateLane.Condition && e.StateChange?.Current == "Stunned");
    }

    [Fact]
    public void StunGatesMovementWhileLeavingOtherPlayersUnchanged()
    {
        WorldSession affected = World();
        WorldSession control = World();
        for (int i = 0; i < 20; i++)
        {
            WorldPlayerCommand p1 = Move(1, HorizontalDirection.Right);
            WorldPlayerCommand p2 = Move(2, HorizontalDirection.Left);
            affected.Step([p1, p2], i == 0
                ? [Request(1, CharacterStateRequestKind.ApplyStun, 60)] : []);
            control.Step([Move(1, HorizontalDirection.Neutral), p2]);
            Assert.Equal(control.GetPlayer(1).Bounds, affected.GetPlayer(1).Bounds);
            Assert.Equal(control.GetPlayer(1).Velocity, affected.GetPlayer(1).Velocity);
            Assert.Equal(control.GetPlayer(2).Bounds, affected.GetPlayer(2).Bounds);
            Assert.Equal(control.GetPlayer(2).State, affected.GetPlayer(2).State);
        }
    }

    [Fact]
    public void JumpAttemptDuringStunIsNotQueuedOrPlayedAfterRecovery()
    {
        WorldSession world = World();
        WorldStepResult first = world.Step([Move(1, HorizontalDirection.Neutral, jump: true)],
            [Request(1, CharacterStateRequestKind.ApplyStun, 3)]);
        Assert.False(world.GetPlayer(1).JumpStartedThisTick);
        Assert.DoesNotContain(first.Events, e => e.Kind == WorldGameplayEventKind.JumpStarted && e.PlayerId == 1);
        world.Step([]);
        world.Step([]);
        Assert.Equal(CharacterConditionState.Normal, world.GetPlayer(1).State.Condition);
        Assert.DoesNotContain(world.Step([]).Events,
            e => e.Kind == WorldGameplayEventKind.JumpStarted && e.PlayerId == 1);
        Assert.True(world.Step([Move(1, HorizontalDirection.Neutral, jump: true)]).Events.Any(e =>
            e.Kind == WorldGameplayEventKind.JumpStarted && e.PlayerId == 1));
    }

    [Fact]
    public void TimedStunExpiresAfterExactlyTheSpecifiedNumberOfTicks()
    {
        WorldSession world = World();
        WorldStepResult one = world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 3)]);
        Assert.Equal(2, world.GetPlayer(1).State.StunTicksRemaining);
        world.Step([]);
        Assert.Equal(1, world.GetPlayer(1).State.StunTicksRemaining);
        WorldStepResult three = world.Step([]);
        Assert.Equal(0, world.GetPlayer(1).State.StunTicksRemaining);
        Assert.Equal(CharacterConditionState.Normal, world.GetPlayer(1).State.Condition);
        Assert.Contains(one.Events, e => e.StateChange?.Current == "Stunned");
        Assert.Single(three.Events.Where(e => e.StateChange?.Lane == CharacterStateLane.Condition &&
            e.StateChange?.Current == "Normal"));
    }

    [Fact]
    public void ActionStartWhileStunnedIsDeniedAndReportedWithoutTransition()
    {
        WorldSession world = World();
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 60)]);
        WorldStepResult result = world.Step([],
            [Request(1, CharacterStateRequestKind.BeginAction)]);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Assert.Single(result.Events.Where(e => e.Kind == WorldGameplayEventKind.CharacterStateRequestDenied &&
            e.PlayerId == 1 && e.Rejection == CharacterStateRejection.Stunned &&
            e.StateRequestKind == CharacterStateRequestKind.BeginAction));
    }

    [Fact]
    public void ClearStunAndBeginActionCanOccurInTheSameTick()
    {
        WorldSession world = World();
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 60)]);
        WorldStepResult result = world.Step([], [
            Request(1, CharacterStateRequestKind.BeginAction),
            Request(1, CharacterStateRequestKind.ClearStun)]);
        Assert.Equal(CharacterActionState.Acting, world.GetPlayer(1).State.Action);
        Assert.Equal(CharacterConditionState.Normal, world.GetPlayer(1).State.Condition);
        Assert.DoesNotContain(result.Events, e => e.Kind == WorldGameplayEventKind.CharacterStateRequestDenied);
    }

    [Fact]
    public void RepeatedInvalidActionTransitionReportsReasonButDoesNotFailStep()
    {
        WorldSession world = World();
        WorldStepResult result = world.Step([], [Request(1, CharacterStateRequestKind.EndAction)]);
        Assert.Equal(1, world.WorldTick);
        Assert.Contains(result.Events, e => e.Rejection == CharacterStateRejection.NotActing);
        world.Step([], [Request(1, CharacterStateRequestKind.BeginAction)]);
        result = world.Step([], [Request(1, CharacterStateRequestKind.BeginAction)]);
        Assert.Contains(result.Events, e => e.Rejection == CharacterStateRejection.AlreadyActing);
    }

    [Fact]
    public void StunOverridesSimultaneousActionStartRegardlessOfRequestListOrder()
    {
        WorldSession first = World();
        WorldSession second = World();
        var stun = Request(1, CharacterStateRequestKind.ApplyStun, 10);
        var begin = Request(1, CharacterStateRequestKind.BeginAction);
        WorldStepResult a = first.Step([], [begin, stun]);
        WorldStepResult b = second.Step([], [stun, begin]);
        Assert.Equal(a.Players.ToArray(), b.Players.ToArray());
        Assert.Equal(a.Events.ToArray(), b.Events.ToArray());
        Assert.Equal(CharacterActionState.None, first.GetPlayer(1).State.Action);
        Assert.Contains(a.Events, e => e.Rejection == CharacterStateRejection.Stunned);
    }

    [Fact]
    public void ReversePlayerOrderAndStateRequestOrderCannotChangeResult()
    {
        WorldSession a = World();
        WorldSession b = World();
        for (int i = 0; i < 30; i++)
        {
            WorldPlayerCommand p1 = Move(1, HorizontalDirection.Left);
            WorldPlayerCommand p2 = Move(2, HorizontalDirection.Right);
            CharacterStateRequest[] requests = i == 0
                ? [Request(1, CharacterStateRequestKind.BeginAction),
                   Request(2, CharacterStateRequestKind.ApplyStun, 5)]
                : [];
            WorldStepResult aa = a.Step([p1, p2], requests);
            WorldStepResult bb = b.Step([p2, p1], requests.Reverse().ToArray());
            Assert.Equal(aa.Players.ToArray(), bb.Players.ToArray());
            Assert.Equal(aa.Events.ToArray(), bb.Events.ToArray());
        }
    }

    [Fact]
    public void InvalidStateRequestsAreRejectedWithoutAdvancingWorldOrConsumingEvents()
    {
        WorldSession world = World();
        var baseline = world.Snapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [Request(9, CharacterStateRequestKind.BeginAction)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [Request(1, CharacterStateRequestKind.EndAction, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [Request(1, (CharacterStateRequestKind)900)]));
        Assert.Throws<ArgumentException>(() => world.Step([], [
            Request(1, CharacterStateRequestKind.BeginAction),
            Request(1, CharacterStateRequestKind.BeginAction)]));
        Assert.Throws<ArgumentException>(() => world.Step([], [
            Request(1, CharacterStateRequestKind.BeginAction),
            Request(1, CharacterStateRequestKind.EndAction)]));
        Assert.Throws<ArgumentException>(() => world.Step([], [
            Request(1, CharacterStateRequestKind.ApplyStun, 1),
            Request(1, CharacterStateRequestKind.ClearStun)]));
        Assert.Equal(baseline.WorldTick, world.WorldTick);
        Assert.Equal(baseline.Players.ToArray(), world.Snapshot().Players.ToArray());
        Assert.Equal(0L, world.WorldTick);
        Assert.Equal(2, world.Step([]).Events.Count(e => e.Kind == WorldGameplayEventKind.PlayerJoined));
    }

    [Fact]
    public void RespawnClearsActionAndStunWithoutResettingOtherPlayerOrWorldClock()
    {
        WorldSession world = World();
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 90),
                        Request(2, CharacterStateRequestKind.BeginAction)]);
        long tick = world.WorldTick;
        WorldPlayerSnapshot beforeP2 = world.GetPlayer(2);
        world.RespawnPlayer(1);
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(beforeP2, world.GetPlayer(2));
        Assert.Equal(CharacterConditionState.Normal, world.GetPlayer(1).State.Condition);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(1).State.Action);
        Assert.Equal(CharacterActionState.Acting, world.GetPlayer(2).State.Action);
        Assert.Contains(world.Step([]).Events, e => e.Kind == WorldGameplayEventKind.PlayerRespawned && e.PlayerId == 1);
    }

    [Fact]
    public void PauseMeansNoStateTimeAdvancesWithoutWorldStep()
    {
        WorldSession world = World();
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 10)]);
        long tick = world.WorldTick;
        CharacterStateSnapshot state = world.GetPlayer(1).State;
        for (int i = 0; i < 50; i++)
            Assert.Equal(state, world.Snapshot().Players.Single(p => p.PlayerId == 1).State);
        Assert.Equal(tick, world.WorldTick);
        world.Step([]);
        Assert.Equal(state.StunTicksRemaining - 1, world.GetPlayer(1).State.StunTicksRemaining);
    }

    [Fact]
    public void ThirdPlayerJoinsWithIndependentNormalState()
    {
        WorldSession world = World();
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 60)]);
        AuthoredWorldGym.JoinGuest(world);
        Assert.Equal(CharacterConditionState.Normal, world.GetPlayer(3).State.Condition);
        Assert.Equal(CharacterConditionState.Stunned, world.GetPlayer(1).State.Condition);
        Assert.Equal(CharacterActionState.None, world.GetPlayer(3).State.Action);
        world.Step([]);
        world.RemovePlayer(3);
        AuthoredWorldGym.JoinGuest(world);
        Assert.Equal(CharacterConditionState.Normal, world.GetPlayer(3).State.Condition);
    }

    [Fact]
    public void StunReapplicationsKeepLongestRemainingDuration()
    {
        WorldSession world = World();
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 20)]);
        int before = world.GetPlayer(1).State.StunTicksRemaining;
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 5)]);
        Assert.Equal(before - 1, world.GetPlayer(1).State.StunTicksRemaining);
        world.Step([], [Request(1, CharacterStateRequestKind.ApplyStun, 40)]);
        Assert.Equal(39, world.GetPlayer(1).State.StunTicksRemaining);
    }

    [Fact]
    public void BadMovementCommandCannotPublishAcceptedStateRequest()
    {
        WorldSession world = World();
        WorldPlayerSnapshot before = world.GetPlayer(1);
        long tick = world.WorldTick;
        Assert.Throws<ArgumentException>(() => world.Step(
            [Move(1, HorizontalDirection.Left), Move(1, HorizontalDirection.Right)],
            [Request(1, CharacterStateRequestKind.BeginAction)]));
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(before, world.GetPlayer(1));
        Assert.Empty(world.Snapshot().Events);
    }

    [Fact]
    public void OriginalWorldStepOverloadRemainsBehaviorallyEquivalent()
    {
        WorldSession original = World();
        WorldSession newOverload = World();
        for (int i = 0; i < 100; i++)
        {
            WorldPlayerCommand command = Move(1, i < 50 ? HorizontalDirection.Right : HorizontalDirection.Left,
                jump: i == 12);
            WorldStepResult a = original.Step([command]);
            WorldStepResult b = newOverload.Step([command], []);
            Assert.Equal(a.Players.ToArray(), b.Players.ToArray());
            Assert.Equal(a.Events.ToArray(), b.Events.ToArray());
        }
    }
}
