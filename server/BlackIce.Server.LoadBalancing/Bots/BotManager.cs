using System.Collections.Generic;
using BlackIce.Photon;
using BlackIce.Server.Core;

namespace BlackIce.Server.LoadBalancing.Bots;

/// <summary>
/// Owns the live bots: reserves non-colliding actor numbers, spawns them into a room session, and
/// drives their per-tick movement. Bot actors start at <see cref="BotActorBase"/> so their
/// viewID blocks (actor*1000) never overlap real players' blocks. Tick is driven off the host's
/// 1 Hz maintenance loop (single-threaded), so no locking is needed here.
/// </summary>
public sealed class BotManager
{
    public const int BotActorBase = 10000;
    private const byte EvPosition = 201, PData = 245;

    private int _nextBotActor = BotActorBase;
    private readonly List<(PlayerBot bot, RoomSession session, IBotBehavior behavior)> _bots = new();

    public PlayerBot Spawn(RoomSession session, BotIdentity identity, IBotBehavior? behavior = null)
    {
        var bot = new PlayerBot(_nextBotActor++, identity);
        bot.Spawn(session);
        _bots.Add((bot, session, behavior ?? new WanderBehavior(0, 0)));
        return bot;
    }

    /// <summary>Advances every bot one step and relays its new position (event 201, unreliable).</summary>
    public void Tick()
    {
        foreach (var (bot, session, behavior) in _bots)
        {
            var p = behavior.Tick();
            session.RelayFrom(bot.Actor, BuildPositionEvent(bot.ViewId, p), unreliable: true);
        }
    }

    /// <summary>
    /// Builds the event-201 serialize batch moving the bot's avatar. The client's
    /// NetworkSyncPosition.OnPhotonSerializeView reads a FIXED field set with no bounds check, so the
    /// per-view stream must carry every field the Player prefab's enabled body component emits or the
    /// receiver throws IndexOutOfRangeException. After pos(Vec3) and rot(Quat) the confirmed body fields
    /// (from a captured real event-201) are the SyncPlayerHP triplet + SyncHead pitch:
    /// damageTaken, maxHealth, tempHP, headPitch. We carry only that single body component (no buff/weapon
    /// tail) so we never invent fields we cannot verify.
    /// </summary>
    private static EventData BuildPositionEvent(int viewId, BotPositionUpdate p)
    {
        var pos = PunVector3(p.X, p.Y, p.Z);
        var rot = PunQuaternion(0, 0, 0, 1);
        var view = new object[] { viewId, false, null!, pos, rot, 0f, 200f, 0f, 0f };
        var batch = new object[] { System.Environment.TickCount, null!, view };
        return new EventData(EvPosition, new() { { PData, batch } });
    }

    private static PhotonCustomData PunVector3(float x, float y, float z)
    {
        var b = new byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(0), x);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(4), y);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(8), z);
        return new PhotonCustomData(86, b);
    }

    private static PhotonCustomData PunQuaternion(float x, float y, float z, float w)
    {
        var b = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(0), x);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(4), y);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(8), z);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(12), w);
        return new PhotonCustomData(81, b);
    }
}
