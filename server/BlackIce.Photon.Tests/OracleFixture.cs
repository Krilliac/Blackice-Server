using ExitGames.Client.Photon;

namespace BlackIce.Photon.Tests;

/// <summary>Produces reference bytes using the real Photon GpBinaryV18 serializer (test-only oracle).</summary>
public static class Oracle
{
    private static readonly IProtocol Protocol = new Protocol18();

    static Oracle()
    {
        // PUN registers its custom types (e.g. Vector3 = code 86) on the peer at runtime via
        // PhotonPeer.RegisterType, which populates Protocol18's static custom-type table. The bare
        // Photon3Unity3D.dll used as our oracle has none registered, so it would throw
        // "Custom type not found" on a code it has never seen. We register a byte-passthrough for the
        // codes our relay forwards so the oracle decodes our slim wire form exactly as the real client
        // would. Serialize/deserialize are identity over the body bytes — same payload PhotonCustomData carries.
        foreach (byte code in new byte[] { 86, 68 })
            PhotonPeer.RegisterType(typeof(PassthroughCustom), code, PassthroughCustom.Serialize, PassthroughCustom.Deserialize);
    }

    /// <summary>Test-only stand-in for a registered Photon custom type: carries the raw body bytes verbatim.</summary>
    private sealed class PassthroughCustom
    {
        public byte[] Data = System.Array.Empty<byte>();
        public static byte[] Serialize(object o) => ((PassthroughCustom)o).Data;
        public static object Deserialize(byte[] data) => new PassthroughCustom { Data = data };
    }

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

    /// <summary>Parses a full message (request/response/event) using the real Photon codec.</summary>
    public static object DeserializeMessage(byte[] bytes) => Protocol.DeserializeMessage(new StreamBuffer(bytes));

    /// <summary>Serializes an operation-request body with no leading GpType byte ([op][table]) — the wire form.</summary>
    public static byte[] SerializeRequestBody(byte op, Dictionary<byte, object> parameters)
    {
        var sb = new StreamBuffer(64);
        Protocol.SerializeOperationRequest(sb, op, parameters, setType: false);
        return sb.ToArray();
    }

    /// <summary>Parses an operation-request body ([op][table]) using the real Photon codec.</summary>
    public static ExitGames.Client.Photon.OperationRequest DeserializeRequestBody(byte[] body)
        => Protocol.DeserializeOperationRequest(new StreamBuffer(body));

    public static string Hex(byte[] b) => BitConverter.ToString(b);
}
