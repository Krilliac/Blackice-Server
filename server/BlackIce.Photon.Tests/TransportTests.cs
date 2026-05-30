using BlackIce.Photon.Transport;
using Xunit;

namespace BlackIce.Photon.Tests;

public class TransportTests
{
    [Fact]
    public void PacketHeader_roundtrips()
    {
        var h = new PhotonHeader(PeerId: 7, CrcEnabled: false, CommandCount: 3, ServerTime: 123456, Challenge: -42);
        var buf = new byte[PhotonHeader.Size];
        h.WriteTo(buf);
        Assert.Equal(h, PhotonHeader.ReadFrom(buf));
    }

    [Fact]
    public void PacketHeader_is_big_endian()
    {
        var buf = new byte[PhotonHeader.Size];
        new PhotonHeader(0, false, 0, 0x01020304, 0).WriteTo(buf);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, buf[4..8]); // ServerTime, network order
    }

    [Fact]
    public void Command_roundtrips()
    {
        var c = new NCommand(NCommand.SendReliable, ChannelId: 0, Flags: NCommand.FlagReliable, ReservedByte: 4,
                             ReliableSequenceNumber: 5, Payload: new byte[] { 9, 9, 9 });
        var bytes = c.ToBytes();
        var parsed = NCommand.Parse(bytes, out int consumed);
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(c.CommandType, parsed.CommandType);
        Assert.Equal(c.ReliableSequenceNumber, parsed.ReliableSequenceNumber);
        Assert.Equal(c.Payload, parsed.Payload);
    }

    private static NCommand Reliable(byte type, byte channel, int seq) =>
        new(type, channel, NCommand.FlagReliable, ReservedByte: 4, seq, Array.Empty<byte>());

    private static NCommand? AckFor(IEnumerable<NCommand> outgoing, byte channel, int seq) =>
        outgoing.FirstOrDefault(c => c.CommandType == NCommand.Acknowledge
                                     && c.ChannelId == channel && c.ReliableSequenceNumber == seq);

    [Theory]
    [InlineData((byte)6)]   // CT_SENDRELIABLE
    [InlineData((byte)5)]   // CT_PING
    [InlineData((byte)12)]  // CT_EG_SERVERTIME — the one that was silently killing sessions
    public void Every_reliable_command_is_acknowledged(byte type)
    {
        var peer = new EnetPeer();
        var outgoing = peer.HandleCommand(Reliable(type, channel: 255, seq: 7), incomingSentTime: 42, out _);
        Assert.NotNull(AckFor(outgoing, channel: 255, seq: 7));
    }

    [Fact]
    public void Reliable_connect_is_both_acked_and_verified()
    {
        var peer = new EnetPeer();
        var outgoing = peer.HandleCommand(Reliable(NCommand.Connect, channel: 255, seq: 1), incomingSentTime: 0, out _);
        Assert.NotNull(AckFor(outgoing, channel: 255, seq: 1));
        Assert.Contains(outgoing, c => c.CommandType == NCommand.VerifyConnect);
    }

    [Fact]
    public void Acknowledge_command_is_not_itself_acked()
    {
        var peer = new EnetPeer();
        var ack = new NCommand(NCommand.Acknowledge, ChannelId: 0, Flags: 0, ReservedByte: 4, 3, new byte[8]);
        var outgoing = peer.HandleCommand(ack, incomingSentTime: 0, out _);
        Assert.DoesNotContain(outgoing, c => c.CommandType == NCommand.Acknowledge);
    }

    [Fact]
    public void Unreliable_command_is_not_acked()
    {
        var peer = new EnetPeer();
        var unreliable = new NCommand(NCommand.SendUnreliable, ChannelId: 0, Flags: 0, ReservedByte: 4, 1, new byte[] { 1 });
        var outgoing = peer.HandleCommand(unreliable, incomingSentTime: 0, out _);
        Assert.DoesNotContain(outgoing, c => c.CommandType == NCommand.Acknowledge);
    }
}
