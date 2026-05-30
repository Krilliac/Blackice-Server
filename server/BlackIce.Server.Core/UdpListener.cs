using System.Net;
using System.Net.Sockets;
using BlackIce.Photon;
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
        Log.Info(_name, $"listening on UDP :{((IPEndPoint)_socket.Client.LocalEndPoint!).Port}");
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _socket.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex) { Log.Debug(_name, $"recv socket error (ignored): {ex.SocketErrorCode}"); continue; }

            try { Process(result.Buffer, result.RemoteEndPoint); }
            catch (Exception ex)
            {
                // A single bad datagram must never take the listener down — but we now log the
                // full exception (was: one-line message) so a mid-session failure is visible.
                Log.Exception(_name, $"drop datagram from {result.RemoteEndPoint} " +
                                     $"({result.Buffer.Length}B: {PhotonNames.Hex(result.Buffer, 64)})", ex);
            }
        }
        Log.Info(_name, "listener loop exited");
    }

    private void Process(byte[] datagram, IPEndPoint from)
    {
        Log.Trace(_name, $"RECV {from} {datagram.Length}B: {PhotonNames.Hex(datagram, 128)}");
        if (datagram.Length < PhotonHeader.Size) { Log.Debug(_name, $"runt datagram from {from} ({datagram.Length}B)"); return; }
        var header = PhotonHeader.ReadFrom(datagram);
        Log.Trace(_name, $"  header peer={header.PeerId} crc={header.CrcEnabled} cmds={header.CommandCount} " +
                         $"srvTime={header.ServerTime} challenge={header.Challenge}");

        var commands = new List<NCommand>(header.CommandCount);
        int offset = PhotonHeader.Size;
        for (int i = 0; i < header.CommandCount && offset + NCommand.HeaderSize <= datagram.Length; i++)
        {
            var cmd = NCommand.Parse(datagram.AsSpan(offset), out int consumed);
            if (consumed <= 0) { Log.Warn(_name, $"command parse stalled at offset {offset} (consumed {consumed})"); break; }
            Log.Trace(_name, $"  cmd[{i}] {PhotonNames.Command(cmd.CommandType)} ch={cmd.ChannelId} " +
                             $"flags={cmd.Flags} seq={cmd.ReliableSequenceNumber} payload={cmd.Payload.Length}B");
            commands.Add(cmd);
            offset += consumed;
        }

        if (!_peers.TryGetValue(from, out var peer))
        {
            peer = new PeerConnection(_name, from, _handler, (cmds, challenge) => Send(from, cmds, challenge));
            _peers[from] = peer;
            Log.Info(_name, $"new peer {from} (total {_peers.Count})");
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
        try
        {
            int sent = _socket.Send(packet, packet.Length, to);
            if (Log.Enabled(LogLevel.Trace))
            {
                var kinds = string.Join("+", commands.Select(c => PhotonNames.Command(c.CommandType)));
                Log.Trace(_name, $"SEND {to} {sent}B [{kinds}] challenge={challenge}: {PhotonNames.Hex(packet, 128)}");
            }
        }
        catch (Exception ex)
        {
            // If the server ever stops replying mid-session the client times out and reconnects;
            // a send failure is the prime suspect, so surface it loudly rather than letting it bubble.
            Log.Exception(_name, $"SEND to {to} failed ({packet.Length}B)", ex);
        }
    }
}
