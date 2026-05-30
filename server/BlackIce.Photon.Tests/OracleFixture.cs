using ExitGames.Client.Photon;

namespace BlackIce.Photon.Tests;

/// <summary>Produces reference bytes using the real Photon GpBinaryV18 serializer (test-only oracle).</summary>
public static class Oracle
{
    private static readonly IProtocol Protocol = new Protocol18();

    /// <summary>Serializes a single typed value (type byte + payload).</summary>
    public static byte[] Serialize(object value) => Protocol.Serialize(value);

    public static byte[] SerializeOperationRequest(byte op, Dictionary<byte, object> parameters)
    {
        var sb = new StreamBuffer(64);
        Protocol.SerializeOperationRequest(sb, op, parameters, setType: true);
        return sb.ToArray();
    }

    /// <summary>Deserializes a single typed value using the real Photon codec.</summary>
    public static object? Deserialize(byte[] bytes) => Protocol.Deserialize(bytes);

    public static string Hex(byte[] b) => BitConverter.ToString(b);
}
