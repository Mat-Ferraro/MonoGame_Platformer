using System;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation;

public sealed class C01CameraAndAnimationTests
{
    private static readonly Vector2D Still = Vector2D.Zero;

    [Fact]
    public void Camera_SnapAndProjectionMatchLegacyLdtkScreenCoordinates()
    {
        var camera = new Camera2D();
        camera.Snap(new Vector2D(30, 20));
        Camera2DView view = camera.GetView(100, 80, 1280, 720, 32);
        Assert.Equal(10, view.Left, 8);
        Assert.Equal(8.75, view.Bottom, 8);
        Assert.Equal(320, view.CameraLeftPx, 8);
        Assert.Equal(1560, view.CameraTopPx, 8);
        var rectangle = view.WorldRect(new Aabb(new Vector2D(29, 19),
            new Vector2D(31, 21)));
        Assert.Equal(new CameraScreenRect(608, 328, 64, 64), rectangle);
        Vector2D roundTrip = view.ScreenToWorld(640, 360);
        Assert.Equal(30, roundTrip.X, 8);
        Assert.Equal(20, roundTrip.Y, 8);
    }

    [Fact]
    public void Camera_ClampsLargeLevelsAndCentersSmallOnes()
    {
        var nearOrigin = Camera2DView.FromFocus(new Vector2D(-5, -7),
            100, 70, 640, 320, 32);
        Assert.Equal(0, nearOrigin.Left);
        Assert.Equal(0, nearOrigin.Bottom);
        var farCorner = Camera2DView.FromFocus(new Vector2D(110, 100),
            100, 70, 640, 320, 32);
        Assert.Equal(80, farCorner.Left);
        Assert.Equal(60, farCorner.Bottom);

        // Preserve the old behavior for rooms smaller than the viewport.
        var tiny = Camera2DView.FromFocus(new Vector2D(9, 9),
            12, 8, 640, 320, 32);
        Assert.Equal(-4, tiny.Left);
        Assert.Equal(-1, tiny.Bottom);
        Assert.Equal(-128, tiny.CameraLeftPx);
        Assert.Equal(-32, tiny.CameraTopPx);
    }

    [Fact]
    public void Camera_DeadZoneAndExponentialFollowDoNotMutateTarget()
    {
        var settings = new CameraFollowSettings
        {
            DeadZoneHalfWidth = .5, DeadZoneHalfHeight = .5,
            HorizontalLookAhead = 0, FollowSharpness = 8
        };
        var camera = new Camera2D(settings);
        camera.Snap(new Vector2D(10, 10));
        var player = new Vector2D(10.2, 10.3);
        camera.Follow(player, Still, 1.0 / 60);
        Assert.Equal(new Vector2D(10, 10), camera.Focus);
        player = new Vector2D(15, 12);
        camera.Follow(player, Still, 1.0 / 60);
        Assert.InRange(camera.Focus.X, 10.01, 14.49);
        Assert.InRange(camera.Focus.Y, 10.01, 11.49);
        Assert.Equal(new Vector2D(15, 12), player);
    }

    [Fact]
    public void Camera_HitchDurationIsBoundedAndPauseIsStable()
    {
        var camera = new Camera2D(new CameraFollowSettings
        {
            DeadZoneHalfWidth = 0, DeadZoneHalfHeight = 0,
            HorizontalLookAhead = 0, FollowSharpness = 10,
            MaximumFrameSeconds = 0.04
        });
        var other = new Camera2D(new CameraFollowSettings
        {
            DeadZoneHalfWidth = 0, DeadZoneHalfHeight = 0,
            HorizontalLookAhead = 0, FollowSharpness = 10,
            MaximumFrameSeconds = 0.04
        });
        camera.Snap(Still);
        other.Snap(Still);
        camera.Follow(new Vector2D(100, 100), Still, 4);
        other.Follow(new Vector2D(100, 100), Still, .04);
        Assert.Equal(other.Focus.X, camera.Focus.X, 10);
        Assert.Equal(other.Focus.Y, camera.Focus.Y, 10);
        Vector2D beforePause = camera.Focus;
        camera.Follow(new Vector2D(200, 200), Still, 0);
        Assert.Equal(beforePause, camera.Focus);
    }

    [Fact]
    public void Camera_LookAheadChangesDirectionButDoesNotMoveSimulationPlayer()
    {
        var camera = new Camera2D(new CameraFollowSettings
        {
            DeadZoneHalfWidth = 0, DeadZoneHalfHeight = 0,
            HorizontalLookAhead = 3, HorizontalSpeedThreshold = .5,
            FollowSharpness = 10
        });
        Vector2D target = new(10, 12);
        camera.Snap(target);
        camera.Follow(target, new Vector2D(5, 0), .016);
        Assert.True(camera.Focus.X > 10);
        double right = camera.Focus.X;
        camera.Follow(target, new Vector2D(-5, 0), .016);
        Assert.True(camera.Focus.X < right);
        Assert.Equal(new Vector2D(10, 12), target);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-.1)]
    [InlineData(double.PositiveInfinity)]
    public void Camera_RejectsInvalidDelta(double seconds)
    {
        var camera = new Camera2D();
        camera.Snap(Still);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            camera.Follow(new Vector2D(1, 1), Still, seconds));
    }

    private static SpriteAnimationClip Clip(SpriteAnimationMode mode) => new(
        "test", [new(10, .1), new(11, .2), new(12, .3)], mode);

    [Fact]
    public void Animation_LoopsAcrossFrameBoundariesAndLargeTimeJump()
    {
        var clip = Clip(SpriteAnimationMode.Loop);
        var timeline = new SpriteAnimationPlayer(clip);
        Assert.Equal(10, timeline.CurrentFrame.FrameId);
        timeline.Advance(.1);
        Assert.Equal(11, timeline.CurrentFrame.FrameId);
        timeline.Advance(.2);
        Assert.Equal(12, timeline.CurrentFrame.FrameId);
        timeline.Advance(.3);
        Assert.Equal(10, timeline.CurrentFrame.FrameId);
        timeline.Advance(6000.15);
        Assert.Equal(11, timeline.CurrentFrame.FrameId);
        Assert.False(timeline.IsFinished);
    }

    [Fact]
    public void Animation_OnceCompletesAndSetClipCanRestart()
    {
        var clip = Clip(SpriteAnimationMode.Once);
        var timeline = new SpriteAnimationPlayer(clip);
        timeline.Advance(10);
        Assert.True(timeline.IsFinished);
        Assert.Equal(12, timeline.CurrentFrame.FrameId);
        timeline.Advance(12);
        Assert.True(timeline.IsFinished);
        timeline.SetClip(clip);
        Assert.True(timeline.IsFinished); // same reference does not restart
        timeline.SetClip(clip, restart: true);
        Assert.False(timeline.IsFinished);
        Assert.Equal(10, timeline.CurrentFrame.FrameId);
    }

    [Fact]
    public void Animation_PingPongTraversesBackWithoutRepeatingEndpoints()
    {
        var timeline = new SpriteAnimationPlayer(Clip(SpriteAnimationMode.PingPong));
        Assert.Equal(10, timeline.CurrentFrame.FrameId);
        timeline.Advance(.11);
        Assert.Equal(11, timeline.CurrentFrame.FrameId);
        timeline.Advance(.20);
        Assert.Equal(12, timeline.CurrentFrame.FrameId);
        timeline.Advance(.30);
        Assert.Equal(11, timeline.CurrentFrame.FrameId);
        timeline.Advance(.20);
        Assert.Equal(10, timeline.CurrentFrame.FrameId);
    }

    [Fact]
    public void Animation_SwitchesClipsWithoutReplayingSameClipEveryFrame()
    {
        var idle = new SpriteAnimationClip("Idle", [new(0, .1), new(1, .2)]);
        var run = new SpriteAnimationClip("Run", [new(5, .1), new(6, .2)]);
        var player = new SpriteAnimationPlayer(idle);
        player.Advance(.12);
        player.SetClip(idle);
        Assert.Equal(1, player.CurrentFrame.FrameId);
        player.SetClip(run);
        Assert.Equal(5, player.CurrentFrame.FrameId);
        player.Advance(.11);
        Assert.Equal(6, player.CurrentFrame.FrameId);
    }

    [Fact]
    public void Animation_ClipDefensivelyCopiesFramesAndValidatesDurations()
    {
        var frames = new[] { new SpriteAnimationFrame(1, .25) };
        var clip = new SpriteAnimationClip("idle", frames);
        frames[0] = new SpriteAnimationFrame(999, .5);
        Assert.Equal(1, clip.Frames[0].FrameId);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteAnimationClip(
            "bad", [new SpriteAnimationFrame(0, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteAnimationClip(
            "bad", [new SpriteAnimationFrame(-1, 1)]));
        var player = new SpriteAnimationPlayer(clip);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Advance(-1));
    }
}
