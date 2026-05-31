---
name: ef-migration
description: Add or apply an EF Core migration for the BlackIce.Server.Data layer (BlackIceDbContext), with the correct --project/--startup-project wiring and the SQLite-vs-MySQL provider caveat handled. Invoke with /ef-migration <MigrationName>.
disable-model-invocation: true
---

# EF Core migration helper (BlackIce.Server.Data)

The data layer (`server/BlackIce.Server.Data`, `BlackIceDbContext`) currently provisions its
schema with `EnsureCreated` — migrations are deferred. Use this skill when adopting migrations
or adding one. The DbContext lives in `BlackIce.Server.Data`; the runnable entry point (which
supplies configuration/DI) is `BlackIce.Server.Host`, so EF must be told both.

## 0. Prerequisites (check first)

```bash
dotnet ef --version            # if "command not found":
dotnet tool install --global dotnet-ef
```

## 1. ⚠️ Provider caveat — decide before generating

The project supports **two** providers (SQLite default + Pomelo MySQL, swapped via
`blackice.server.json`). **EF migrations are provider-specific** — a migration scaffolded
against SQLite will not faithfully apply to MySQL and vice-versa. Pick the approach:

- **Single-provider (simplest, recommended for now):** generate against the **default
  SQLite** provider only. Document that MySQL deployments use `EnsureCreated` or a manual
  schema until multi-provider migrations are set up.
- **Multi-provider (only if you ship MySQL):** maintain separate migration sets, e.g.
  `Migrations/Sqlite` and `Migrations/MySql`, selected by a design-time factory that reads
  which provider is active. This is a larger change — confirm with the user before doing it.

The design-time provider is whatever `BlackIceDbContext` / the Host resolves by default
(SQLite, `Data Source=blackice.db`). Ensure config selects the intended provider before running.

## 2. First migration (transition off EnsureCreated)

If no migrations exist yet, the initial migration must match the schema `EnsureCreated`
already produces, and the dev DB must be recreated:

```bash
dotnet ef migrations add InitialCreate \
  --project server/BlackIce.Server.Data \
  --startup-project server/BlackIce.Server.Host \
  --context BlackIceDbContext
```

Then switch the Host's startup from `EnsureCreated()` to `Migrate()` (or
`context.Database.Migrate()`), and delete any stale dev `*.db` so it's rebuilt from migrations.

## 3. Add a migration (ongoing)

```bash
dotnet ef migrations add <MigrationName> \
  --project server/BlackIce.Server.Data \
  --startup-project server/BlackIce.Server.Host \
  --context BlackIceDbContext
```

Inspect the generated `Up`/`Down` in `server/BlackIce.Server.Data/Migrations/` — confirm it
matches intent and that `Down` cleanly reverses `Up`.

## 4. Validate before committing

```bash
# Applies migrations to a throwaway DB to prove they run end-to-end:
dotnet ef database update \
  --project server/BlackIce.Server.Data \
  --startup-project server/BlackIce.Server.Host \
  --context BlackIceDbContext
dotnet build server/BlackIce.Server.sln
dotnet test  server/BlackIce.Server.sln
```

Commit the migration files with a `feat(data):` message. Do **not** commit the resulting
`*.db` (gitignored, and the leak-guard blocks it).
