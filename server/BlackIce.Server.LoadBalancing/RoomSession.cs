using BlackIce.Photon;
using BlackIce.Server.Core;

namespace BlackIce.Server.LoadBalancing;

/// <summary>
/// Per-room relay: holds the connected peers by actor number, runs the interceptor chain over an
/// inbound event, and fans the resulting event(s) out to every OTHER actor in the room. Driven from
/// the single-threaded UDP listener loop. The <c>_gate</c> lock guards ONLY the membership map and
/// the spawn cache (snapshot under the lock, send outside it); outbound sends themselves are NOT
/// serialized by <c>_gate</c> — cross-thread safety of a peer's sequence state relies on EnetPeer's
/// own internal locking.
/// </summary>
public sealed class RoomSession
{
    // PUN event codes carried on the relay. 202 = networked object Instantiation (sent with
    // EventCaching.AddToRoomCache so the server replays it to late joiners); 204 = Destroy of an
    // instantiated object. We mirror Photon's room cache so a player who joins after others have
    // spawned still renders them.
    private const byte EvInstantiation = 202, EvDestroy = 204;
    private const byte PData = 245;        // RaiseEvent data hashtable
    private const byte PunViewId = 7;      // key inside the 202 PData hashtable holding the int viewID

    private readonly object _gate = new();
    private readonly Dictionary<int, PeerConnection> _members = new();
    private readonly InterceptorChain _chain;

    // Ordered spawn cache keyed by viewID (latest 202 per viewID wins; re-spawns replace prior).
    // Insertion order is preserved for deterministic replay; a viewID re-cached keeps its existing
    // slot's order but updates the event, so a later despawn/respawn doesn't reorder the world.
    // 202s with no resolvable viewID get a negative synthetic key so they still replay (never dropped).
    private readonly Dictionary<int, EventData> _spawnCache = new();
    private readonly List<int> _spawnOrder = new();
    private int _syntheticViewId = -1;     // monotonically decreasing keys for viewID-less spawns

    public string RoomName { get; }

    public RoomSession(string roomName, InterceptorChain chain)
    {
        RoomName = roomName; _chain = chain;
    }

    public void Join(int actor, PeerConnection peer) { lock (_gate) _members[actor] = peer; }
    public void Leave(int actor) { lock (_gate) _members.Remove(actor); }
    public int Count { get { lock (_gate) return _members.Count; } }

    /// <summary>Runs the interceptor chain over <paramref name="ev"/> and fans the verdict out to
    /// every actor except <paramref name="senderActor"/>.</summary>
    public void RelayFrom(int senderActor, EventData ev, bool unreliable = false)
    {
        var verdict = _chain.Run(new EventContext(RoomName, senderActor, ev, unreliable));
        if (verdict.Action == RelayAction.Drop) return;

        List<PeerConnection> recipients;
        lock (_gate)
        {
            // Maintain the room spawn cache under the same lock that guards membership.
            if (verdict.Event is { Code: EvInstantiation }) CacheSpawn(verdict.Event);
            else if (verdict.Event is { Code: EvDestroy }) EvictSpawn(verdict.Event);

            recipients = new List<PeerConnection>(_members.Count);
            foreach (var (actor, peer) in _members)
                if (actor != senderActor) recipients.Add(peer);
        }

        foreach (var peer in recipients)
        {
            if (verdict.Event is not null) peer.RaiseEvent(verdict.Event, unreliable);
            foreach (var extra in verdict.Originated) peer.RaiseEvent(extra, unreliable);
        }
    }

    /// <summary>
    /// Replays the room's cached instantiation events to a single newly-joined actor (reliably, in
    /// insertion order) so the newcomer renders the world that already exists. Mirrors how the Photon
    /// server flushes EventCaching.AddToRoomCache to a late joiner. A snapshot is taken under the lock
    /// and the sends happen outside it, matching the <see cref="RelayFrom"/> pattern.
    /// </summary>
    public void ReplayCacheTo(int actor)
    {
        PeerConnection? peer;
        List<EventData> snapshot;
        lock (_gate)
        {
            if (!_members.TryGetValue(actor, out peer)) return;
            snapshot = new List<EventData>(_spawnOrder.Count);
            foreach (var key in _spawnOrder)
                if (_spawnCache.TryGetValue(key, out var ev)) snapshot.Add(ev);
        }

        foreach (var ev in snapshot) peer.RaiseEvent(ev);   // reliable: spawns must not be dropped
    }

    /// <summary>Caches a 202 keyed by its viewID (latest wins, existing order preserved). Must hold <c>_gate</c>.</summary>
    private void CacheSpawn(EventData ev)
    {
        int key = TryReadViewId(ev, out var viewId) ? viewId : _syntheticViewId--;
        if (!_spawnCache.ContainsKey(key)) _spawnOrder.Add(key);
        _spawnCache[key] = ev;   // re-spawn / update of the same viewID replaces the prior entry
    }

    /// <summary>Evicts the cached spawn a 204 destroy refers to, so a despawned object is not replayed.
    /// Must hold <c>_gate</c>.</summary>
    private void EvictSpawn(EventData ev)
    {
        if (!TryReadDestroyViewId(ev, out var viewId)) return;   // can't resolve: leave cached (re-instantiate is tolerated)
        if (_spawnCache.Remove(viewId)) _spawnOrder.Remove(viewId);
    }

    /// <summary>Reads the viewID from a 202's PData(245) hashtable at key 7 (PUN's instantiation viewID slot).</summary>
    private static bool TryReadViewId(EventData ev, out int viewId)
    {
        viewId = 0;
        if (!ev.Parameters.TryGetValue(PData, out var raw) || raw is not IDictionary<object, object> pdata) return false;
        if (pdata.TryGetValue(PunViewId, out var v) && v is int i) { viewId = i; return true; }
        return false;
    }

    /// <summary>
    /// Best-effort viewID extraction from a 204 destroy. PUN's destroy payload is less rigidly shaped
    /// than the 202 instantiation, so we first try the viewID slot (key 7), then fall back to scanning
    /// the PData hashtable values for any int that matches a currently-cached viewID. If nothing matches
    /// we return false and the caller conservatively keeps the spawn (replaying a stale spawn is far less
    /// harmful than dropping a live one — the client tolerates a re-instantiate).
    /// </summary>
    private bool TryReadDestroyViewId(EventData ev, out int viewId)
    {
        viewId = 0;
        if (!ev.Parameters.TryGetValue(PData, out var raw) || raw is not IDictionary<object, object> pdata) return false;
        if (pdata.TryGetValue(PunViewId, out var v) && v is int i && _spawnCache.ContainsKey(i)) { viewId = i; return true; }
        foreach (var value in pdata.Values)
            if (value is int candidate && _spawnCache.ContainsKey(candidate)) { viewId = candidate; return true; }
        return false;
    }
}
