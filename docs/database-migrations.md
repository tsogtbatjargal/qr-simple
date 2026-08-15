# Database migrations

`Program.cs` calls `Database.Migrate()` on startup. EF Core tracks which migrations have run in a
`__EFMigrationsHistory` table in the target database. Any database that already has `__EFMigrationsHistory`
rows behaves normally — `dotnet ef database update` (or app startup) applies only what's missing.

## One-time step: adopting migrations on a pre-existing database

Before this change, the schema was created via `Database.EnsureCreated()`, which never wrote to
`__EFMigrationsHistory`. The first migration in this repo, `InitialCreate`
(`src/QrSimple.Api/Migrations/20260814173925_InitialCreate.cs`), was generated to match that
already-created schema exactly — so it must be marked as applied *without running its SQL* (the
tables it would create already exist). Skipping this step means `Migrate()` tries to `CREATE TABLE`
for tables that already exist, and the app crashes on startup.

Run this once, directly against any database that was created via the old `EnsureCreated()` path
and has never run an EF migration before:

```sql
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260814173925_InitialCreate', '10.0.10')
ON CONFLICT DO NOTHING;
```

After that, `dotnet ef database update` (or app startup) will apply only the real migration —
`AddUserIsActiveAndEmailIndex` — which adds the `Users.IsActive` column (existing rows default to
`true`, so no one is locked out) and a unique index on `Users.Email`.

**The unique index will fail to create if duplicate emails already exist.** Resolve any duplicates
first (e.g. via the `/app/users` admin page — deactivate or fix the wrong row) before running the
update. This is intentional: the migration failing loudly is safer than silently succeeding with
bad data.

## Fresh databases

None of the above applies to a database that has never been created by this app before (e.g. the
Testcontainers Postgres instance used in tests, or a new environment). `Migrate()` applies every
migration from scratch in order and just works.

## Day-to-day workflow

This repo uses a local `dotnet-ef` tool, restorable via:

```sh
dotnet tool restore
```

To add a new migration after changing the EF model:

```sh
dotnet dotnet-ef migrations add <Name> --project src/QrSimple.Api --startup-project src/QrSimple.Api
```
