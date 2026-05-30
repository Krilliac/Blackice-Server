using BlackIce.Server.Core;
using BlackIce.Server.Data;
using BlackIce.Server.Host;
using BlackIce.Server.LoadBalancing;

var config = ServerConfig.Load("blackice.server.json");
if (args.Length > 0 && !args[0].StartsWith("--")) config.AdvertisedHost = args[0];
if (args.Contains("--require-token")) config.AllowAnonymousLan = false;
const string secret = "change-me-platform-sp1";

using var db = config.Database.CreateContext();      // EnsureCreated runs here
var accounts = new AccountService(db);

Console.WriteLine($"BlackIce.Server — DB {config.Database.Provider}, advertising {config.AdvertisedHost}");
Console.WriteLine($"*** One-time bootstrap code (redeem in-game once available): {accounts.EnsureBootstrapCode()} ***");

var registry = new RoomRegistry();
registry.GetOrCreate(config.TestRoomName);

var listeners = new[]
{
    new UdpListener("NameServer", 5058, new NameServerHandler($"{config.AdvertisedHost}:5055", secret, accounts)),
    new UdpListener("MasterServer", 5055, new MasterServerHandler($"{config.AdvertisedHost}:5056", secret, registry, config.AllowAnonymousLan, config.TestRoomName, accounts)),
    new UdpListener("GameServer", 5056, new GameServerHandler(secret, registry, config.AllowAnonymousLan, accounts)),
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Console admin command loop on a background thread (its own context for thread-safety).
var processor = new ConsoleCommandProcessor(new AccountService(config.Database.CreateContext()));
var consoleThread = new Thread(() =>
{
    Console.WriteLine("console ready — type 'help'.");
    string? line;
    while (!cts.IsCancellationRequested && (line = Console.ReadLine()) != null)
    {
        try
        {
            var outp = processor.Execute(line);
            if (!string.IsNullOrEmpty(outp)) Console.WriteLine(outp);
        }
        catch (Exception ex) { Console.Error.WriteLine($"command error: {ex.Message}"); }
    }
}) { IsBackground = true };
consoleThread.Start();

Console.WriteLine("Listening — NS 5058 / Master 5055 / Game 5056");
try { await Task.WhenAll(listeners.Select(l => l.RunAsync(cts.Token))); }
catch (OperationCanceledException) { }
Console.WriteLine("BlackIce.Server stopped.");
