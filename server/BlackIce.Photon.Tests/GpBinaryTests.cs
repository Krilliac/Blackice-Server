using BlackIce.Photon;
using Xunit;

namespace BlackIce.Photon.Tests;

/// <summary>
/// Interop tests against the real Photon codec (oracle). We assert round-trip compatibility,
/// not byte-identity: the client must be able to decode what we write, and we must decode
/// what the client writes. This frees our writer from replicating Photon's size optimizations.
/// </summary>
public class GpBinaryTests
{
    public static IEnumerable<object[]> Values() => new[]
    {
        new object[] { (byte)200 }, new object[] { (byte)0 },
        new object[] { true }, new object[] { false },
        new object[] { (short)-1234 }, new object[] { (short)0 },
        new object[] { 0 }, new object[] { 1 }, new object[] { 255 }, new object[] { 1_000_000 }, new object[] { -50_000 },
        new object[] { -7L }, new object[] { 0L }, new object[] { 9_000_000_000L },
        new object[] { 3.5f }, new object[] { 2.5d },
        new object[] { "" }, new object[] { "Black Ice" }, new object[] { "188.241.71.81:5056" },
        new object[] { new byte[] { 1, 2, 250, 0 } },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void Client_can_decode_what_we_write(object value)
    {
        var ourBytes = new GpBinaryWriter().WriteTyped(value).ToArray();
        var decoded = Oracle.Deserialize(ourBytes);   // real Photon deserializer
        AssertValueEqual(value, decoded);
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void We_can_decode_what_the_client_writes(object value)
    {
        var clientBytes = Oracle.Serialize(value);     // real Photon serializer
        var decoded = new GpBinaryReader(clientBytes).ReadTyped();
        AssertValueEqual(value, decoded);
    }

    [Fact]
    public void Client_can_decode_our_int_array()
    {
        var arr = new[] { 1, 5, 42 };
        var decoded = Oracle.Deserialize(new GpBinaryWriter().WriteTyped(arr).ToArray());
        Assert.Equal(arr, (int[])decoded!);
    }

    [Fact]
    public void Client_can_decode_our_hashtable()
    {
        var ht = new Dictionary<byte, object> { { 1, "x" }, { 2, 7 } };
        var decoded = Oracle.Deserialize(new GpBinaryWriter().WriteTyped(ht).ToArray());
        Assert.NotNull(decoded);
    }

    [Fact]
    public void Reads_custom_type_slim_without_desync()
    {
        // A CustomTypeSlim value (PUN Vector3 = code 86 => type byte 0x80+86=0xD6), 12 bytes,
        // followed by a trailing marker we must still be aligned to read.
        var vec3 = new byte[] { 0x3F, 0x80, 0, 0, 0x3F, 0xC0, 0, 0, 0x3F, 0x80, 0, 0 };
        var bytes = new List<byte> { 0x80 + 86, 12 };
        bytes.AddRange(vec3);
        bytes.Add(GpType.Byte); bytes.Add(7);   // trailing byte value to prove alignment

        var r = new GpBinaryReader(bytes.ToArray());
        var custom = Assert.IsType<PhotonCustomData>(r.ReadTyped());
        Assert.Equal(86, custom.Code);
        Assert.Equal(vec3, custom.Data);
        Assert.Equal((byte)7, r.ReadTyped());    // still aligned
    }

    private static void AssertValueEqual(object expected, object? actual)
    {
        if (expected is byte[] eb) Assert.Equal(eb, (byte[])actual!);
        else Assert.Equal(expected, actual);
    }
}
