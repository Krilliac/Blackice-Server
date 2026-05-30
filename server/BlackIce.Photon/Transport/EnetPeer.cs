using System.Buffers.Binary;

namespace BlackIce.Photon.Transport;

/// <summary>
/// Per-peer eNet transport state: the CONNECT/VERIFYCONNECT handshake, reliable-command
/// acknowledgements, and per-channel outgoing sequence numbers. Fragmentation and unreliable
/// channels are deferred (Phase 2) — the connect flow is small reliable commands only.
/// </summary>
public sealed class EnetPeer
{
    private static int _peerCounter;
    private readonly Dictionary<byte, int> _outgoingSeq = new();

    public short PeerId { get; private set; } = -1;
    /// <summary>The client's per-connection challenge, echoed in every outgoing packet header.</summary>
    public int Challenge { get; private set; }

    public void NoteChallenge(int challenge) => Challenge = challenge;

    /// <summary>
    /// Processes one incoming command. Returns control commands to send back (VERIFYCONNECT, ACK);
    /// if the command carried an application payload (reliable send), it is returned via <paramref name="appPayload"/>.
    /// </summary>
    public List<NCommand> HandleCommand(NCommand cmd, int incomingSentTime, out byte[]? appPayload)
    {
        appPayload = null;
        var outgoing = new List<NCommand>();
        switch (cmd.CommandType)
        {
            case NCommand.Connect:
                if (PeerId < 0) PeerId = (short)Interlocked.Increment(ref _peerCounter);
                outgoing.Add(VerifyConnect());
                break;
            case NCommand.SendReliable:
                outgoing.Add(Ack(cmd, incomingSentTime));
                appPayload = cmd.Payload;
                break;
            case NCommand.Ping:
                outgoing.Add(Ack(cmd, incomingSentTime));
                break;
            // Acknowledge (1) and Disconnect (4): nothing to emit during the Phase 1 connect flow.
        }
        return outgoing;
    }

    /// <summary>Wraps an application payload ([0xF3]... message) as a reliable command on channel 0.</summary>
    public NCommand WrapReliable(byte[] payload, byte channel = 0)
        => new(NCommand.SendReliable, channel, NCommand.FlagReliable, 4, NextSeq(channel), payload);

    private NCommand VerifyConnect()
    {
        var payload = new byte[32];                       // client reads peerId then skips 30 bytes
        BinaryPrimitives.WriteInt16BigEndian(payload, PeerId);
        return new NCommand(NCommand.VerifyConnect, 0xFF, NCommand.FlagReliable, 4, NextSeq(0xFF), payload);
    }

    private static NCommand Ack(NCommand acked, int sentTime)
    {
        var payload = new byte[8];                        // [ackedReliableSeq][ackedSentTime], big-endian
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0), acked.ReliableSequenceNumber);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4), sentTime);
        return new NCommand(NCommand.Acknowledge, acked.ChannelId, 0, 4, acked.ReliableSequenceNumber, payload);
    }

    private int NextSeq(byte channel)
    {
        _outgoingSeq.TryGetValue(channel, out int n);
        n++;
        _outgoingSeq[channel] = n;
        return n;
    }
}
