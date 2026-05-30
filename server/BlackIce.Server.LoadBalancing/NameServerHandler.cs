using BlackIce.Photon;
using BlackIce.Server.Core;

namespace BlackIce.Server.LoadBalancing;

/// <summary>
/// Photon Name Server role: authenticates the client and hands it to the Master Server by
/// returning the Master address (param 230), a minted token (221), and a UserId (225).
/// </summary>
public sealed class NameServerHandler : IOperationHandler
{
    private const byte OpAuthenticate = 230;
    private const byte PAddress = 230, PSecret = 221, PUserId = 225;

    private readonly string _masterAddress;
    private readonly string _secret;

    public NameServerHandler(string masterAddress, string secret)
    {
        _masterAddress = masterAddress;
        _secret = secret;
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
        var userId = Guid.NewGuid().ToString();
        return new OperationResponse(OpAuthenticate, 0, null, new()
        {
            { PAddress, _masterAddress },
            { PSecret, AuthToken.Mint(userId, _secret) },
            { PUserId, userId },
        });
    }
}
