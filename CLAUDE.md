# BlackIce.Server — project guide for Claude

A clean-room, from-scratch server that speaks the Black Ice game's Photon protocol, so
the game can run independent of Photon Cloud (eventual goal: server authority + custom
anticheat). Precedents: OpenRA, TrinityCore. **Intended to be open-sourced (GPLv3).**

## ⛔ The one rule that matters most: never leak copyrighted/secret material

This repo is public-facing and must contain **only original code + our own protocol
documentation**. The following are game-derived or secret and must **never** be committed
(they are gitignored — `.gitignore` is the leak guard, so a normal `git add`/`-A` will not
stage them; never `git add -f` a path below, as that deliberately bypasses the guard):

- `decompiled/`, `captures/`, `asset-dumps/`, `third-party/`, anything under `Black Ice_Data/`
- Game binaries: `*.dll`, `*.exe`, `*.pdb`
- Secrets/runtime: `oplog.jsonl` (carries Steam tokens), `steam-ticket-spike.log` (real
  SteamID + ticket bytes), `*.db*` (dev databases), `blackice.server.json` (generated config)

If you genuinely need to reference the game's behavior, describe it in `docs/` in our own
words — do not paste decompiled source. See `NOTICE` and `SECURITY.md`.

## Build & test

```bash
dotnet build server/BlackIce.Server.sln          # the server
dotnet test  server/BlackIce.Server.sln           # xUnit tests (191 run without the game DLL)
dotnet build BlackIce.sln                          # everything (server + plugins + tools)
```

- **The `Photon3Unity3D.dll` interop oracle is now optional.** It cross-checks our clean-room codec +
  DH/AES against the real Photon implementation, so keep using it locally — but it no longer blocks
  the build. The reference is conditional on the DLL existing (override its path with
  `-p:PhotonOracleDll=<path>` or `PHOTON_ORACLE_DLL`); without it, the oracle-only tests are skipped
  and the rest of the suite (transport, eNet sequencing, decode, server, data) still builds and runs.
  Codec/transport changes must still round-trip against the oracle locally before merge.
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
- EF Core data layer uses **migrations** on SQLite (committed under `BlackIce.Server.Data/Migrations`,
  applied on startup via `AutoMigrate`); MySQL still uses `EnsureCreated` until provider-specific
  migrations are added. SQLite default, Pomelo MySQL swappable. Add migrations with
  `dotnet ef migrations add <Name> --project server/BlackIce.Server.Data`.
- The host runs on the **.NET Generic Host** (DI + lifetime); listeners and the admin console are
  `IHostedService`s. Config is `blackice.server.json` layered under `BLACKICE_*` env overrides and
  validated at startup — see `docs/configuration.md`. Photon wire codes live in `PhotonCodes`.

## Gotchas

- `curl` here needs `--ssl-no-revoke` (schannel revocation failure). NuGet/dotnet restore unaffected.
- **SECURITY GATE:** the client mod currently sends the SteamID read from the registry,
  which is **spoofable** — so privileged/admin actions stay console-only. Networked admin
  must wait on Steam **game-server ticket** validation. The client-side ticket spike has
  **passed** (a BepInEx plugin can mint a Steam-validated ticket); server-side validation
  (`BeginAuthSession`) is the remaining piece.

## Claude Code automations in this repo (`.claude/`)

- **Hooks** (`settings.json` + `hooks/`): PostToolUse `dotnet format`. (Leak protection is
  `.gitignore` alone — the old PreToolUse leak-guard hook was removed as too false-positive-prone.)
- **Subagents** (`agents/`): `security-reviewer`, `photon-interop-reviewer`.
- **Skill** (`skills/ef-migration/`): user-invoked EF Core migration helper.
- **MCP** (`.mcp.json`): `blackice-db` SQLite browser (requires `uv` — see file).

> These activate only when Claude is launched from this repo directory (`blackice-re/`).

## Parallel sessions

If more than one Claude Code session may touch this repo at once, follow **`CLAUDE_PARALLEL.md`**:
run `tools/parallel/status.sh`, then `tools/parallel/claim.sh <area> "<files>" "<desc>"` before editing.
Single session? You can skip it, but a `status.sh` at start is cheap courtesy. `AGENTS.md` is the
one-screen session bootstrap.

## Anti-bloat — sanity, not sacrifice

AI-assisted development has a structural bias toward complexity: features "just in case," helpers for a
single use, systems built but never wired in. Keep code clean without stripping legitimate readability.
Before writing code, run this checklist:

1. **Does this already exist?** Search before writing (codec helpers, interceptor base types, packet parsers).
2. **Will this be called?** If you can't name the caller, don't write it.
3. **Can existing code do this with a small change?** Prefer editing over adding.
4. **One-time use?** Inline it — no helper, no new class.
5. **Future-proofing?** Stop. Write only what's needed today.
6. **Dead code?** Delete it — git history exists. Don't comment it out.
7. **Built but not wired in?** Wire it or delete it (see below).

Thresholds are *pause-and-think* signals, not hard limits: a `.cs` past ~500 lines, a method past ~60,
a class past ~15 public members → ask "is this doing one job?" A clean long file is fine; a cryptic
short one is not. **Never sacrifice readability to hit a line count** — descriptive names
(`punRpcViewId` > `v`), `// why` comments, one statement per line.

**STUB/GAP markers** (greppable inventory — `git grep -nE "// (STUB|GAP):"`):
- `// STUB:` — returns a constant / does nothing / wrong value; real callers WILL misbehave. Stays until real.
- `// GAP: <missing> — <revisit>` — correct on the happy path, a known edge unimplemented (e.g. "no MySQL
  migration yet"). Pins the limit cheaply for a future audit.
- Don't annotate code that does its job — the markers exist to bound the gap inventory, not decorate.

## Fix anything you surface — no deferring

Every kind of work (feature, bugfix, audit, running tests, reading the relay trace, a code review)
reveals the next layer of issues. **Fix everything that surfaces, even if it predates your slice or sits
outside your task's obvious boundary.** A "not my code" regression is still visible to whoever's next;
deferring just buries the cost with interest.

- **After every change, re-scan every surface that produces signal**, not just the one you started with:
  - `dotnet build server/BlackIce.Server.sln` — every `warning` is a fix target, every error is.
  - `dotnet test server/BlackIce.Server.sln` — every non-pass is a fix target (keep the full suite green).
  - `dotnet format --verify-no-changes` — style drift is a fix target (the PostToolUse hook usually handles it).
  - The live relay trace (`blackice-server-*.log` with `--trace`/`--debug`) — `FATAL`/`Unhandled`/`Exception`
    lines, and a listener loop that exited, are fix targets.
  - `git grep -nE "// (STUB|GAP):"` — the live gap inventory is itself the audit list.
  - Any CI check red on the branch's PR — poll until green.
- **Scope is whatever the signal exposes**, not what fits the commit message.
- **A symptom-cluster gets one investigation, not N** — trace one failure to root cause; the root usually
  retires the cluster (this is how the bot "stuck at origin" → "lava death" → "floating" chain each
  resolved to a single root, not N local patches).
- **Class-of-bug pattern matching** — recurring shapes worth checking before chasing call sites:
  *stale-comment drift* (comment claims behaviour the code dropped — fix both), *sentinel divergence*
  (two paths spell the same placeholder differently), *whitelist incompleteness* (a `== A` predicate
  missing a newly-added member), *merge-orphan duplication* (a git merge concatenates two copies of a
  test/method — both compiled, one stale; this bit us porting Phase 3 onto the plugin refactor).
- **Intermittent is still a bug** — a test that fails this run but not last is timing/ordering/ASLR
  dependent, not "flaky, ignore." Re-run to confirm, then find the shared-state root.
- **"No deferring" is the default**, not a special instruction. Don't propose follow-up slices or stash
  TODOs; fix now or give a concrete reason it can't land this session (needs a runtime artefact that
  doesn't exist, change bigger than context, a real refactor with a cyclic dependency).

## Wiring things in — functionality is not optional

A system that exists but is never initialized, called, or connected is **worse than not existing** — it
rots until a refactor accidentally re-enables it. Every interceptor must be in a plugin's chain; every
plugin must be discoverable (`PluginLoader.BuiltIn()` reflection) and registered; every hosted service
must be `AddHostedService`'d; every sink must have a source. **If you find something built but not wired
in: wire it immediately, or delete it.** (Phase 3's authority layer was inert until registered as a
plugin — that's the failure mode this rule prevents.)

## Diagnostic logging — keep it, gate it

When you add log lines to localise a bug, **don't strip them once it's fixed** — they're exactly what the
next debugger wants. The discipline: keep the useful line, gate it by level (`Log.Warn` for the failure
summary; `Log.Debug`/`Log.Trace` for verbose detail — visible only under `--debug`/`--trace`), and never
flood the default level. A clean run stays quiet; a regression leaves a `[WRN]` sentinel behind without
anyone re-adding prints. If a line wouldn't earn its place under those rules, don't add it.

## Reusable tooling — save it, don't re-derive it

When you write a script, harness, or one-off tool with value beyond the immediate task (a capture
parser, a repro driver, a log correlator, a map/asset extractor), **commit it under `tools/` instead of
leaving it in `/tmp`.** The next session should `ls tools/` and find it, not reverse-engineer it from a
transcript. Parameterise the hard-coded paths/timeouts, keep it dependency-light, syntax-check it, and
commit it with the work it supported. Re-deriving a rig every session is the same wasted-interest cost
as letting a signal rot — pay it once. (`tools/parallel/`, `tools/capture/`, the recon catalog all live
here for this reason.)

## Stream-timeout prevention

1. Do numbered tasks ONE AT A TIME — complete one, confirm, move on.
2. Avoid writing a file longer than ~150 lines in a single tool call; build it up in passes.
3. Keep grep/search output short (`--glob`, `-l`, `head_limit`).
4. If a step times out, retry it in a shorter form — don't restart the whole task.
