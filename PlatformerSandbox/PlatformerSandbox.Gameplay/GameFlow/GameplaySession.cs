using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Gameplay.GameFlow;

/// <summary>
/// Application flow owns a GameplaySession across level changes/restarts.
/// A world owns live motors, clocks, contacts and ability timers; none of those
/// are stored here. The session retains participant identity and input ownership.
/// </summary>
public readonly record struct GameSessionParticipant(
    int PlayerId, int CharacterId, int? InputSourceId);

public sealed class GameplaySession
{
    private readonly SortedDictionary<int, GameSessionParticipant> _members = new();

    public GameplaySession(long sessionId, IEnumerable<GameSessionParticipant> participants)
    {
        if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
        SessionId = sessionId;
        UpdateParticipants(participants);
    }

    public long SessionId { get; }
    public long LevelEntryCount { get; private set; }
    public SceneDestination? CurrentLevel { get; private set; }
    public IReadOnlyList<GameSessionParticipant> Participants =>
        Array.AsReadOnly(_members.Values.ToArray());

    public void UpdateParticipants(IEnumerable<GameSessionParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var next = new SortedDictionary<int, GameSessionParticipant>();
        var characters = new HashSet<int>();
        var devices = new HashSet<int>();
        foreach (GameSessionParticipant member in participants)
        {
            if (member.PlayerId <= 0 || member.CharacterId <= 0 ||
                !next.TryAdd(member.PlayerId, member) ||
                !characters.Add(member.CharacterId) ||
                member.InputSourceId is int source && (source <= 0 || !devices.Add(source)))
                throw new ArgumentException("Invalid or duplicate player, character, or source ID.",
                    nameof(participants));
        }
        if (next.Count == 0)
            throw new ArgumentException("A gameplay session requires a participant.", nameof(participants));
        _members.Clear();
        foreach (var item in next) _members.Add(item.Key, item.Value);
    }

    public void EnterLevel(SceneDestination destination,
        IReadOnlyList<WorldPlayerSlotInfo>? worldSlots = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (worldSlots is not null)
            UpdateParticipants(worldSlots.Select(s =>
                new GameSessionParticipant(s.PlayerId, s.CharacterId, s.InputSourceId)));
        CurrentLevel = destination;
        checked { LevelEntryCount++; }
    }

    /// <summary>Snapshot for preparation; does not mutate the running session.</summary>
    public IReadOnlyList<GameSessionParticipant> PreviewRoster(
        IReadOnlyList<WorldPlayerSlotInfo>? currentWorldSlots, int firstPlayerSource)
    {
        if (firstPlayerSource <= 0)
            throw new ArgumentOutOfRangeException(nameof(firstPlayerSource));
        GameSessionParticipant[] roster = currentWorldSlots is { Count: > 0 }
            ? currentWorldSlots.Select(s => new GameSessionParticipant(
                s.PlayerId, s.CharacterId, s.InputSourceId)).ToArray()
            : _members.Values.ToArray();
        for (int i = 0; i < roster.Length; i++)
            if (roster[i].PlayerId == 1)
                roster[i] = roster[i] with { InputSourceId = firstPlayerSource };
        return Array.AsReadOnly(roster);
    }
}

/// <summary>
/// Owned resources are released exactly once when a level/scene retires.
/// Shared application-owned graphics resources must never be registered here.
/// Current graybox worlds have no separately disposable GPU resources.
/// </summary>
public sealed class SceneResourceScope : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    public bool IsDisposed { get; private set; }
    public int ResourceCount => _resources.Count;

    public T Own<T>(T resource) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (IsDisposed) throw new ObjectDisposedException(nameof(SceneResourceScope));
        _resources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        for (int i = _resources.Count - 1; i >= 0; i--)
            _resources[i].Dispose();
        _resources.Clear();
    }
}
