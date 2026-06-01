# Server Platform SP3 — Steam Ticket Validation / Verified Identity — Design Spec

**Status:** Draft design (2026-06-01). Closes the SECURITY GATE in `CLAUDE.md` and the
"tracked for SP3" note in `NameServerHandler.Authenticate`.
**Scope decision:** This spec covers server-side validation of a Steam auth ticket and the
binding of a *proven* SteamID to the session, plus gating networked privileged actions on that
proof. It does **not** cover the client-side ticket minting (already prototyped — the
"steam-ticket-spike" passed) nor any new admin commands (those exist; they're console-only today).

> Provenance note: this design is written from official Steamworks documentation and public
> open-source patterns only (see §10). No game-derived or decompiled material. The publisher Web
> API key, real tickets, and real SteamIDs are secrets — see §8.

---

## 1. Problem & goal

Identity today is **asserted, not proven**. In `NameServerHandler.Authenticate`, the server reads
the SteamID the client sends as the Photon `UserId` (param 225), format-checks it
(`SteamId.IsValidIndividual` — defense-in-depth, *not* anti-spoofing), and mints an `AuthToken`
(HMAC-SHA256 over that SteamID, param 221) that the Master and Game servers re-validate downstream.
Because the originating SteamID comes straight from the client registry, **any client can claim any
SteamID**. `SteamId.cs` and `SECURITY.md` both say plainly: *do not gate privilege on a
network-asserted SteamID.* That is why all privileged/admin actions are **console-only**.

**Goal:** prove the player owns the SteamID before the Name Server mints a token over it, so that:

1. The minted `AuthToken` carries a **Steam-verified** identity (downstream Master/Game flow is
   unchanged — it already trusts the token).
2. A new **trust level** on the session/token lets networked privileged actions be gated on
   *proven* identity, retiring the "console-only" restriction for verified admins.
3. Legitimate players who don't (or can't) present a ticket are unaffected for ordinary,
   client-authoritative play — they simply never reach a privileged trust level (fail-open for
   play, fail-closed for privilege).

## 2. Why the Steam **Web API** path (key decision)

Two ways to validate a Steam auth ticket server-side:

| | Native `ISteamGameServer::BeginAuthSession` | **Web API `ISteamUserAuth/AuthenticateUserTicket`** |
|---|---|---|
| Dependency | proprietary `steam_api`/`steamclient` native binaries + P/Invoke; must init the in-process Steam game-server runtime | none — a single HTTPS GET with `HttpClient` |
| Clean-room fit | **poor** — pulls Valve binaries into a pure-managed, GPLv3, "no game/proprietary binaries" repo | **good** — stays pure-managed |
| Testability | needs a live Steam client/runtime | trivially fakeable behind an interface |
| Result delivery | async `ValidateAuthTicketResponse_t` callback | synchronous HTTP response (JSON) |

**Decision: Web API path.** It keeps the server pure-managed and clean-room (no `steam_api64.dll`),
needs no new third-party dependency (`HttpClient` + `System.Text.Json`), and is testable without
Steam. The native path is recorded as a rejected alternative (§9).

This mirrors how Photon Cloud's own "custom authentication" provider would validate the ticket —
we slot validation into the same place (Name Server `Authenticate`) that Photon Cloud's custom-auth
hook occupies.

## 3. Flow

```
Client (BepInEx plugin)                Name Server                         Steam Web API
─────────────────────                  ───────────                         ─────────────
GetAuthTicketForWebApi("blackice")
  -> hex ticket
OpAuthenticate { ticket, ... }  ─────► HandleAuthenticate
                                        │ extract hex ticket param
                                        │ ISteamTicketValidator.Validate ─► GET .../AuthenticateUserTicket
                                        │                                    ?key=<pub>&appid=<id>
                                        │                                    &ticket=<hex>&identity=blackice
                                        │ ◄──────────────────────────────── { result:"OK", steamid, ... }
                                        │ verified steamId  (+ ban / VAC checks)
                                        │ mint AuthToken over VERIFIED steamId, trust=Verified
                              ◄──────── rc=0 { Secret=token, UserId=verified-steamId, Address=master }
(unchanged downstream: Master/Game validate the token as today)
```

Key property: **only the Name Server changes.** The Master/Game `Authenticate` already recover the
SteamID from the token (`AuthToken.Validate`); they keep trusting the token, which now means they
transitively trust a Steam-verified identity.

## 4. Key design decisions

1. **Validate at the single mint point (Name Server).** The token is the trust anchor for the whole
   connect chain; proving identity once, where the token is minted, is sufficient and avoids
   re-validating a single-use ticket on every hop.
2. **Ticket is optional → two trust levels, not accept/reject.** Connect succeeds either way:
   - **Verified** — a valid ticket was presented and validated; token is minted `trust=Verified`.
   - **Unverified** — no ticket (or LAN/anonymous mode); plays normally, never privileged.
   Privileged networked actions require `trust=Verified` **and** the existing permission level
   (`PlayerLevel`/`CommandRegistry`). Fail-open for play, fail-closed for privilege. (Operators who
   want a locked-down realm can flip a config switch to require Verified for *all* connects — §6.)
3. **Trust level travels in the token, signed.** Extend `AuthToken` so the HMAC covers an identity
   *and* a trust tag (e.g. `"<steamId>:V"` vs `"<steamId>:U"`); the existing
   `FixedTimeEquals` check makes the trust tag unforgeable. Downstream handlers read trust from the
   validated token — no new cross-server state. (Back-compat: the current `Mint(userId)` becomes the
   `Unverified` form.)
4. **Validation is injected behind `ISteamTicketValidator`.** The Name Server depends on the
   interface, not on `HttpClient`/Steam. Production impl calls the Web API; tests use a fake. This is
   the only way to unit-test the gate without Steam, and it keeps the network call out of the hot
   handler for mocking.
5. **Validate-once, bind-for-session.** The online check is single-use (Valve rejects ticket reuse),
   so we validate exactly once at the Name Server and rely on the signed token thereafter — never
   re-submit the same ticket.
6. **Fail-closed on validator error for *privilege only*.** If the Web API call errors/time-outs,
   the session is treated as **Unverified** (not granted privilege), but ordinary connect still
   succeeds (unless strict mode requires Verified). A Steam outage must not lock legitimate players
   out of normal play, but must never *grant* privilege on an unproven identity.

## 5. The Web API call (`SteamWebApiTicketValidator`)

```
GET https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/
    ?key=<PUBLISHER_WEB_API_KEY>      # secret, server-side only
    &appid=<APPID>
    &ticket=<HEX of the ticket bytes> # hex, NOT base64
    &identity=<must byte-match the client's GetAuthTicketForWebApi identity>
```

Success response (parse with `System.Text.Json`):

```json
{ "response": { "params": {
  "result": "OK",
  "steamid": "7656119…",        // bind THIS as the proven identity
  "ownersteamid": "7656119…",   // differs under Family Sharing
  "vacbanned": false,
  "publisherbanned": false
}}}
```

Rules:
- Trust the identity only when `result == "OK"`.
- Reject (→ Unverified) when `vacbanned` or `publisherbanned` is true (config-toggleable for VAC).
- Optionally enforce `steamid == ownersteamid` to refuse Family-Sharing borrowers from privilege.
- Map non-OK / HTTP-error / timeout → Unverified (privilege denied), per §4.6.

Implementation notes: SDK ≥ 1.57 on the client (`GetAuthTicketForWebApi`, version-2 identity
semantics); ticket is **hex**; `identity` must match on both ends exactly (the single most common
integration failure). One pooled `HttpClient`; short timeout; `api.steampowered.com` is the
rate-limited fallback host.

## 6. Configuration

Extend `blackice.server.json` `Server` (or a new `Steam` section) + `BLACKICE_` overrides, validated
at startup like the rest (`docs/configuration.md`):

| Setting | Env override | Notes |
|---|---|---|
| `Steam:Enabled` | `BLACKICE_Steam__Enabled` | master switch; off → today's behavior (asserted SteamID), with a startup warning |
| `Steam:WebApiKey` | `BLACKICE_Steam__WebApiKey` | **publisher** key — SECRET, never committed (§8) |
| `Steam:AppId` | `BLACKICE_Steam__AppId` | Black Ice AppID |
| `Steam:Identity` | `BLACKICE_Steam__Identity` | must equal the client's ticket identity string |
| `Steam:RequireForPrivilege` | … | default **true**: privilege needs Verified |
| `Steam:RequireForAllConnects` | … | default **false**: strict realms reject Unverified entirely |
| `Steam:RejectVacBanned` | … | default true |

Startup validation: if `Enabled` and `WebApiKey` is empty → hard error (same posture as the empty
token-secret check). `RequireForAllConnects` with `Enabled=false` → hard error (contradiction).
LAN/anonymous mode (`allowAnonymousLan`, loopback/private range) keeps bypassing Steam as today.

## 7. Where the ticket rides on the wire

The client sends the hex ticket as an `OpAuthenticate` parameter. The exact Photon param code (the
custom-authentication data param — candidates 214/216/224 in Photon's custom-auth convention) must
be **confirmed against the client plugin and `docs/protocol/` connect-flow map** before coding, and
recorded as a new constant in `PhotonCodes.Param`. This spec does not invent a code; it flags the
confirmation as the first implementation step.

## 8. Security & leak considerations

- **Publisher Web API key is a top-tier secret** — it's the asymmetry that fixes spoofing (clients
  can't forge a ticket that validates; the validating key never leaves the server). Treat it like the
  DB password: `blackice.server.json` is **already gitignored** (`CLAUDE.md` leak guard); the key
  lives there or in `BLACKICE_Steam__WebApiKey`. Never commit it; never log it; IP-lock it in the
  Steamworks admin if egress IP is stable.
- **Ticket bytes and real SteamIDs are sensitive** (the existing `steam-ticket-spike.log` is
  gitignored for exactly this reason). Do not log raw tickets; log only the verified SteamID and
  result, at debug.
- **Replay / single-use:** rely on Valve's single-use online check + our signed session token; never
  cache-and-replay a ticket.
- **Trust-tag forgery:** prevented by HMAC over `identity:trust` (§4.3) — a client cannot upgrade
  Unverified→Verified without the server secret.
- **Downstream unchanged = small blast radius:** Master/Game keep doing `AuthToken.Validate`; the
  only new trust surface is one HTTPS call at the Name Server.
- This change should pass a `security-reviewer` subagent review before merge (touches auth/identity).

## 9. Rejected / deferred alternatives

- **Native `BeginAuthSession` (Steamworks.NET / Facepunch.Steamworks, both MIT):** rejected for the
  clean-room/pure-managed reasons in §2. Revisit only if the Web API path proves insufficient
  (e.g. we need offline validation of the signed ownership-ticket portion).
- **SteamKit2 (LGPL-2.1):** not applicable — it reimplements the Steam *client* (logging in *as* a
  user); it has no "validate a ticket I received" server entry point. Recorded so we don't re-evaluate.
- **Per-hop re-validation:** rejected — ticket is single-use; the signed token already carries trust.

## 10. Implementation outline (a full plan follows design approval)

1. **Confirm the wire param** for the ticket (§7); add the `PhotonCodes.Param` constant.
2. **`ISteamTicketValidator`** + `SteamWebApiTicketValidator` (HttpClient + System.Text.Json) in
   LoadBalancing; record type `TicketResult(bool Ok, string? SteamId, bool VacBanned, bool PublisherBanned, string? OwnerSteamId)`.
3. **Extend `AuthToken`** to sign `identity + trust` (back-compat: existing form = Unverified).
4. **`NameServerHandler.Authenticate`:** when `Steam:Enabled`, extract ticket → `Validate` →
   bind verified SteamID → mint `Verified` token; on absence/failure → Unverified per policy; honor
   `RequireForAllConnects`.
5. **Gate:** thread the token's trust level onto `PeerConnection`; require `Verified` for networked
   privileged ops (alongside `CommandRegistry`/`PlayerLevel`). Lift "console-only" for verified admins.
6. **Config + startup validation** (§6); docs update (`configuration.md`, `SECURITY.md`).
7. **Tests** (§11).
8. **Reviews:** `security-reviewer` + `photon-interop-reviewer` (param/codec touch).

## 11. Testing strategy

- **No real Steam in CI.** All tests inject a fake `ISteamTicketValidator`.
- `SteamWebApiTicketValidator` parsing: feed canned OK / non-OK / VAC-banned / malformed JSON and
  assert the mapped `TicketResult` (drive the `HttpClient` with a fake `HttpMessageHandler`; never
  hit the network).
- `AuthToken` round-trip: Verified/Unverified tags validate; a flipped tag fails `Validate`.
- `NameServerHandler`: valid ticket → `Verified` token + verified SteamID; missing ticket →
  Unverified; banned → rc=-3; `RequireForAllConnects` rejects Unverified; LAN bypass still works.
- Gate: an Unverified session is denied a privileged op; a Verified+permitted session is allowed.
- Negative/leak: raw ticket bytes never appear in logs.

## 12. Out of scope

Client-side ticket minting (done); new admin commands (exist); anticheat/authority (Phase 3); VAC
ban *appeals*/sync beyond the per-ticket flags; multi-AppID. These are separate tracks.
