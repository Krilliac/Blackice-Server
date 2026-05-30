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
}
