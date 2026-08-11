# QR Simple — Agent Notes

QR-based quick-info lookup for mining equipment. Domain vocabulary lives in [CONTEXT.md](CONTEXT.md) — read it before touching Equipment/Organization/Role terminology. Architectural decisions with real trade-offs are recorded in [docs/adr/](docs/adr/); check there before "fixing" something that looks wrong (e.g. why a QR code is never regenerated).

The full spec — every user story, in and out of scope for v1 — is [GitHub issue #1](https://github.com/tsogtbatjargal/qr-simple/issues/1). This file does not restate it; it tracks what's built versus what's left.

## Architecture

ASP.NET Core minimal API (`src/QrSimple.Api`) + PostgreSQL via EF Core, tested at a single seam: real HTTP requests in-process (`WebApplicationFactory<Program>`) against a real containerized Postgres (Testcontainers) — no mocking, no unit tests below that seam. `tests/QrSimple.Api.Tests/ApiFactory.cs` is the fixture; every test class implements `IClassFixture<ApiFactory>`.

`Program.cs` is a thin HTTP adapter — routes translate requests into calls on these modules and translate results back into responses. It should stay thin; if you find yourself adding a validation rule or business decision directly inside a route lambda, that logic belongs in one of the modules below instead:

- **`EquipmentCatalog`** — single-Equipment lifecycle (create, update, retire, reactivate). Owns category validation. Returns an `EquipmentResult` (`Success` / `NotFound` / `UnknownCategory`) that `Program.cs` pattern-matches into an HTTP response.
- **`EquipmentImport`** — bulk CSV import: parsing, duplicate skip+report, update-existing upsert mode.
- **`RequireRoleFilter`** — an `IEndpointFilter` gating write endpoints by role. Applied per-route via `.AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"))`.
- **`QrCode`** / **`ScanPage`** — QR PNG generation and the public scan-page HTML, respectively.

**Auth split**: authentication (proving who you are — real Google OAuth, wired in `Program.cs` via `AddGoogle`) is separate from authorization (what you can do — `RequireRoleFilter` looking up the authenticated email's `Role` in the `Users` table). Only authorization is covered by automated tests: real Google OAuth needs a live browser and a real account, so it can't run in CI. Tests authenticate via `TestAuthHandler`, a scheme registered only in `ApiFactory` that reads an `X-Test-Email` header. Use `factory.CreateClientAs("Operator")` (or `"Admin"` / `"Reader"`) in a test to get an `HttpClient` that's already seeded a User and attached that header — don't hand-roll authenticated requests.

`Equipment.Status` is an `EquipmentStatus` enum, not a string — stored as text (`HasConversion<string>()` in `AppDbContext`) and serialized as text (`JsonStringEnumConverter` in `Program.cs`), so the wire format reads `"Active"`/`"Retired"` even though the type is safe internally.

## Running tests

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$PATH"
export DOCKER_HOST="unix://$XDG_RUNTIME_DIR/podman/podman.sock"
export TESTCONTAINERS_RYUK_DISABLED=true
dotnet test
```

These three env vars are also appended to `~/.bashrc`, but non-interactive shells (including how the Bash tool invokes commands) don't reliably source it — export them explicitly per command rather than assuming they're already set.

### Environment gotchas

This machine is Fedora Silverblue (immutable OS) with no Docker and no `dotnet` preinstalled. Neither is a config-file fact you'd find by looking — worth knowing before you go hunting:

- **`dotnet`** is a user-local install at `~/.dotnet` (via the official `dotnet-install.sh` script), not `rpm-ostree layer` — that would need a reboot. If `dotnet` is ever genuinely missing, reinstall the same way rather than reaching for the package manager.
- **No Docker daemon** — Testcontainers talks to `podman` instead via `DOCKER_HOST` pointed at the rootless podman socket (`systemctl --user enable --now podman.socket` if it's ever not running — check with `systemctl --user status podman.socket`). `TESTCONTAINERS_RYUK_DISABLED=true` is required because Ryuk (Testcontainers' resource-reaper sidecar) doesn't play well with rootless podman.
- **SkiaSharp native library version must match the managed package exactly.** `SkiaSharp.NativeAssets.Linux` is pinned to `3.119.1` in both `.csproj` files to match the `SkiaSharp` version ZXing.Net.Bindings.SkiaSharp pulls in transitively. If you bump either package and see `System.TypeInitializationException` / "native libSkiaSharp library... is incompatible" at test runtime (not compile time), the pin has drifted — check `dotnet build` restore logs for the actual resolved `SkiaSharp` version and re-pin `SkiaSharp.NativeAssets.Linux` to match.

## Status

**Done** (all 22 user stories from issue #1, ~22 passing tests): Equipment CRUD + QR generation, public scan page (quick info, Retired indicator, Document links), bulk CSV import (happy path, duplicate skip+report, update-existing), managed/enforced Category list, Users + Google OAuth wiring + `/me` authorization, role-gated write endpoints, Reader's Equipment list with the Retired filter.

**Known, deliberate gaps** (not oversights — see conversation history / commit messages for the reasoning):
- `POST`/`GET /users` are intentionally ungated. Gating them would create a bootstrap problem: no Admin could ever create the first Admin. Revisit only alongside an actual bootstrap mechanism (e.g. first-user-becomes-admin), not by bolting `RequireRoleFilter` on ad hoc.
- Real Google OAuth (`Authentication:Google:ClientId`/`ClientSecret` in `appsettings.json`) is a `"REPLACE_ME"` placeholder — untested by design, per the auth split above.
- Explicit v1 non-goals (from issue #1): offline scanning, built-in file upload/hosting for Documents, non-Google sign-in, structured Site hierarchy, multi-Organization UI, audit/change history.

**Next natural slices** if picking this back up: a real bootstrap mechanism for the first Admin, wiring real Google credentials for a live deployment, or anything from the "explicit non-goals" list above if priorities have changed.
