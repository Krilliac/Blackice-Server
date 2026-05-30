using System.Net;
using System.Net.Sockets;
using BlackIce.Photon.Transport;

namespace BlackIce.Server.Core;

/// <summary>
/// Receives Photon UDP datagrams for one server role, parses the packet header + commands,
/// and routes them to the owning <see cref="PeerConnection"/>. Malformed datagrams are logged
/// and dropped, never fatal.
/// </summary>
public sealed class UdpListener
{
    private readonly string _name;
    private readonly UdpClient _socket;
    private readonly IOperationHandler _handler;
    private readonly Dictionary<IPEndPoint, PeerConnection> _peers = new();

    public UdpListener(string name, int port, IOperationHandler handler)
    {
        _name = name;
        _handler = handler;
        _socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine($"[{_name}] listening on UDP :{((IPEndPoint)_socket.Client.LocalEndPoint!).Port}");
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _socket.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }

            try { Process(result.Buffer, result.RemoteEndPoint); }
            catch (Exception ex) { Console.Error.WriteLine($"[{_name}] drop from {result.RemoteEndPoint}: {ex.Message}"); }
        }
    }

    private void Process(byte[] datagram, IPEndPoint from)
    {
        if (datagram.Length < PhotonHeader.Size) return;
        var header = PhotonHeader.ReadFrom(datagram);

        var commands = new List<NCommand>(header.CommandCount);
        int offset = PhotonHeader.Size;
        for (int i = 0; i < header.CommandCount && offset + NCommand.HeaderSize <= datagram.Length; i++)
        {
            var cmd = NCommand.Parse(datagram.AsSpan(offset), out int consumed);
            if (consumed <= 0) break;
            commands.Add(cmd);
            offset += consumed;
        }

        if (!_peers.TryGetValue(from, out var peer))
        {
            peer = new PeerConnection(from, _handler, (cmds, challenge) => Send(from, cmds, challenge));
            _peers[from] = peer;
            Console.WriteLine($"[{_name}] new peer {from}");
        }
        peer.HandlePacket(header, commands);
    }

    private void Send(IPEndPoint to, IReadOnlyList<NCommand> commands, int challenge)
    {
        if (commands.Count == 0) return;
        var body = commands.SelectMany(c => c.ToBytes()).ToArray();
        var packet = new byte[PhotonHeader.Size + body.Length];
        new PhotonHeader(0, false, (byte)commands.Count, Environment.TickCount, challenge).WriteTo(packet);
        body.CopyTo(packet, PhotonHeader.Size);
        _socket.Send(packet, packet.Length, to);
    }
}
