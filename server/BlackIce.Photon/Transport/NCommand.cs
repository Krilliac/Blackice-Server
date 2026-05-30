using System.Buffers.Binary;

namespace BlackIce.Photon.Transport;

/// <summary>
/// One eNet command: a 12-byte big-endian header followed by an optional payload.
/// Header: type(1) channel(1) flags(1) reserved(1) length(int32, incl. header) reliableSeq(int32).
/// </summary>
public sealed record NCommand(byte CommandType, byte ChannelId, byte Flags, byte ReservedByte,
                              int ReliableSequenceNumber, byte[] Payload)
{
    public const int HeaderSize = 12;

    // Command types (docs/protocol/05-transport.md).
    public const byte Acknowledge = 1, Connect = 2, VerifyConnect = 3, Disconnect = 4,
                      Ping = 5, SendReliable = 6, SendUnreliable = 7, SendFragment = 8;

    // Command flags.
    public const byte FlagReliable = 1, FlagUnsequenced = 2;

    public byte[] ToBytes()
    {
        int total = HeaderSize + Payload.Length;
        var b = new byte[total];
        b[0] = CommandType; b[1] = ChannelId; b[2] = Flags; b[3] = ReservedByte;
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), total);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), ReliableSequenceNumber);
        Payload.CopyTo(b.AsSpan(HeaderSize));
        return b;
    }

    public static NCommand Parse(ReadOnlySpan<byte> b, out int consumed)
    {
        byte type = b[0], channel = b[1], flags = b[2], reserved = b[3];
        int length = BinaryPrimitives.ReadInt32BigEndian(b[4..]);
        int seq = BinaryPrimitives.ReadInt32BigEndian(b[8..]);
        var payload = b[HeaderSize..length].ToArray();
        consumed = length;
        return new NCommand(type, channel, flags, reserved, seq, payload);
    }
}
