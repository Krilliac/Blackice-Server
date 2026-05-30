# BlackIce.Server

An independent, open-source server implementation for the Unity game **Black Ice**,
created for interoperability, preservation, and server-authoritative anti-cheat.

Black Ice's multiplayer runs on Photon PUN with a *master-client authority* model:
one player simulates the shared world for everyone, which makes client-side cheating
trivial. This project reimplements the server side of that protocol so the game can run
on infrastructure you control, and so world authority can move from an untrusted player
to the server.

## Status

**Phase 0 — Reconnaissance & Protocol Map.** Documenting the protocol and building the
recon tooling. No server yet. See `docs/superpowers/plans/`.

## Legal / scope

This is a clean, independent interoperability project in the tradition of OpenRA and
TrinityCore. It contains **only original code and protocol documentation**. It does not
contain, redistribute, or depend on the game's copyrighted binaries, assets, or
decompiled source — those are analysis-only artifacts kept locally and excluded from
version control. You must own a legitimate copy of Black Ice to use this software.

## License

GPLv3 — see `LICENSE`.
