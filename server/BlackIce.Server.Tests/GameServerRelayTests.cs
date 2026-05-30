using System.Collections.Generic;
using System.Net;
using BlackIce.Photon;
using BlackIce.Server.Core;
using BlackIce.Server.Data;
using BlackIce.Server.LoadBalancing;
using Xunit;

namespace BlackIce.Server.Tests;

public class GameServerRelayTests
{
    private static (GameServerHandler h, RoomRegistry reg, TestDb db) NewHandler()
    {
        var db = new TestDb();
        db.Context.Realms.Add(new Realm { Name = "co-op", IsEnabled = true });
        db.Context.SaveChanges();
        var reg = new RoomRegistry();
        var h = new GameServerHandler("s", reg, allowAnonymousLan: true, realms: new RealmService(db.Context));
        return (h, reg, db);
    }

    private static PeerConnection Peer(out List<EventData> raised)
    {
        var captured = new List<EventData>();
        raised = captured;
        var p = new PeerConnection("GameServer", new IPEndPoint(IPAddress.Loopback, 0), new Null(), (_, _) => { });
        p.OnRaised = captured.Add;
        return p;
    }
    private sealed class Null : IOperationHandler
    {
        public void OnConnect(PeerConnection peer) { }
        public void OnOperationRequest(PeerConnection peer, OperationRequest request) { }
        public void OnDisconnect(PeerConnection peer) { }
    }

    private static OperationRequest Join() => new(226, new() { { 255, "co-op" } });
    private static OperationRequest GameplayRpc() => new(253, new()
    {
        { 244, (byte)200 },
        { 245, new Dictionary<object, object> { { (byte)0, 2001 }, { (byte)5, (byte)73 },
                 { (byte)4, new object[] { 1.0 } } } },
    });

    [Fact]
    public void Gameplay_rpc_from_one_actor_is_relayed_to_the_other()
    {
        var (h, _, db) = NewHandler();
        using (db)
        {
            var a = Peer(out var aRaised); var b = Peer(out var bRaised);
            h.OnOperationRequest(a, Join());
            h.OnOperationRequest(b, Join());
            aRaised.Clear(); bRaised.Clear();

            h.OnOperationRequest(a, GameplayRpc());

            Assert.Empty(aRaised);
            Assert.Single(bRaised);
            Assert.Equal(200, bRaised[0].Code);
        }
    }

    [Fact]
    public void Slash_motd_is_still_intercepted_not_relayed()
    {
        var (h, _, db) = NewHandler();
        using (db)
        {
            var a = Peer(out var aRaised); var b = Peer(out var bRaised);
            h.OnOperationRequest(a, Join());
            h.OnOperationRequest(b, Join());
            aRaised.Clear(); bRaised.Clear();

            var motd = new OperationRequest(253, new()
            {
                { 244, (byte)200 },
                { 245, new Dictionary<object, object> { { (byte)5, (byte)7 },
                         { (byte)4, new object[] { "/motd" } } } },
            });
            h.OnOperationRequest(a, motd);

            Assert.Single(aRaised);
            Assert.Equal(199, aRaised[0].Code);
            Assert.Empty(bRaised);
        }
    }

    [Fact]
    public void A_disconnected_actor_no_longer_receives_relayed_events()
    {
        var (h, _, db) = NewHandler();
        using (db)
        {
            var a = Peer(out _); var b = Peer(out var bRaised);
            h.OnOperationRequest(a, Join());
            h.OnOperationRequest(b, Join());
            h.OnDisconnect(b);
            bRaised.Clear();

            h.OnOperationRequest(a, GameplayRpc());
            Assert.Empty(bRaised);
        }
    }

    [Fact]
    public void Existing_actors_are_notified_when_a_new_actor_joins()
    {
        var (h, _, db) = NewHandler();
        using (db)
        {
            var a = Peer(out var aRaised);
            h.OnOperationRequest(a, Join());
            aRaised.Clear();

            var b = Peer(out _);
            h.OnOperationRequest(b, Join());

            Assert.Contains(aRaised, e => e.Code == 255
                && e.Parameters.TryGetValue(254, out var nr) && nr is int i && i == 2);
        }
    }

    [Fact]
    public void Remaining_actors_are_notified_when_an_actor_leaves()
    {
        var (h, _, db) = NewHandler();
        using (db)
        {
            var a = Peer(out var aRaised);
            var b = Peer(out _);
            h.OnOperationRequest(a, Join());
            h.OnOperationRequest(b, Join());
            aRaised.Clear();

            h.OnDisconnect(b);

            Assert.Contains(aRaised, e => e.Code == 254
                && e.Parameters.TryGetValue(254, out var nr) && nr is int i && i == 2);
        }
    }
}
