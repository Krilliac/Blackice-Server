using BlackIce.Photon;
using Xunit;

namespace BlackIce.Photon.Tests;

/// <summary>Wire-framing interop: [0xF3][msgType][body], body verified against the real codec.</summary>
public class WireMessageTests
{
    [Fact]
    public void We_parse_a_client_style_operation_message()
    {
        // Build what the client puts in a reliable command: [0xF3][2] + body([op][table]).
        var body = Oracle.SerializeRequestBody(227, new() { { 255, "Black Ice Public Game #1" } });
        var wire = new byte[] { WireMessage.Magic, WireMessage.Operation }.Concat(body).ToArray();

        var parsed = WireMessage.Parse(wire);
        Assert.Equal(WireMessage.Operation, parsed.MessageType);
        Assert.Equal(227, parsed.Code);
        Assert.Equal("Black Ice Public Game #1", parsed.Parameters[255]);
    }

    [Fact]
    public void Clients_codec_parses_our_response_body()
    {
        // Our response wire message, minus the [0xF3][3] frame, is a valid response the client reads.
        var wire = WireMessage.Response(new OperationResponse(230, 0, null, new()
        {
            { 230, "127.0.0.1:5055" }, { 221, "token.sig" },
        }));
        Assert.Equal(WireMessage.Magic, wire[0]);
        Assert.Equal(WireMessage.OperationResponse, wire[1]);

        // The body ([op][rc][debug][table]) is parseable as a full GpBinary response when prefixed
        // with the standalone type byte (25), confirming our field order matches Photon.
        var standalone = new byte[] { 25 }.Concat(wire[2..]).ToArray();
        var msg = (ExitGames.Client.Photon.OperationResponse)Oracle.DeserializeMessage(standalone);
        Assert.Equal(230, msg.OperationCode);
        Assert.Equal("127.0.0.1:5055", msg.Parameters[230]);
    }

    [Fact]
    public void Roundtrip_internal_request_for_init_encryption()
    {
        // InitEncryption: InternalOperationRequest (6), op 0, ClientKey(1) = public key bytes.
        var body = WireMessage.RequestBody(0, new() { { 1, new byte[] { 9, 8, 7 } } });
        var wire = new byte[] { WireMessage.Magic, WireMessage.InternalOperationRequest }.Concat(body).ToArray();

        var parsed = WireMessage.Parse(wire);
        Assert.Equal(WireMessage.InternalOperationRequest, parsed.MessageType);
        Assert.Equal(0, parsed.Code);
        Assert.Equal(new byte[] { 9, 8, 7 }, (byte[])parsed.Parameters[1]);
    }
}
