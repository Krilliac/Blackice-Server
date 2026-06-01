# Reusable open-source references

A curated, license-flagged map of public projects and write-ups worth borrowing from, per area.
Recorded in our own words (no game-derived/decompiled material). **License direction is one-way:**
MIT / BSD / Apache-2.0 / LGPL / GPLv3 can flow *into* this GPLv3 repo (preserve NOTICE for
Apache/LGPL); **GPLv2-only** (TrinityCore, AzerothCore core, WowPacketParser, ServUO) and
**proprietary** (FishNet, Photon Fusion) are **ideas-only** — fine for a clean-room reimplementation,
not for copying source.

## Photon wire format (codec + transport oracles without the DLL)

> Caveat: our `GpType` uses the newer **GpBinary V2 / "Protocol18"** type scheme (Boolean=2, Byte=3,
> Short=4, with zero/inline-int optimisations). References that document the older **Protocol16**
> letter-codes (Boolean=`111 'o'`) are valid for *structure* (parameter-table/message framing) but
> are **not** a byte-level type-code oracle for us. The eNet **command** codes do match us exactly.

- **AltspaceVR/wireshark-photon-dissector** — GPL-3.0 (same as us → citable directly). Best
  clean-room description of Photon's eNet command layer: 12-byte protocol header (PeerID, CRC flag,
  command count, timestamp, challenge), 12-byte command headers, command codes
  `SendReliable=6 / SendUnreliable=7 / SendFragment=8`, and full fragment fields (start-seq, count,
  number, total length, offset) — the spec for the fragment reassembly we haven't built yet.
- **mazurwiktor/photon_decode** (Rust, MIT/Apache) — independent confirmation of fragment reassembly
  + message-type discriminators (2=Request, 3=Response, 4=Event).
- **0blu/PhotonPackageParser** (C#, MIT) — Protocol16 parameter-table/message *structure* reference;
  its `Protocol16.Tests` layout is a model for a DLL-free codec round-trip test.
- **albion-packet-hooking/AlbionPacketHandler** (C#, GPL-3.0) — `[EventHandler]`/`[OperationHandler]`
  attribute dispatch pattern over a Photon parser.

## Reliable-UDP transport (validate our eNet-style layer)

- **lsalzman/enet** (C, MIT) + its DeepWiki — authoritative reference for retransmit, RTT→RTO
  (mean+variance+backoff), and window/throttle congestion logic. (Photon's eNet is a *modified*
  variant — use lsalzman for mechanics, the dissector above for Photon-specific header/code deltas.)
- **Molth/enet-csharp** and **ikpil/ENet.NET** (both pure-C#, MIT) — read alongside lsalzman to
  validate our sequencing/fragment/ack logic in our own language.
- **LiteNetLib** (MIT) — borrow: 5-mode channel taxonomy (Reliable-Ordered/Unordered/Sequenced,
  Unreliable-Sequenced, Unreliable), MTU discovery, small-packet merging, and a loss/latency
  **simulation test harness**.
- **KCP** (MIT) — adopt **fast-retransmit-on-duplicate-ACK** to cut latency under loss.

## Server architecture (master/game split, dispatch, hosting)

- **OpenRA** (C#, **GPLv3 — copy-compatible**) — our best same-language/same-license reference for the
  relay stage: single-threaded `BlockingCollection<IServerEvent>` event loop, frame-wrapped
  broadcast-to-all-except-sender, `Task.Run`+callback async auth (the pattern for non-blocking Steam
  validation), and game-state-hash desync detection (doubles as a Phase-3 cheat signal).
- **TrinityCore / WowPacketParser** (GPLv2 — ideas only) — central opcode→handler table where each
  entry carries a **required session status** + processing mode; attribute-registered handlers.
  Directly informs our `SessionStatus`-gated dispatch and the Steam trust-level gate.
- **OpenCoreMMO** (C#/.NET 9, GPLv3) — modern emulator with multi-provider EF persistence + xUnit;
  mirrors our stack, freely referenceable.
- **AzerothCore module system** (idea) — register anticheat/relay/game features as self-contained
  server-side modules (handlers + migrations + config) rather than core edits.

## Server authority (Phase 3)

- **Mirror** (MIT) — borrow snapshot-interpolation buffer + interest-management code; transport
  abstraction model. **Avoid FishNet / Photon Fusion code** (proprietary) — concepts only.
- **Gabriel Gambetta** "Client-Side Prediction & Server Reconciliation" and **Gaffer On Games**
  (Snapshot Interpolation / State Synchronization / Deterministic Lockstep) — implement in our own
  words: input sequence numbers, fixed-timestep authoritative re-simulation, client interpolation
  ~100ms behind. Misprediction magnitude is itself an anticheat signal.

## Anticheat (Phase 4)

- **azerothcore/mod-anticheat** (**MIT** — prefer over TrinityCore's GPLv2 version) — port the math:
  speed (`distance·1000/Δt > allowed·1.05`), teleport-to-plane Z-delta, climb-angle, fly-without-aura,
  all with configurable tolerances and per-hack report counters.
- **Cheapest/highest-signal first** (TrinityCore pattern, near-zero false positives): movement-flag
  contradiction checks + capability-without-grant rejection; teleport ACK state machine.
- **NoCheatPlus** (GPLv3) — violation-level accumulator with decay; honest about false-positive risk.
- **Posture:** log-first, threshold-then-act (matches our console-only-until-proven stance). Use
  latency median/stddev outlier filtering before speed math so lag spikes don't false-positive.

## Drop-in .NET pieces (no licensing friction)

- **System.Threading.RateLimiting** (first-party) — `PartitionedRateLimiter` keyed per
  connection/SteamID + a global guard; `TokenBucketRateLimiter` to allow join bursts. Flood/DoS
  hardening for listeners. Push SYN/connection-flood to the edge (iptables/SYN cookies).
- **CsCheck** (Apache-2.0; used by Orleans for serialization tests) — optional future upgrade for the
  codec round-trip property tests (we currently use a dependency-free seeded generator —
  `GpBinaryRoundTripTests`).
- **SharpFuzz** (MIT) — coverage-guided fuzz target feeding random bytes at the decoder; high value
  given we parse untrusted client packets (needs an AFL/libFuzzer toolchain, so it runs outside
  `dotnet test`).

## Steam server-side auth

See **`docs/superpowers/specs/2026-06-01-sp3-steam-ticket-validation-design.md`** — the decision is
the Steam **Web API** path (`ISteamUserAuth/AuthenticateUserTicket`), pure-managed, no native SDK.
