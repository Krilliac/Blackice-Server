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
    private readonly string _role;
    private DiffieHellmanCryptoProvider? _crypto;

    public IPEndPoint Remote { get; }
    /// <summary>Per-peer slot for role handlers to stash state (e.g. authenticated userId).</summary>
    public object? Tag { get; set; }

    public PeerConnection(string role, IPEndPoint remote, IOperationHandler handler, Action<IReadOnlyList<NCommand>, int> send)
    {
        _role = role;
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
            if (cmd.CommandType == NCommand.Connect)
            {
                Log.Info(_role, $"{Remote} eNet CONNECT");
                _handler.OnConnect(this);
            }
            if (cmd.CommandType == NCommand.Disconnect)
            {
                // The client is telling us it's leaving. We don't tear down transport state yet
                // (Phase 1), but record it — a Disconnect here vs. a silent timeout are very
                // different failure modes when diagnosing "kicked out of the game".
                Log.Info(_role, $"{Remote} eNet DISCONNECT received");
                _handler.OnDisconnect(this);
            }
            if (payload is not null) HandleAppPayload(payload);
        }
        if (control.Count > 0) _send(control, _enet.Challenge);
    }

    private void HandleAppPayload(byte[] payload)
    {
        if (!WireMessage.TryPeekType(payload, out var type, out var encrypted))
        {
            Log.Warn(_role, $"{Remote} dropped non-Photon payload ({payload.Length}B: {PhotonNames.Hex(payload, 32)})");
            return;
        }

        // Init handshake: a fixed 41-byte blob naming the target app. The client only needs a
        // 2-byte InitResponse back before it proceeds to encryption + authentication.
        if (type == WireMessage.Init)
        {
            Log.Info(_role, $"{Remote} Init -> InitResponse");
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
            Log.Exception(_role, $"{Remote} dropped malformed {PhotonNames.MessageType(type)} message " +
                                 $"(enc={encrypted}, {payload.Length}B)", ex);
            return;
        }

        if (msg is { MessageType: WireMessage.InternalOperationRequest, Code: 0 })
        {
            Log.Info(_role, $"{Remote} InitEncryption -> deriving shared key");
            EstablishEncryption(msg.Parameters);
            return;
        }

        if (msg.MessageType == WireMessage.Operation)
            Log.Info(_role, $"{Remote} <- {PhotonNames.Op(msg.Code)} [{PhotonNames.Params(msg.Parameters)}]");
        else
            Log.Info(_role, $"{Remote} <- {PhotonNames.MessageType(msg.MessageType)} code={msg.Code} " +
                            $"[{PhotonNames.Params(msg.Parameters)}]");

        try
        {
            _handler.OnOperationRequest(this, new OperationRequest(msg.Code, msg.Parameters));
        }
        catch (Exception ex)
        {
            // A handler throwing on one operation must not kill the peer/listener silently. Log it
            // with the offending op so we can see exactly which message broke processing.
            Log.Exception(_role, $"{Remote} handler threw on {PhotonNames.Op(msg.Code)}", ex);
        }
    }

    /// <summary>InitEncryption: derive the shared key from the client's public key, return ours.</summary>
    private void EstablishEncryption(Dictionary<byte, object> parameters)
    {
        var clientPublicKey = (byte[])parameters[1];   // PhotonCodes.ClientKey
        _crypto = new DiffieHellmanCryptoProvider();
        var serverPublicKey = _crypto.PublicKey;
        _crypto.DeriveSharedKey(clientPublicKey);
        Log.Info(_role, $"{Remote} shared key derived (clientKey {clientPublicKey.Length}B, serverKey {serverPublicKey.Length}B)");
        var response = new OperationResponse(0, 0, null, new() { { 1, serverPublicKey } }); // ServerKey
        SendRaw(WireMessage.Response(response, WireMessage.InternalOperationResponse));
    }

    public void SendResponse(OperationResponse response)
    {
        Log.Info(_role, $"{Remote} -> {PhotonNames.Op(response.OperationCode)} response rc={response.ReturnCode}" +
                        $"{(response.DebugMessage is null ? "" : $" \"{response.DebugMessage}\"")} " +
                        $"[{PhotonNames.Params(response.Parameters)}]");
        SendRaw(WireMessage.Response(response));
    }

    public void RaiseEvent(EventData ev)
    {
        Log.Info(_role, $"{Remote} -> raise {PhotonNames.Event(ev.Code)} [{PhotonNames.Params(ev.Parameters)}]");
        SendRaw(WireMessage.EventMessage(ev));
    }

    private void SendRaw(byte[] appMessage) =>
        _send(new[] { _enet.WrapReliable(appMessage) }, _enet.Challenge);
}
