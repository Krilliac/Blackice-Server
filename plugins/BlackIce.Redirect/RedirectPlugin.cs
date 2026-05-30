using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;

namespace BlackIce.Redirect;

/// <summary>
/// Realmlist-style server redirect. The player sets one config value (ServerAddress) and the
/// client connects to that custom BlackIce server instead of Photon Cloud. The override is
/// applied just before <see cref="PhotonNetwork.ConnectUsingSettings()"/> runs, so it wins
/// regardless of when the game initiates the connection.
/// </summary>
[BepInPlugin("blackice.redirect", "BlackIce Server Redirect", "0.1.0")]
public sealed class RedirectPlugin : BaseUnityPlugin
{
    internal static ConfigEntry<string> ServerAddress = null!;
    internal static ConfigEntry<int> ServerPort = null!;
    internal static BepInEx.Logging.ManualLogSource Log = null!;

    private void Awake()
    {
        Log = Logger;
        ServerAddress = Config.Bind("Server", "ServerAddress", "127.0.0.1",
            "Custom BlackIce server address (the Name Server). Like WoW's realmlist — point this at your server.");
        ServerPort = Config.Bind("Server", "Port", 5058, "Name Server UDP port.");
        new Harmony("blackice.redirect").PatchAll();
        Logger.LogInfo($"BlackIce redirect armed -> {ServerAddress.Value}:{ServerPort.Value}");
    }
}

/// <summary>Overrides the Photon app settings to point at our server right before connect.</summary>
[HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.ConnectUsingSettings), new System.Type[0])]
internal static class ConnectRedirectPatch
{
    static void Prefix()
    {
        var settings = PhotonNetwork.PhotonServerSettings.AppSettings;
        settings.Server = RedirectPlugin.ServerAddress.Value;
        settings.Port = RedirectPlugin.ServerPort.Value;
        settings.UseNameServer = true;   // client walks Name -> Master -> Game against us
        // Keep the game's FixedRegion (already set, e.g. "us"): a non-empty fixed region makes the
        // client authenticate directly on our Name Server instead of asking for a region list.
        if (string.IsNullOrEmpty(settings.FixedRegion)) settings.FixedRegion = "us";
        RedirectPlugin.Log.LogInfo($"Redirecting Photon connect -> {settings.Server}:{settings.Port} (region '{settings.FixedRegion}')");
    }
}
