using System.Net;
using BlackIce.Photon;
using BlackIce.Photon.Crypto;
using BlackIce.Photon.Transport;

namespace BlackIce.Server.Core;

/// <summary>
/// One connected client. Owns the eNet transport state and (once established) the DH crypto
/// provider. Parses inbound application messages, transparently handles the InitEncryption
/// handshake, and dispatches operations to the role handler. Outbound responses/events are
/// framed and sent through the listener's send callback.
/// </summary>
public sealed class PeerConnection
{
    private readonly EnetPeer _enet = new();
    private readonly IOperationHandler _handler;
    private readonly Action<IReadOnlyList<NCommand>, int> _send;
    private DiffieHellmanCryptoProvider? _crypto;

    public IPEndPoint Remote { get; }
    /// <summary>Per-peer slot for role handlers to stash state (e.g. authenticated userId).</summary>
    public object? Tag { get; set; }

    public PeerConnection(IPEndPoint remote, IOperationHandler handler, Action<IReadOnlyList<NCommand>, int> send)
    {
        Remote = remote;
        _handler = handler;
        _send = send;
    }

    public void HandlePacket(PhotonHeader header, IReadOnlyList<NCommand> commands)
    {
        _enet.NoteChallenge(header.Challenge);
        var control = new List<NCommand>();
        foreach (var cmd in commands)
        {
            control.AddRange(_enet.HandleCommand(cmd, header.ServerTime, out var payload));
            if (cmd.CommandType == NCommand.Connect) _handler.OnConnect(this);
            if (payload is not null) HandleAppPayload(payload);
        }
        if (control.Count > 0) _send(control, _enet.Challenge);
    }

    private void HandleAppPayload(byte[] payload)
    {
        if (!WireMessage.TryPeekType(payload, out var type, out _))
        {
            Console.Error.WriteLine($"[{Remote}] dropped non-Photon payload");
            return;
        }

        // Init handshake: a fixed 41-byte blob naming the target app. The client only needs a
        // 2-byte InitResponse back before it proceeds to encryption + authentication.
        if (type == WireMessage.Init)
        {
            Console.WriteLine($"[{Remote}] Init -> InitResponse");
            SendRaw(WireMessage.InitResponseMessage());
            return;
        }

        WireMessage.Parsed msg;
        try
        {
            msg = WireMessage.Parse(payload, _crypto is not null ? b => _crypto!.Decrypt(b) : null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Remote}] dropped malformed message: {ex.Message}");
            return;
        }

        if (msg is { MessageType: WireMessage.InternalOperationRequest, Code: 0 })
        {
            Console.WriteLine($"[{Remote}] InitEncryption -> deriving shared key");
            EstablishEncryption(msg.Parameters);
            return;
        }

        Console.WriteLine($"[{Remote}] op {msg.Code} ({msg.Parameters.Count} params)");
        _handler.OnOperationRequest(this, new OperationRequest(msg.Code, msg.Parameters));
    }

    /// <summary>InitEncryption: derive the shared key from the client's public key, return ours.</summary>
    private void EstablishEncryption(Dictionary<byte, object> parameters)
    {
        var clientPublicKey = (byte[])parameters[1];   // PhotonCodes.ClientKey
        _crypto = new DiffieHellmanCryptoProvider();
        var serverPublicKey = _crypto.PublicKey;
        _crypto.DeriveSharedKey(clientPublicKey);
        var response = new OperationResponse(0, 0, null, new() { { 1, serverPublicKey } }); // ServerKey
        SendRaw(WireMessage.Response(response, WireMessage.InternalOperationResponse));
    }

    public void SendResponse(OperationResponse response) => SendRaw(WireMessage.Response(response));
    public void RaiseEvent(EventData ev) => SendRaw(WireMessage.EventMessage(ev));

    private void SendRaw(byte[] appMessage) =>
        _send(new[] { _enet.WrapReliable(appMessage) }, _enet.Challenge);
}
