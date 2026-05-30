using System.Collections.Generic;
using BlackIce.Photon;
using BlackIce.Server.Data;
using BlackIce.Server.LoadBalancing;
using Xunit;

namespace BlackIce.Server.Tests;

public class ChatCommandTests
{
    private static OperationRequest ChatRpc(string text) => new(253, new()
    {
        { 244, (byte)200 },   // PUN RPC event code
        { 245, new Dictionary<object, object>
            {
                { (byte)3, "ReceiveChatMessage" },
                { (byte)4, new object[] { text } },
            } },
    });

    private static GameServerHandler Handler(out TestDb db)
    {
        db = new TestDb();
        db.Context.Realms.Add(new Realm { Name = "co-op", IsEnabled = true });
        db.Context.SaveChanges();
        var motd = new MotdService(db.Context);
        motd.SetGlobal("Daily news: the Ice grows.");
        return new GameServerHandler("s", new RoomRegistry(), allowAnonymousLan: true,
                                     realms: new RealmService(db.Context), motd: motd);
    }

    [Fact]
    public void Slash_motd_returns_server_message_with_resolved_text()
    {
        var h = Handler(out var db);
        using (db)
        {
            var ev = h.TryHandleChatCommand("co-op", ChatRpc("/motd"));
            Assert.NotNull(ev);
            Assert.Equal(199, ev!.Code);
            Assert.Equal("Daily news: the Ice grows.", ev.Parameters[245]);
        }
    }

    [Fact]
    public void Unknown_slash_command_returns_hint()
    {
        var h = Handler(out var db);
        using (db)
        {
            var ev = h.TryHandleChatCommand("co-op", ChatRpc("/frobnicate"));
            Assert.NotNull(ev);
            Assert.Contains("Unknown command", (string)ev!.Parameters[245]);
        }
    }

    [Fact]
    public void Plain_chat_is_not_a_command()
    {
        var h = Handler(out var db);
        using (db)
            Assert.Null(h.TryHandleChatCommand("co-op", ChatRpc("hello everyone")));
    }

    [Fact]
    public void Non_chat_raise_event_is_ignored()
    {
        var h = Handler(out var db);
        using (db)
        {
            var notChat = new OperationRequest(253, new() { { 244, (byte)201 }, { 245, new Dictionary<object, object>() } });
            Assert.Null(h.TryHandleChatCommand("co-op", notChat));
        }
    }
}
