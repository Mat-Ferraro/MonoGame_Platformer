using System;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Presentation;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Presentation;

/// <summary>
/// Platformer-specific animation-state adapter. It observes world snapshots,
/// drives reusable timelines, and never sends commands back to the simulation.
/// The original procedural graybox animation remains the art fallback.
/// </summary>
internal sealed class LdtkGrayboxAnimator
{
    private static readonly SpriteAnimationClip FallbackIdle = new("Idle", [
        new(0, .55), new(1, .12), new(0, .35)]);
    private static readonly SpriteAnimationClip FallbackRun = new("Run", [
        new(0, .11), new(1, .11), new(2, .11), new(3, .11)]);
    private static readonly SpriteAnimationClip FallbackRising = new("Rising", [new(0, .1)]);
    private static readonly SpriteAnimationClip FallbackFalling = new("Falling", [new(0, .1)]);
    private static readonly SpriteAnimationClip FallbackDash = new("Dash", [
        new(0, .07), new(1, .07)]);

    private readonly SpriteSheetDefinition? _sheet;
    private readonly Dictionary<int, SpriteAnimationPlayer> _players = new();
    private readonly Dictionary<int, int> _facing = new();
    private readonly Dictionary<int, bool> _wasGrounded = new();
    private readonly HashSet<int> _landing = new();

    public LdtkGrayboxAnimator(SpriteSheetDefinition? sheet = null)
    {
        // A sheet is usable only if it provides all platformer animation
        // states. The atlas parser itself is game-agnostic.
        if (sheet is not null)
        {
            foreach (string name in new[] { "Idle", "Run", "Rising", "Falling", "Dash", "Land" })
                if (!sheet.Clips.ContainsKey(name))
                    throw new ArgumentException("Missing player animation clip: " + name,
                        nameof(sheet));
            if (sheet.GetClip("Land").Mode != SpriteAnimationMode.Once)
                throw new ArgumentException("Land animation must be Once.", nameof(sheet));
        }
        _sheet = sheet;
    }

    public void Advance(double deltaSeconds, IReadOnlyList<WorldPlayerSnapshot> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        for (int i = 0; i < players.Count; i++)
        {
            WorldPlayerSnapshot player = players[i];
            int id = player.PlayerId;
            if(player.IsDead)
            {
                _players.Remove(id);_wasGrounded.Remove(id);_landing.Remove(id);_facing.Remove(id);
                continue;
            }
            bool isGrounded = player.IsGrounded;
            SpriteAnimationClip desired = SelectClip(player);
            if (!_players.TryGetValue(id, out SpriteAnimationPlayer? timeline))
            {
                timeline = new SpriteAnimationPlayer(desired);
                _players.Add(id, timeline);
            }
            else if (_sheet is not null && _wasGrounded.TryGetValue(id, out bool previous) &&
                !previous && isGrounded && Math.Abs(player.Motion.DashVelocityX) < .001)
            {
                _landing.Add(id);
                timeline.SetClip(_sheet.GetClip("Land"), restart: true);
            }

            if (!isGrounded) _landing.Remove(id);
            if (_landing.Contains(id))
            {
                // A transient landing animation runs to completion even if the
                // player immediately returns to the normal idle/run state.
                timeline.Advance(deltaSeconds);
                if (timeline.IsFinished)
                {
                    _landing.Remove(id);
                    timeline.SetClip(desired);
                }
            }
            else
            {
                timeline.SetClip(desired);
                timeline.Advance(deltaSeconds);
            }
            _wasGrounded[id] = isGrounded;
            _facing[id] = player.Motion.FacingDirection is -1 or 1
                ? player.Motion.FacingDirection : 1;
        }
    }

    public LdtkGrayboxPose GetPose(int playerId)
    {
        if (!_players.TryGetValue(playerId, out SpriteAnimationPlayer? player))
            return new LdtkGrayboxPose("Idle", _sheet?.GetClip("Idle").Frames[0].FrameId ?? 0, 1);
        return new LdtkGrayboxPose(player.Clip.Name, player.CurrentFrame.FrameId,
            _facing[playerId]);
    }

    private SpriteAnimationClip SelectClip(WorldPlayerSnapshot player)
    {
        string name = Math.Abs(player.Motion.DashVelocityX) > .001 ? "Dash" :
            player.State.Locomotion switch
            {
                CharacterLocomotionState.GroundedMoving => "Run",
                CharacterLocomotionState.Rising => "Rising",
                CharacterLocomotionState.Falling => "Falling",
                _ => "Idle"
            };
        if (_sheet is not null) return _sheet.GetClip(name);
        return name switch
        {
            "Run" => FallbackRun,
            "Rising" => FallbackRising,
            "Falling" => FallbackFalling,
            "Dash" => FallbackDash,
            _ => FallbackIdle
        };
    }
}

internal readonly record struct LdtkGrayboxPose(string Clip, int Frame, int Facing);
