using System;
using System.IO;
using PlatformerSandbox.Foundation.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation;

public sealed class C02SpriteSheetDefinitionTests
{
    private const string Valid = """
    {
      "version": 1,
      "textureAsset": "Characters/C02_TestCharacter",
      "frameWidth": 32, "frameHeight": 48, "columns": 8, "rows": 3,
      "clips": [
        { "name": "Idle", "mode": "Loop", "frames": [
          { "id": 0, "seconds": 0.25 }, { "id": 9, "seconds": 0.1 }
        ] },
        { "name": "Land", "mode": "Once", "frames": [
          { "id": 14, "seconds": 0.05 }, { "id": 16, "seconds": 0.1 }
        ] }
      ]
    }
    """;

    [Fact]
    public void ParsesClipsAndRowMajorFramesWithoutGraphicsDevice()
    {
        SpriteSheetDefinition sheet = SpriteSheetDefinition.FromJson(Valid);
        Assert.Equal("Characters/C02_TestCharacter", sheet.TextureAsset);
        Assert.Equal(256, sheet.TextureWidth);
        Assert.Equal(144, sheet.TextureHeight);
        Assert.Equal(24, sheet.FrameCount);
        Assert.Equal(new SpriteSheetRegion(0, 0, 32, 48), sheet.RegionForFrame(0));
        Assert.Equal(new SpriteSheetRegion(32, 48, 32, 48), sheet.RegionForFrame(9));
        Assert.Equal(new SpriteSheetRegion(0, 96, 32, 48), sheet.RegionForFrame(16));
        Assert.Equal(SpriteAnimationMode.Once, sheet.GetClip("Land").Mode);
    }

    [Fact]
    public void TimelineSelectsActualAuthoredFrameIdsAndCompletesLand()
    {
        SpriteSheetDefinition sheet = SpriteSheetDefinition.FromJson(Valid);
        var player = new SpriteAnimationPlayer(sheet.GetClip("Idle"));
        Assert.Equal(0, player.CurrentFrame.FrameId);
        player.Advance(.26);
        Assert.Equal(9, player.CurrentFrame.FrameId);
        player.SetClip(sheet.GetClip("Land"));
        Assert.Equal(14, player.CurrentFrame.FrameId);
        player.Advance(.06);
        Assert.Equal(16, player.CurrentFrame.FrameId);
        player.Advance(.12);
        Assert.True(player.IsFinished);
        Assert.Equal(16, player.CurrentFrame.FrameId);
    }

    [Fact]
    public void RejectsDuplicateClipNames()
    {
        string json = Valid.Replace("\"name\": \"Land\"", "\"name\": \"Idle\"");
        Assert.Throws<InvalidDataException>(() => SpriteSheetDefinition.FromJson(json));
    }

    [Fact]
    public void RejectsFrameIndexOutsideAtlas()
    {
        string json = Valid.Replace("\"id\": 16", "\"id\": 24");
        Assert.Throws<InvalidDataException>(() => SpriteSheetDefinition.FromJson(json));
        SpriteSheetDefinition sheet = SpriteSheetDefinition.FromJson(Valid);
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.RegionForFrame(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.RegionForFrame(-1));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.25")]
    [InlineData("1e400")]
    public void RejectsNonPositiveOrUnboundedFrameDurations(string seconds)
    {
        string json = Valid.Replace("\"seconds\": 0.25", "\"seconds\": " + seconds);
        Assert.ThrowsAny<Exception>(() => SpriteSheetDefinition.FromJson(json));
    }

    [Theory]
    [InlineData("../Private/secrets")]
    [InlineData("C:/Other/Atlas")]
    [InlineData("Characters\\\\Test")]
    [InlineData("Characters/Test.png")]
    public void RefusesNonContentTextureAssetPaths(string asset)
    {
        string json = Valid.Replace("Characters/C02_TestCharacter", asset);
        Assert.Throws<InvalidDataException>(() => SpriteSheetDefinition.FromJson(json));
    }

    [Fact]
    public void RejectsUnknownAnimationModesAndMalformedDimensions()
    {
        Assert.Throws<InvalidDataException>(() => SpriteSheetDefinition.FromJson(
            Valid.Replace("\"mode\": \"Loop\"", "\"mode\": \"RepeatForever\"")));
        Assert.Throws<InvalidDataException>(() => SpriteSheetDefinition.FromJson(
            Valid.Replace("\"columns\": 8", "\"columns\": 0")));
        Assert.Throws<InvalidDataException>(() => SpriteSheetDefinition.FromJson(
            Valid.Replace("\"version\": 1", "\"version\": 5")));
    }

    [Fact]
    public void ReturnedClipsAreReadOnlyAndEachDefinitionHasIndependentTimeline()
    {
        SpriteSheetDefinition sheet = SpriteSheetDefinition.FromJson(Valid);
        var a = new SpriteAnimationPlayer(sheet.GetClip("Idle"));
        var b = new SpriteAnimationPlayer(sheet.GetClip("Idle"));
        a.Advance(.3);
        Assert.Equal(9, a.CurrentFrame.FrameId);
        Assert.Equal(0, b.CurrentFrame.FrameId);
        Assert.Throws<NotSupportedException>(() =>
            ((System.Collections.Generic.IDictionary<string, SpriteAnimationClip>)sheet.Clips)
                .Add("Bad", sheet.GetClip("Idle")));
    }
}
