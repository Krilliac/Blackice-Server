using BlackIce.Photon;
using BlackIce.Server.Core;
using BlackIce.Server.Data;

namespace BlackIce.Server.LoadBalancing;

/// <summary>
/// Photon Name Server role: authenticates the client, resolves/creates the account for the
/// SteamID it presents (Photon UserId, param 225), rejects bans, and hands the client to the
/// Master Server by returning the Master address (230), a token over the SteamID (221), and the
/// UserId (225).
/// </summary>
public sealed class NameServerHandler : IOperationHandler
{
    private const byte OpAuthenticate = 230;
    private const byte PAddress = 230, PSecret = 221, PUserId = 225;

    private readonly string _masterAddress;
    private readonly string _secret;
    private readonly AccountService _accounts;

    public NameServerHandler(string masterAddress, string secret, AccountService accounts)
    {
        _masterAddress = masterAddress;
        _secret = secret;
        _accounts = accounts;
    }

    public void OnConnect(PeerConnection peer) { }
    public void OnDisconnect(PeerConnection peer) { }

    public void OnOperationRequest(PeerConnection peer, OperationRequest request)
    {
        if (request.OperationCode == OpAuthenticate)
            peer.SendResponse(Authenticate(request));
        else
            peer.SendResponse(new OperationResponse(request.OperationCode, -2, "Unsupported on Name Server", new()));
    }

    public OperationResponse Authenticate(OperationRequest request)
    {
        // Identity = the SteamID the client mod sends as the Photon UserId (param 225). Without
        // the mod (e.g. native LAN) there is none, so we mint a non-authoritative id.
        var steamId = request.Parameters.TryGetValue(PUserId, out var u) && u is string s && s.Length > 0
            ? s : Guid.NewGuid().ToString();

        var account = _accounts.ResolveOrCreate(steamId, steamId);
        if (account.IsBanned)
            return new OperationResponse(OpAuthenticate, -3, "Account banned", new());

        return new OperationResponse(OpAuthenticate, 0, null, new()
        {
            { PAddress, _masterAddress },
            { PSecret, AuthToken.Mint(steamId, _secret) },
            { PUserId, steamId },
        });
    }
}
