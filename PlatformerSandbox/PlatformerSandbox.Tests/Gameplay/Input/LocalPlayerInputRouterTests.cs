using System;
using System.Collections.Generic;
using PlatformerSandbox.Gameplay.Input.Local;
using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Input;

public sealed class LocalPlayerInputRouterTests
{
    private static LocalDeviceInput Keys(
        bool left = false, bool right = false, bool jump = false) =>
        new(true, left, right, jump);

    private static LocalDeviceInput Pad(
        bool connected = true, bool left = false, bool right = false,
        bool jump = false, double stick = 0.0) =>
        new(connected, left, right, jump, stick);

    [Theory]
    [InlineData(true, false, HorizontalDirection.Left)]
    [InlineData(false, true, HorizontalDirection.Right)]
    [InlineData(false, false, HorizontalDirection.Neutral)]
    [InlineData(true, true, HorizontalDirection.Neutral)]
    public void KeyboardButtonDirectionsHaveDeterministicConflictPolicy(
        bool left, bool right, HorizontalDirection expected)
    {
        Assert.Equal(expected, LocalInputNormalizer.Direction(
            Keys(left, right)));
    }

    [Theory]
    [InlineData(-0.90, HorizontalDirection.Left)]
    [InlineData(-0.49, HorizontalDirection.Neutral)]
    [InlineData(0.0, HorizontalDirection.Neutral)]
    [InlineData(0.49, HorizontalDirection.Neutral)]
    [InlineData(0.50, HorizontalDirection.Right)]
    [InlineData(0.90, HorizontalDirection.Right)]
    public void GamepadStickUsesConfigurableThreshold(
        double stick, HorizontalDirection expected)
    {
        Assert.Equal(expected, LocalInputNormalizer.Direction(Pad(stick: stick)));
    }

    [Fact]
    public void DpadAndStickOppositionProducesNeutral()
    {
        Assert.Equal(HorizontalDirection.Neutral,
            LocalInputNormalizer.Direction(Pad(left: true, stick: 0.90)));
    }

    [Fact]
    public void PressHoldAndReleaseProduceOneEdgeEach()
    {
        var router = new LocalPlayerInputRouter();
        LocalPlayerInput press = router.Sample(Keys(jump: true), Pad());
        LocalPlayerInput hold = router.Sample(Keys(jump: true), Pad());
        LocalPlayerInput release = router.Sample(Keys(), Pad());
        LocalPlayerInput idle = router.Sample(Keys(), Pad());
        Assert.True(press.JumpPressed);
        Assert.True(press.JumpHeld);
        Assert.False(press.JumpReleased);
        Assert.False(hold.JumpPressed);
        Assert.True(hold.JumpHeld);
        Assert.True(release.JumpReleased);
        Assert.False(release.JumpHeld);
        Assert.False(idle.JumpPressed || idle.JumpReleased);
    }

    [Fact]
    public void OneTickQuickTapProducesExactlyOnePressAndRelease()
    {
        var router = new LocalPlayerInputRouter();
        LocalPlayerInput press = router.Sample(Keys(jump: true), Pad());
        LocalPlayerInput release = router.Sample(Keys(), Pad());
        LocalPlayerInput none = router.Sample(Keys(), Pad());
        Assert.True(press.JumpPressed);
        Assert.True(release.JumpReleased);
        Assert.False(none.JumpPressed || none.JumpReleased);
    }

    [Fact]
    public void ChangingToHeldGamepadDoesNotFabricateJumpPress()
    {
        var router = new LocalPlayerInputRouter();
        router.SelectSource(LocalInputSource.GamepadOne, Pad(jump: true));
        LocalPlayerInput hold = router.Sample(Keys(), Pad(jump: true));
        Assert.False(hold.JumpPressed);
        Assert.True(hold.JumpHeld);
        Assert.Equal(MovementRequestSources.GamepadOne, hold.SourceId);
        Assert.True(router.Sample(Keys(), Pad()).JumpReleased);
        Assert.True(router.Sample(Keys(), Pad(jump: true)).JumpPressed);
    }

    [Fact]
    public void SelectingUnavailableGamepadIsNeutralAndDoesNotStealKeyboardInput()
    {
        var router = new LocalPlayerInputRouter();
        router.SelectSource(LocalInputSource.GamepadOne, Pad(connected: false));
        LocalPlayerInput command = router.Sample(
            Keys(right: true, jump: true), Pad(connected: false));
        Assert.Equal(HorizontalDirection.Neutral, command.Direction);
        Assert.False(command.IsConnected);
        Assert.False(command.JumpPressed);
        Assert.False(command.JumpHeld);
    }

    [Fact]
    public void DisconnectReleasesHeldJumpExactlyOnce()
    {
        var router = new LocalPlayerInputRouter();
        router.SelectSource(LocalInputSource.GamepadOne, Pad());
        Assert.True(router.Sample(Keys(), Pad(jump: true)).JumpPressed);
        LocalPlayerInput lost = router.Sample(Keys(), Pad(connected: false));
        LocalPlayerInput stillLost = router.Sample(Keys(), Pad(connected: false));
        Assert.True(lost.JumpReleased);
        Assert.False(lost.IsConnected);
        Assert.Equal(HorizontalDirection.Neutral, lost.Direction);
        Assert.False(stillLost.JumpReleased || stillLost.JumpPressed);
    }

    [Fact]
    public void ReconnectWhileHoldingJumpRequiresReleaseAndRepress()
    {
        var router = new LocalPlayerInputRouter();
        router.SelectSource(LocalInputSource.GamepadOne, Pad());
        router.Sample(Keys(), Pad(connected: false));
        LocalPlayerInput reconnectHeld = router.Sample(Keys(), Pad(jump: true));
        LocalPlayerInput stillHeld = router.Sample(Keys(), Pad(jump: true));
        Assert.False(reconnectHeld.JumpPressed || reconnectHeld.JumpHeld);
        Assert.False(stillHeld.JumpPressed || stillHeld.JumpHeld);
        router.Sample(Keys(), Pad());
        Assert.True(router.Sample(Keys(), Pad(jump: true)).JumpPressed);
    }

    [Fact]
    public void TwoRoutersHaveIndependentEdges()
    {
        var a = new LocalPlayerInputRouter();
        var b = new LocalPlayerInputRouter();
        Assert.True(a.Sample(Keys(jump: true), Pad()).JumpPressed);
        Assert.True(b.Sample(Keys(jump: true), Pad()).JumpPressed);
        Assert.False(a.Sample(Keys(jump: true), Pad()).JumpPressed);
        Assert.True(b.Sample(Keys(), Pad()).JumpReleased);
        Assert.False(a.Sample(Keys(jump: true), Pad()).JumpReleased);
    }

    [Fact]
    public void InvalidInactiveDeviceDoesNotAffectActiveKeyboard()
    {
        var router = new LocalPlayerInputRouter();
        LocalPlayerInput command = router.Sample(
            Keys(jump: true), Pad(stick: double.NaN));
        Assert.True(command.JumpPressed);
        Assert.Equal(MovementRequestSources.Keyboard, command.SourceId);
    }

    [Fact]
    public void InvalidActiveDeviceStateDoesNotAlterEdgeHistory()
    {
        var router = new LocalPlayerInputRouter();
        router.SelectSource(LocalInputSource.GamepadOne, Pad());
        Assert.Throws<ArgumentOutOfRangeException>(() => router.Sample(
            Keys(), Pad(jump: true, stick: double.NaN)));
        Assert.True(router.Sample(Keys(), Pad(jump: true)).JumpPressed);
    }

    [Fact]
    public void InvalidSourceSelectionRetainsPreviousInputOwnership()
    {
        var router = new LocalPlayerInputRouter();
        Assert.Throws<ArgumentOutOfRangeException>(() => router.SelectSource(
            (LocalInputSource)999, Keys()));
        Assert.Equal(LocalInputSource.Keyboard, router.Source);
    }

    [Fact]
    public void GamepadAndKeyboardDriveEquivalentMovementAndJump()
    {
        var keyboardRouter = new LocalPlayerInputRouter();
        var gamepadRouter = new LocalPlayerInputRouter();
        gamepadRouter.SelectSource(LocalInputSource.GamepadOne, Pad());
        var kbGym = new MovementGymSession();
        var gpGym = new MovementGymSession();
        var requests = new List<MovementRequest>(5);
        for (int i = 0; i < 18; i++)
        {
            bool jumpDown = i >= 3 && i < 8;
            LocalPlayerInput k = keyboardRouter.Sample(Keys(right: true, jump: jumpDown), Pad());
            LocalPlayerInput g = gamepadRouter.Sample(Keys(), Pad(stick: 0.85, jump: jumpDown));
            Assert.Equal(k.Direction, g.Direction);
            Assert.Equal(k.JumpPressed, g.JumpPressed);
            Assert.Equal(k.JumpReleased, g.JumpReleased);

            MovementRequestGymHarness.Populate(requests,
                MovementRequestGymScenario.KeyboardOnly,
                k.Direction, k.JumpPressed, k.JumpReleased,
                primarySourceId: k.SourceId);
            var kr = kbGym.Step(MovementGymSession.FixedStepSeconds, requests);
            MovementRequestGymHarness.Populate(requests,
                MovementRequestGymScenario.KeyboardOnly,
                g.Direction, g.JumpPressed, g.JumpReleased,
                primarySourceId: g.SourceId);
            var gr = gpGym.Step(MovementGymSession.FixedStepSeconds, requests);
            Assert.Equal(kr.Bounds, gr.Bounds);
            Assert.Equal(kr.Velocity, gr.Velocity);
            Assert.Equal(kr.JumpStarted, gr.JumpStarted);
        }
    }

    [Fact]
    public void NormalAdapterProducesCorrectTypedSourceAndJumpEdges()
    {
        var buffer = new List<MovementRequest>();
        var input = new LocalPlayerInput(
            MovementRequestSources.GamepadOne, HorizontalDirection.Right,
            true, false, true, true);
        LocalInputRequestAdapter.Populate(buffer, input);
        Assert.Equal(2, buffer.Count);
        Assert.Equal(MovementRequest.Horizontal(MovementRequestSources.GamepadOne,
            HorizontalDirection.Right), buffer[0]);
        Assert.Equal(MovementRequest.JumpPress(MovementRequestSources.GamepadOne),
            buffer[1]);
    }

    [Fact]
    public void NormalAdapterAndA08KeyboardOnlyScenarioAreEquivalent()
    {
        var normal = new List<MovementRequest>();
        var fixture = new List<MovementRequest>();
        var input = new LocalPlayerInput(MovementRequestSources.Keyboard,
            HorizontalDirection.Left, true, true, false, true);
        LocalInputRequestAdapter.Populate(normal, input);
        MovementRequestGymHarness.Populate(fixture,
            MovementRequestGymScenario.KeyboardOnly,
            input.Direction, input.JumpPressed, input.JumpReleased);
        Assert.Equal(fixture, normal);
    }

    [Fact]
    public void InvalidRequestAdapterInputDoesNotOverwriteExistingBuffer()
    {
        var requests = new List<MovementRequest> {
            MovementRequest.Horizontal(1, HorizontalDirection.Right)
        };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LocalInputRequestAdapter.Populate(requests,
                new LocalPlayerInput(-1, HorizontalDirection.Neutral,
                    false, false, false, true)));
        Assert.Single(requests);
        Assert.Equal(HorizontalDirection.Right, requests[0].Direction);
    }

    [Fact]
    public void ReseedingOnResumeDoesNotCreateJumpPressForHeldButton()
    {
        var router = new LocalPlayerInputRouter();
        router.Sample(Keys(), Pad());
        router.SelectSource(LocalInputSource.Keyboard, Keys(jump: true));
        Assert.False(router.Sample(Keys(jump: true), Pad()).JumpPressed);
        Assert.True(router.Sample(Keys(), Pad()).JumpReleased);
        Assert.True(router.Sample(Keys(jump: true), Pad()).JumpPressed);
    }

    [Fact]
    public void ActiveGamepadCanParticipateInExistingPriorityHarness()
    {
        var requests = new List<MovementRequest>();
        MovementRequestGymHarness.Populate(requests,
            MovementRequestGymScenario.ForceLeft,
            HorizontalDirection.Right, false, false,
            primarySourceId: MovementRequestSources.GamepadOne);
        MovementResolution result = MovementRequestResolver.ResolveDetailed(requests);
        Assert.Equal(HorizontalDirection.Left, result.Intent.Direction);
        Assert.Equal(MovementRequestGymHarness.ForceSourceId, result.WinningSourceId);
    }
}
