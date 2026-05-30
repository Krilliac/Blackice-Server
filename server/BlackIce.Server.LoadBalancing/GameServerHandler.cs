using BlackIce.Photon;
using BlackIce.Server.Core;
using BlackIce.Server.Data;

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
    private readonly AccountService? _accounts;
    private readonly RealmService? _realms;
    private readonly MotdService? _motd;

    /// <param name="allowAnonymousLan">
    /// When true, tokenless auth (LAN mode) is accepted from loopback/private-range peers only.
    /// Defaults to false (secure): a valid token from the Master/Name Server is required.
    /// </param>
    /// <param name="accounts">Account store for ban enforcement on the resolved SteamID (optional in tests).</param>
    /// <param name="realms">Realm definitions whose ruleset is applied on join (optional in tests).</param>
    /// <param name="motd">Message of the Day service; when provided the resolved MOTD is stamped as a room property.</param>
    public GameServerHandler(string secret, RoomRegistry registry, bool allowAnonymousLan = false,
                             AccountService? accounts = null, RealmService? realms = null, MotdService? motd = null)
    {
        _secret = secret;
        _registry = registry;
        _realms = realms;
        _allowAnonymousLan = allowAnonymousLan;
        _accounts = accounts;
        _motd = motd;
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
                var (response, join) = EnterRoom(request, ExtractJoinPassword(request));
                peer.SendResponse(response);
                if (response.ReturnCode == 0) peer.RaiseEvent(join);
                break;
            default:
                peer.SendResponse(new OperationResponse(request.OperationCode, -2, "Unknown operation", new()));
                break;
        }
    }

    public OperationResponse Authenticate(OperationRequest r, bool allowAnonymous = false)
    {
        if (r.Parameters.TryGetValue(PSecret, out var t) && t is string token)
        {
            if (!AuthToken.TryValidate(token, _secret, out var steamId))
                return new OperationResponse(OpAuthenticate, -1, "Invalid token", new());
            if (_accounts?.Find(steamId)?.IsBanned == true)
                return new OperationResponse(OpAuthenticate, -3, "Account banned", new());
            return new OperationResponse(OpAuthenticate, 0, null, new());
        }

        return allowAnonymous
            ? new OperationResponse(OpAuthenticate, 0, null, new())
            : new OperationResponse(OpAuthenticate, -1, "Authentication token required", new());
    }

    public (OperationResponse Response, EventData Join) EnterRoom(OperationRequest r, string? joinPassword)
    {
        var name = r.Parameters.TryGetValue(PRoomName, out var n) ? n.ToString()! : "room";
        var realm = _realms?.Get(name);

        // When realms are configured, only known+enabled realms are joinable, and a locked
        // realm requires the matching password. (No realms configured = open, for tests.)
        if (_realms is not null && (realm is null || !realm.IsEnabled))
            return (new OperationResponse(r.OperationCode, -4, "No such realm", new()), new EventData(EvJoin, new()));
        if (realm is not null && realm.Password.Length > 0 && joinPassword != realm.Password)
            return (new OperationResponse(r.OperationCode, -5, "Wrong password", new()), new EventData(EvJoin, new()));

        var room = _registry.GetOrCreate(name);
        int actor = room.AddActor();

        // Stamp the realm ruleset into the room's game properties so the client sees it in-room.
        var gameProps = new Dictionary<object, object>();
        if (realm is not null)
        {
            gameProps["PVP"] = realm.Pvp;
            gameProps["HackDifficultyIncrease"] = realm.HackDifficultyIncrease;
            gameProps["Password"] = realm.Password;
        }

        var motdText = _motd?.Resolve(realm);
        if (!string.IsNullOrWhiteSpace(motdText)) gameProps["motd"] = motdText!;

        var response = new OperationResponse(r.OperationCode, 0, null, new()
        {
            { PActorNr, actor },
            { PGameProperties, gameProps },
            { PActorProperties, new Dictionary<byte, object>() },
        });
        var join = new EventData(EvJoin, new()
        {
            { PActorNr, actor },
            { PActorList, room.ActorNumbers.ToArray() },
        });
        return (response, join);
    }

    /// <summary>Reads a join password from the request's GameProperties hashtable, if present.</summary>
    private static string? ExtractJoinPassword(OperationRequest r)
        => r.Parameters.TryGetValue(PGameProperties, out var gp)
           && gp is System.Collections.IDictionary d && d.Contains("Password")
            ? d["Password"]?.ToString() : null;
}
