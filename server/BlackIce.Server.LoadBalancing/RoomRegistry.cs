using System.Collections.Concurrent;

namespace BlackIce.Server.LoadBalancing;

/// <summary>An in-memory game room and its membership. Persistence is out of scope for Phase 1.</summary>
public sealed class Room
{
    public required string Name { get; init; }
    public Dictionary<byte, object> Properties { get; } = new();
    public List<int> ActorNumbers { get; } = new();
    private int _nextActor;

    public int AddActor()
    {
        var actor = Interlocked.Increment(ref _nextActor);
        lock (ActorNumbers) ActorNumbers.Add(actor);
        return actor;
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
