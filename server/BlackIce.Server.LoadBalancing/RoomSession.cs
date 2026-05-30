using BlackIce.Photon;
using BlackIce.Server.Core;

namespace BlackIce.Server.LoadBalancing;

/// <summary>
/// Per-room relay: holds the connected peers by actor number, runs the interceptor chain over an
/// inbound event, and fans the resulting event(s) out to every OTHER actor in the room. Driven from
/// the single-threaded UDP listener loop; a lock guards membership for safety against maintenance.
/// </summary>
public sealed class RoomSession
{
    private readonly object _gate = new();
    private readonly Dictionary<int, PeerConnection> _members = new();
    private readonly InterceptorChain _chain;

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
    public void RelayFrom(int senderActor, EventData ev)
    {
        var verdict = _chain.Run(new EventContext(RoomName, senderActor, ev));
        if (verdict.Action == RelayAction.Drop) return;

        List<PeerConnection> recipients;
        lock (_gate)
        {
            recipients = new List<PeerConnection>(_members.Count);
            foreach (var (actor, peer) in _members)
                if (actor != senderActor) recipients.Add(peer);
        }

        foreach (var peer in recipients)
        {
            if (verdict.Event is not null) peer.RaiseEvent(verdict.Event);
            foreach (var extra in verdict.Originated) peer.RaiseEvent(extra);
        }
    }
}
