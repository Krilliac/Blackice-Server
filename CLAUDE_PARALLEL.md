# Parallel Session Protocol

## Read before touching files when more than one session is active

BlackIce.Server may be worked on by several concurrent Claude Code sessions at once. When that's the
case, follow this protocol so two sessions don't edit the same code at the same time. The scripts in
`tools/parallel/` automate the bookkeeping; the shared coordinator file `PARALLEL_WORK.md` (tracked at
the repo root) is the source of truth for who owns what.

> **Single session?** You can skip the claim dance — but it's still good hygiene to run `status.sh` so a
> later concurrent session sees the repo state. The protocol is mandatory only once two sessions overlap.

> **Visibility note.** Coordination only works if every session sees the same coordinator. `claim.sh` /
> `release.sh` rebase on `origin/master` first so claims that have merged are visible. A claim that lives
> only on an un-pushed session branch is invisible to others — when in doubt, check with the other
> session rather than assuming a file is free.

---

## On session start

**Step 1 — Check active sessions:**
```bash
tools/parallel/status.sh
```
Read the output. Identify what's claimed and by whom, and note the conflict check.

**Step 2 — Claim your area before editing anything:**
```bash
tools/parallel/claim.sh <area-name> "<files/dirs>" "<brief description>"
```
Examples:
```bash
tools/parallel/claim.sh authority "server/BlackIce.Server.LoadBalancing/Authority/*" "Phase 3 outcome rules"
tools/parallel/claim.sh codec     "server/BlackIce.Photon/*"                          "GpBinary writer fix"
tools/parallel/claim.sh data      "server/BlackIce.Server.Data/*"                     "EF Core realm schema"
tools/parallel/claim.sh bots      "server/BlackIce.Server.LoadBalancing/Bots/*"       "smart-bot behavior"
```

This will:
- Rebase your branch on the latest `origin/master`.
- Warn you if your target files are already claimed by an active session.
- Keep you on your `claude/*`/`feat/*` session branch (or create `claude/<area>`).
- Register your claim in `PARALLEL_WORK.md` and commit it.

**Step 3 — Verify no conflicts before proceeding.**
If `claim.sh` warned about an existing claim, stop and coordinate with the other session.

---

## During work

- **Stay in your claimed files/dirs.** Don't touch files outside your scope.
- **Commit frequently** — every logical unit of work, conventional commits (`feat(authority):`, …),
  not one giant commit.
- **If you need a file owned by another session**, ask before touching it. Coordinate via `status.sh`.
- **Do not edit `PARALLEL_WORK.md` by hand** — use the scripts.
- **Honour the project rules** (see `CLAUDE.md`): clean-room (no game-derived material), the spoofable-
  SteamID security gate, and **spec → plan → build per phase**. A claim grants editing ownership, not a
  licence to skip the workflow.

---

## On session complete

```bash
# Push the session branch only (a human or another session does the merge):
tools/parallel/release.sh <area>

# Push AND merge into master (explicit opt-in):
tools/parallel/release.sh <area> --merge
```

Use `--merge` only when **all** of these hold:
- Your area has no dependency on in-progress work from another session.
- The full suite is green: `dotnet test server/BlackIce.Server.sln`.
- The project's "don't push/merge to master without explicit permission" bar is met — the `--merge`
  flag *is* that explicit opt-in, so do not pass it casually. (Large features should land via PR/review,
  not `--merge`.)

---

## Conflict resolution

If two sessions edited the same file:
1. Do **not** blind force-push over the other session's work.
2. Scope the delta: `git diff origin/master...HEAD`.
3. If the other session merged first, rebase: `git rebase origin/master`.
4. Resolve conflicts, `git add` the files, then re-run `release.sh`.

`release.sh` pushes with `--force-with-lease`, which refuses to clobber commits you haven't seen — if it
rejects the push, fetch and rebase before retrying.

---

## File-ownership cheatsheet (BlackIce layout)

| Area        | Path                                                  | Notes                                          |
|-------------|------------------------------------------------------|------------------------------------------------|
| photon      | `server/BlackIce.Photon/`                            | GpBinary codec, transport, wire types          |
| crypto      | `server/BlackIce.Photon.Crypto/`                     | Diffie-Hellman / cipher                         |
| core        | `server/BlackIce.Server.Core/`                       | UdpListener, PeerConnection, options, router   |
| data        | `server/BlackIce.Server.Data/`                       | EF Core: accounts, realms, MOTD, migrations    |
| lb          | `server/BlackIce.Server.LoadBalancing/`              | Relay, handlers, room sessions (broad)         |
| authority   | `server/BlackIce.Server.LoadBalancing/Authority/`    | Anti-cheat / server-authority interceptors     |
| plugins     | `server/BlackIce.Server.LoadBalancing/Plugins/`      | Built-in server plugins                        |
| bots        | `server/BlackIce.Server.LoadBalancing/Bots/`         | Playerbots                                      |
| host        | `server/BlackIce.Server.Host/`                       | Program/Generic Host, hosted services, config  |
| tests       | `server/BlackIce.Server.Tests/` (+ `.Photon.Tests/`, `.Data.Tests/`) | xUnit suites          |
| client-mods | `plugins/`                                            | CLIENT-side BepInEx mods (netstandard2.0)      |
| docs        | `docs/`                                               | Protocol, design, specs/plans                  |
| build       | `*.sln`, `server/Directory.Build.props`, `cmake`-equivalent project files | **Shared — coordinate before editing** |

> Cross-cutting spots are high-conflict: `BlackIce.Photon/PhotonCodes.cs`, `RoomSession.cs`,
> `IEventInterceptor.cs`, the `.sln` files, and `Directory.Build.props` get pulled in tree-wide. If
> another session is active, wait or stage the change in an area-local spot and promote it at integration.

---

## Quick reference

```bash
tools/parallel/status.sh                          # See all active/completed sessions
tools/parallel/claim.sh <area> "<files>" "<desc>" # Claim before starting
tools/parallel/release.sh <area>                  # Release when done (push branch)
tools/parallel/release.sh <area> --merge          # Release and merge to master
```
