using System.Text.Json;
using BlackIce.Server.Data;

namespace BlackIce.Server.Host;

/// <summary>Server configuration, loaded from blackice.server.json (written with defaults if absent).</summary>
public sealed class ServerConfig
{
    public string AdvertisedHost { get; set; } = "127.0.0.1";
    public bool AllowAnonymousLan { get; set; } = true;
    public string TestRoomName { get; set; } = "[CUSTOM SERVER] Test Room";
    public DatabaseOptions Database { get; set; } = new();

    public static ServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var def = new ServerConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
            return def;
        }
        return JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path)) ?? new ServerConfig();
    }
}
