# Agent Session Bootstrap

Scope: entire repository (`BlackIce.Server`).

For every new session/chat in this repo:

1. Read `CLAUDE.md` first — it is the authoritative project guide (build/test, conventions, the
   clean-room rule, the security gate, and the phased spec → plan → build workflow).
2. Skim `docs/` for context: protocol facts in `docs/protocol/`, design directives in `docs/design/`,
   and per-phase specs/plans under `docs/superpowers/`.
3. Honour the workflow: each phase is its own **spec → plan → build** under `docs/superpowers/`. Don't
   jump to code on a new phase without a spec/plan.
4. If concurrent sessions are possible, follow `CLAUDE_PARALLEL.md` — run `tools/parallel/status.sh`
   and claim your area before editing.

If `CLAUDE.md` is missing, report that clearly and continue with available context.

## Project summary for agents

BlackIce.Server is a **clean-room, from-scratch C#/.NET server** that speaks the Black Ice game's Photon
protocol, so the game can run independent of Photon Cloud (eventual goal: server authority + custom
anticheat). Precedents: OpenRA, TrinityCore. **Intended to be open-sourced under GPLv3.**

⛔ **The one rule that matters most:** never commit copyrighted/secret material — no decompiled source,
game binaries (`*.dll`/`*.exe`/`*.pdb`), asset/capture dumps, or credential-bearing runtime files. These
are gitignored; reference the game's behavior only in our own words under `docs/`. See `CLAUDE.md`, `NOTICE`,
`SECURITY.md`.
