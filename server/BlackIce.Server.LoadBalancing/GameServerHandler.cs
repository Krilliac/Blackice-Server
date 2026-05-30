using BlackIce.Photon;
using BlackIce.Server.Core;

namespace BlackIce.Server.LoadBalancing;

/// <summary>
/// Photon Game Server role: re-authenticates via token, enters the actual room (CreateGame/JoinGame),
/// assigns an actor number, and raises the Join event (255) — the in-room milestone.
/// </summary>
public sealed class GameServerHandler : IOperationHandler
{
    private const byte OpAuthenticate = 230, OpCreateGame = 227, OpJoinGame = 226;
    private const byte EvJoin = 255;
    private const byte PRoomName = 255, PSecret = 221, PActorNr = 254, PActorList = 252,
                       PGameProperties = 248, PActorProperties = 249;

    private readonly string _secret;
    private readonly RoomRegistry _registry;
    private readonly bool _allowAnonymousLan;

    /// <param name="allowAnonymousLan">
    /// When true, tokenless auth (LAN mode) is accepted from loopback/private-range peers only.
    /// Defaults to false (secure): a valid token from the Master/Name Server is required.
    /// </param>
    public GameServerHandler(string secret, RoomRegistry registry, bool allowAnonymousLan = false)
    {
        _secret = secret;
        _registry = registry;
        _allowAnonymousLan = allowAnonymousLan;
    }

    public void OnConnect(PeerConnection peer) { }
    public void OnDisconnect(PeerConnection peer) { }

    public void OnOperationRequest(PeerConnection peer, OperationRequest request)
    {
        switch (request.OperationCode)
        {
            case OpAuthenticate:
                peer.SendResponse(Authenticate(request, _allowAnonymousLan && TrustedNetwork.IsLanOrLoopback(peer.Remote)));
                break;
            case OpCreateGame:
            case OpJoinGame:
                var (response, join) = EnterRoom(request);
                peer.SendResponse(response);
                peer.RaiseEvent(join);
                break;
            default:
                peer.SendResponse(new OperationResponse(request.OperationCode, -2, "Unknown operation", new()));
                break;
        }
    }

    public OperationResponse Authenticate(OperationRequest r, bool allowAnonymous = false)
    {
        if (r.Parameters.TryGetValue(PSecret, out var t) && t is string token)
            return AuthToken.TryValidate(token, _secret, out _)
                ? new OperationResponse(OpAuthenticate, 0, null, new())
                : new OperationResponse(OpAuthenticate, -1, "Invalid token", new());

        return allowAnonymous
            ? new OperationResponse(OpAuthenticate, 0, null, new())
            : new OperationResponse(OpAuthenticate, -1, "Authentication token required", new());
    }

    public (OperationResponse Response, EventData Join) EnterRoom(OperationRequest r)
    {
        var name = r.Parameters.TryGetValue(PRoomName, out var n) ? n.ToString()! : "room";
        var room = _registry.GetOrCreate(name);
        int actor = room.AddActor();

        var response = new OperationResponse(r.OperationCode, 0, null, new()
        {
            { PActorNr, actor },
            { PGameProperties, new Dictionary<byte, object>(room.Properties) },
            { PActorProperties, new Dictionary<byte, object>() },
        });
        var join = new EventData(EvJoin, new()
        {
            { PActorNr, actor },
            { PActorList, room.ActorNumbers.ToArray() },
        });
        return (response, join);
    }
}
