using BlackIce.Photon;
using BlackIce.Server.Core;

namespace BlackIce.Server.LoadBalancing;

/// <summary>
/// Photon Master Server role: re-authenticates via the token, handles lobby + matchmaking, and
/// routes the client to the Game Server by returning its address (param 230) for CreateGame/JoinGame.
/// </summary>
public sealed class MasterServerHandler : IOperationHandler
{
    private const byte OpAuthenticate = 230, OpJoinLobby = 229, OpCreateGame = 227, OpJoinGame = 226;
    private const byte EvGameList = 230;
    private const byte PAddress = 230, PSecret = 221, PRoomName = 255, PGameList = 1;

    private readonly string _gameAddress;
    private readonly string _secret;
    private readonly RoomRegistry _registry;

    public MasterServerHandler(string gameAddress, string secret, RoomRegistry registry)
    {
        _gameAddress = gameAddress;
        _secret = secret;
        _registry = registry;
    }

    public void OnConnect(PeerConnection peer) { }
    public void OnDisconnect(PeerConnection peer) { }

    public void OnOperationRequest(PeerConnection peer, OperationRequest request)
    {
        switch (request.OperationCode)
        {
            case OpAuthenticate:
                peer.SendResponse(Authenticate(request));
                break;
            case OpJoinLobby:
                peer.SendResponse(JoinLobby(request));
                peer.RaiseEvent(new EventData(EvGameList, new() { { PGameList, new Dictionary<byte, object>() } }));
                break;
            case OpCreateGame:
            case OpJoinGame:
                peer.SendResponse(CreateGame(request));
                break;
            default:
                peer.SendResponse(new OperationResponse(request.OperationCode, -2, "Unknown operation", new()));
                break;
        }
    }

    public OperationResponse Authenticate(OperationRequest r)
        => r.Parameters.TryGetValue(PSecret, out var t) && t is string token && AuthToken.TryValidate(token, _secret, out _)
            ? new OperationResponse(OpAuthenticate, 0, null, new())
            : new OperationResponse(OpAuthenticate, -1, "Invalid token", new());

    public OperationResponse JoinLobby(OperationRequest r) => new(OpJoinLobby, 0, null, new());

    public OperationResponse CreateGame(OperationRequest r)
    {
        var name = r.Parameters.TryGetValue(PRoomName, out var n) ? n.ToString()! : $"Room-{Guid.NewGuid():N}";
        _registry.GetOrCreate(name);
        return new OperationResponse(r.OperationCode, 0, null, new()
        {
            { PAddress, _gameAddress },
            { PRoomName, name },
        });
    }
}
