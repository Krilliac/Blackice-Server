using System.Collections.Concurrent;

namespace BlackIce.Server.LoadBalancing;

/// <summary>An in-memory game room and its membership. Persistence is out of scope for Phase 1.</summary>
public sealed class Room
{
    public required string Name { get; init; }
    public Dictionary<byte, object> Properties { get; } = new();
    public List<int> ActorNumbers { get; } = new();
    private int _nextActor;

    private readonly object _propsLock = new();
    private readonly Dictionary<object, object> _gameProps = new();
    private readonly Dictionary<int, Dictionary<object, object>> _actorProps = new();

    public int AddActor()
    {
        var actor = Interlocked.Increment(ref _nextActor);
        lock (ActorNumbers) ActorNumbers.Add(actor);
        return actor;
    }

    /// <summary>
    /// Merges a property set into the room. With <paramref name="actorNr"/>, the values are that
    /// actor's player properties; without it, they are the shared game properties. This is how
    /// OpSetProperties (op 252) persists in-room state so later joiners / GetProperties see it.
    /// Keys/values are stored as-is (Photon's loosely-typed hashtable entries).
    /// </summary>
    public void SetProperties(int? actorNr, System.Collections.IDictionary props)
    {
        lock (_propsLock)
        {
            var target = actorNr is int nr
                ? (_actorProps.TryGetValue(nr, out var ap) ? ap : _actorProps[nr] = new())
                : _gameProps;
            foreach (System.Collections.DictionaryEntry e in props)
                if (e.Key is not null) target[e.Key] = e.Value!;
        }
    }

    /// <summary>Snapshot of the game (shared) properties.</summary>
    public IReadOnlyDictionary<object, object> GameProperties
    {
        get { lock (_propsLock) return new Dictionary<object, object>(_gameProps); }
    }

    /// <summary>Snapshot of one actor's player properties, or empty if none set.</summary>
    public IReadOnlyDictionary<object, object> ActorProperties(int actorNr)
    {
        lock (_propsLock)
            return _actorProps.TryGetValue(actorNr, out var ap)
                ? new Dictionary<object, object>(ap) : new Dictionary<object, object>();
    }
}

/// <summary>Tracks rooms shared across the Master and Game server roles.</summary>
public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    public Room GetOrCreate(string name) => _rooms.GetOrAdd(name, n => new Room { Name = n });
    public Room? Find(string name) => _rooms.TryGetValue(name, out var r) ? r : null;
    public IReadOnlyCollection<Room> All => (IReadOnlyCollection<Room>)_rooms.Values;
}
