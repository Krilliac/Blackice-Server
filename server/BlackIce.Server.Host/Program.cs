using BlackIce.Server.Core;
using BlackIce.Server.LoadBalancing;

// Usage: BlackIce.Server.Host [advertisedHost]
// advertisedHost is the address the server tells clients to use for the Master/Game hops
// (must be reachable by the client). Defaults to 127.0.0.1 for local testing.
var advertised = args.Length > 0 ? args[0] : "127.0.0.1";
const string secret = "change-me-phase1";

// LAN mode: the game connects directly to the Master with no Name Server token. We accept that
// only from loopback/private-range peers (enforced per-request). Disable for an internet-facing
// deployment that should require the full Name Server token flow.
bool allowAnonymousLan = !args.Contains("--require-token");

// Always-on test room: advertised in the lobby browser and joinable. Its obviously-custom name
// is your proof you're on this server and not Photon Cloud (no such room can exist on live).
const string testRoomName = "[CUSTOM SERVER] Test Room";

var registry = new RoomRegistry();
registry.GetOrCreate(testRoomName);   // exists from startup (always on)

var listeners = new[]
{
    new UdpListener("NameServer", 5058, new NameServerHandler($"{advertised}:5055", secret)),
    new UdpListener("MasterServer", 5055, new MasterServerHandler($"{advertised}:5056", secret, registry, allowAnonymousLan, testRoomName)),
    new UdpListener("GameServer", 5056, new GameServerHandler(secret, registry, allowAnonymousLan)),
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"BlackIce.Server starting — advertising {advertised} (NS 5058 / Master 5055 / Game 5056)");
try { await Task.WhenAll(listeners.Select(l => l.RunAsync(cts.Token))); }
catch (OperationCanceledException) { }
Console.WriteLine("BlackIce.Server stopped.");
