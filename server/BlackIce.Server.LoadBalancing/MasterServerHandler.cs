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
    private readonly bool _allowAnonymousLan;

    /// <param name="allowAnonymousLan">
    /// When true, tokenless first-contact auth (the game's LAN mode) is accepted — but only from
    /// loopback/private-range peers. Defaults to false (secure): the full Name Server token is required.
    /// </param>
    public MasterServerHandler(string gameAddress, string secret, RoomRegistry registry, bool allowAnonymousLan = false)
    {
        _gameAddress = gameAddress;
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
                bool anon = _allowAnonymousLan && TrustedNetwork.IsLanOrLoopback(peer.Remote);
                peer.SendResponse(Authenticate(request, anon));
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

    public OperationResponse Authenticate(OperationRequest r, bool allowAnonymous = false)
    {
        // Full Name Server flow: a token is present and must validate.
        if (r.Parameters.TryGetValue(PSecret, out var t) && t is string token)
            return AuthToken.TryValidate(token, _secret, out _)
                ? new OperationResponse(OpAuthenticate, 0, null, new())
                : new OperationResponse(OpAuthenticate, -1, "Invalid token", new());

        // LAN/direct flow (UseNameServer=false): no token issued. Only honored when explicitly
        // enabled AND the peer is on a trusted local network (checked by the caller).
        if (!allowAnonymous)
            return new OperationResponse(OpAuthenticate, -1, "Authentication token required", new());

        var userId = Guid.NewGuid().ToString();
        return new OperationResponse(OpAuthenticate, 0, null, new()
        {
            { PSecret, AuthToken.Mint(userId, _secret) },   // hand back a token for the Game hop
            { 225, userId },                                 // ParameterCode.UserId
        });
    }

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
