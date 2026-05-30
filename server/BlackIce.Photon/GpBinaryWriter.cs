using System.Text;

namespace BlackIce.Photon;

/// <summary>
/// Writes values in GpBinary v1.8. Emits the simplest decode-compatible form per type
/// (e.g. always CompressedInt for ints) — the client's deserializer accepts these regardless
/// of Photon's own size optimizations. Fixed-width numerics are little-endian; integers are
/// zig-zag + unsigned LEB128.
/// </summary>
public sealed class GpBinaryWriter
{
    private readonly List<byte> _buf = new();

    public byte[] ToArray() => _buf.ToArray();

    /// <summary>Writes a raw byte with no type tag (operation code, parameter key, count).</summary>
    public void WriteByte(byte b) => _buf.Add(b);

    /// <summary>Writes a 16-bit value little-endian with no type tag (e.g. response ReturnCode).</summary>
    public void WriteInt16(short v) { _buf.Add((byte)(v & 0xFF)); _buf.Add((byte)((v >> 8) & 0xFF)); }

    /// <summary>Writes one typed value (type byte + payload).</summary>
    public GpBinaryWriter WriteTyped(object? value)
    {
        switch (value)
        {
            case null: _buf.Add(GpType.Null); break;
            case bool b: _buf.Add(b ? GpType.BooleanTrue : GpType.BooleanFalse); break;
            case byte by: _buf.Add(GpType.Byte); _buf.Add(by); break;
            case short s: _buf.Add(GpType.Short); WriteInt16(s); break;
            case int i: _buf.Add(GpType.CompressedInt); WriteUVarInt(EncodeZigZag32(i)); break;
            case long l: _buf.Add(GpType.CompressedLong); WriteUVarInt64(EncodeZigZag64(l)); break;
            case float f: _buf.Add(GpType.Float); _buf.AddRange(BitConverter.GetBytes(f)); break;
            case double d: _buf.Add(GpType.Double); _buf.AddRange(BitConverter.GetBytes(d)); break;
            case string str: _buf.Add(GpType.String); WriteStringBody(str); break;
            case byte[] ba: _buf.Add(GpType.ByteArray); WriteUVarInt((uint)ba.Length); _buf.AddRange(ba); break;
            case int[] ia: _buf.Add(GpType.IntArray); WriteIntArrayBody(ia); break;
            case Dictionary<byte, object> ht: _buf.Add(GpType.Hashtable); WriteHashtableBody(ht); break;
            default: throw new NotSupportedException($"GpBinary v1.8 write not implemented for {value.GetType()}");
        }
        return this;
    }

    private void WriteStringBody(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        WriteUVarInt((uint)bytes.Length);
        _buf.AddRange(bytes);
    }

    private void WriteIntArrayBody(int[] arr)
    {
        WriteUVarInt((uint)arr.Length);
        foreach (var n in arr) WriteUVarInt(EncodeZigZag32(n));
    }

    // A byte-keyed parameter table written as a Hashtable value: count byte, then key+typed value.
    private void WriteHashtableBody(Dictionary<byte, object> map)
    {
        WriteUVarInt((uint)map.Count);
        foreach (var kv in map) { WriteTyped(kv.Key); WriteTyped(kv.Value); }
    }

    internal static uint EncodeZigZag32(int v) => (uint)((v << 1) ^ (v >> 31));
    internal static ulong EncodeZigZag64(long v) => (ulong)((v << 1) ^ (v >> 63));

    /// <summary>Unsigned LEB128 (Photon WriteCompressedUInt32).</summary>
    internal void WriteUVarInt(uint value)
    {
        _buf.Add((byte)(value & 0x7F));
        for (value >>= 7; value != 0; value >>= 7)
        {
            _buf[^1] |= 0x80;
            _buf.Add((byte)(value & 0x7F));
        }
    }

    internal void WriteUVarInt64(ulong value)
    {
        _buf.Add((byte)(value & 0x7F));
        for (value >>= 7; value != 0; value >>= 7)
        {
            _buf[^1] |= 0x80;
            _buf.Add((byte)(value & 0x7F));
        }
    }
}
