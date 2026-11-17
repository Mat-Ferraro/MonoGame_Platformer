using System;
using PlatformerSandbox.Gameplay.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Presentation;

public sealed class GameDebugHudModeTests
{
    [Fact]
    public void Full_CyclesToCompact()
    {
        Assert.Equal(GameDebugHudMode.Compact,
            GameDebugHudModes.Next(GameDebugHudMode.Full));
    }

    [Fact]
    public void Compact_CyclesToHidden()
    {
        Assert.Equal(GameDebugHudMode.Hidden,
            GameDebugHudModes.Next(GameDebugHudMode.Compact));
    }

    [Fact]
    public void Hidden_CyclesBackToFull()
    {
        Assert.Equal(GameDebugHudMode.Full,
            GameDebugHudModes.Next(GameDebugHudMode.Hidden));
    }

    [Fact]
    public void EveryThreePresses_ReturnsToSameMode()
    {
        foreach (GameDebugHudMode mode in Enum.GetValues<GameDebugHudMode>())
        {
            var afterThree = GameDebugHudModes.Next(
                GameDebugHudModes.Next(GameDebugHudModes.Next(mode)));
            Assert.Equal(mode, afterThree);
        }
    }

    [Fact]
    public void LdtkScenes_DefaultToCompact()
    {
        Assert.Equal(GameDebugHudMode.Compact,
            GameDebugHudModes.DefaultForScene(isLdtkLevel: true));
    }

    [Fact]
    public void RegressionGyms_DefaultToFull()
    {
        Assert.Equal(GameDebugHudMode.Full,
            GameDebugHudModes.DefaultForScene(isLdtkLevel: false));
    }

    [Fact]
    public void InvalidValue_IsRejectedExplicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameDebugHudModes.Next((GameDebugHudMode)999));
    }
}
