# BlackIce.Server — project guide for Claude

A clean-room, from-scratch server that speaks the Black Ice game's Photon protocol, so
the game can run independent of Photon Cloud (eventual goal: server authority + custom
anticheat). Precedents: OpenRA, TrinityCore. **Intended to be open-sourced (GPLv3).**

## ⛔ The one rule that matters most: never leak copyrighted/secret material

This repo is public-facing and must contain **only original code + our own protocol
documentation**. The following are game-derived or secret and must **never** be committed
(they are gitignored, and a PreToolUse leak-guard hook blocks force-adds / explicit refs):

- `decompiled/`, `captures/`, `asset-dumps/`, `third-party/`, anything under `Black Ice_Data/`
- Game binaries: `*.dll`, `*.exe`, `*.pdb`
- Secrets/runtime: `oplog.jsonl` (carries Steam tokens), `steam-ticket-spike.log` (real
  SteamID + ticket bytes), `*.db*` (dev databases), `blackice.server.json` (generated config)

If you genuinely need to reference the game's behavior, describe it in `docs/` in our own
words — do not paste decompiled source. See `NOTICE` and `SECURITY.md`.

## Build & test

```bash
dotnet build server/BlackIce.Server.sln          # the server
dotnet test  server/BlackIce.Server.sln           # ~85 xUnit tests
dotnet build BlackIce.sln                          # everything (server + plugins + tools)
```

- **Tests use the real `Photon3Unity3D.dll` as an interop oracle** (test-only reference +
  reflection for internal types). Codec/transport changes must round-trip against it.
- Client plugins (`plugins/`) target `netstandard2.0` (BepInEx/Mono) and auto-deploy into
  the game's `BepInEx/plugins` on build.

## How work is organized

Development is **phased**, and **each phase = its own spec → plan → build** under
`docs/superpowers/`. Don't jump to code on a new phase without a spec/plan.

- Phase 0 Recon ✅ · Phase 1 Connect flow ✅ · Server Platform SP1 (accounts) ✅ ·
  SP2 (realms) ✅ · Phase 2 relay · Phase 3 server authority · Phase 4 anticheat.
- Protocol facts live in `docs/protocol/`; design directives in `docs/design/`.

## Conventions

- **Conventional commits**, scoped: `feat(lb):`, `feat(data):`, `test(...):`, `fix(...):`,
  `docs:`, `security:`. (`lb` = LoadBalancing, `data` = EF Core layer.)
- Professional naming/comments/structure throughout — this is community-facing code.
- EF Core data layer currently uses `EnsureCreated` (migrations deferred); SQLite default,
  Pomelo MySQL swappable via `blackice.server.json`. Use `/ef-migration` when adopting migrations.

## Gotchas

- `curl` here needs `--ssl-no-revoke` (schannel revocation failure). NuGet/dotnet restore unaffected.
- **SECURITY GATE:** the client mod currently sends the SteamID read from the registry,
  which is **spoofable** — so privileged/admin actions stay console-only. Networked admin
  must wait on Steam **game-server ticket** validation. The client-side ticket spike has
  **passed** (a BepInEx plugin can mint a Steam-validated ticket); server-side validation
  (`BeginAuthSession`) is the remaining piece.

## Claude Code automations in this repo (`.claude/`)

- **Hooks** (`settings.json` + `hooks/`): PreToolUse leak-guard; PostToolUse `dotnet format`.
- **Subagents** (`agents/`): `security-reviewer`, `photon-interop-reviewer`.
- **Skill** (`skills/ef-migration/`): user-invoked EF Core migration helper.
- **MCP** (`.mcp.json`): `blackice-db` SQLite browser (requires `uv` — see file).

> These activate only when Claude is launched from this repo directory (`blackice-re/`).
