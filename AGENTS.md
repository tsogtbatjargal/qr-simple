# QR Simple — Agent Notes

QR-based quick-info lookup for mining equipment. Domain vocabulary lives in [CONTEXT.md](CONTEXT.md) — read it before touching Equipment/Organization/Role terminology. Architectural decisions with real trade-offs are recorded in [docs/adr/](docs/adr/); check there before "fixing" something that looks wrong (e.g. why a QR code is never regenerated).

The full spec — every user story, in and out of scope for v1 — is [GitHub issue #1](https://github.com/tsogtbatjargal/qr-simple/issues/1). This file does not restate it; it tracks what's built versus what's left.

## Architecture

ASP.NET Core minimal API (`src/QrSimple.Api`) + PostgreSQL via EF Core, tested at a single seam: real HTTP requests in-process (`WebApplicationFactory<Program>`) against a real containerized Postgres (Testcontainers) — no mocking, no unit tests below that seam. `tests/QrSimple.Api.Tests/ApiFactory.cs` is the fixture; every test class implements `IClassFixture<ApiFactory>`.

`Program.cs` is a thin HTTP adapter — routes translate requests into calls on these modules and translate results back into responses. It should stay thin; if you find yourself adding a validation rule or business decision directly inside a route lambda, that logic belongs in one of the modules below instead:

- **`EquipmentCatalog`** — single-Equipment lifecycle (create, update, retire, reactivate). Owns category validation. Returns an `EquipmentResult` (`Success` / `NotFound` / `UnknownCategory`); call `.ToHttpResult(onSuccess)` to turn it into a response — don't hand-write a new `switch` per route, `NotFound`/`UnknownCategory` already map correctly for every case.
- **`Equipment.Create(...)`** — the one place that constructs a new Equipment (`Id = Guid.NewGuid()` + field mapping). Both `EquipmentCatalog` and `EquipmentImport` call it — add a new required field once, here, not at every construction site.
- **`EquipmentImport`** — bulk CSV import: parsing, duplicate skip+report, update-existing upsert mode.
- **`RequireRoleFilter`** — an `IEndpointFilter` gating write endpoints by role. Applied per-route via `.AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"))`.
- **`QrCode`** / **`ScanPage`** — QR PNG generation and the public scan-page HTML, respectively.
- **`Components/`** — the Blazor Server admin UI (sign-in, Equipment list/add/detail). Pages call the modules above in-process (`EquipmentCatalog`, `QrCode`) rather than the JSON HTTP endpoints — they're another thin adapter, not a client of the API. Use `IDbContextFactory<AppDbContext>` here, not `AppDbContext` directly — a Blazor Server circuit's DI scope outlives a single request.

**Auth split**: authentication (proving who you are — real Google OAuth, wired in `Program.cs` via `AddGoogle`) is separate from authorization (what you can do — `RequireRoleFilter` looking up the authenticated email's `Role` in the `Users` table). Only authorization is covered by automated tests: real Google OAuth needs a live browser and a real account, so it can't run in CI. Tests authenticate via `TestAuthHandler`, a scheme registered only in `ApiFactory` that reads an `X-Test-Email` header. Use `factory.CreateClientAs("Operator")` (or `"Admin"` / `"Reader"`) in a test to get an `HttpClient` that's already seeded a User and attached that header — don't hand-roll authenticated requests.

`Equipment.Status` is an `EquipmentStatus` enum, not a string — stored as text (`HasConversion<string>()` in `AppDbContext`) and serialized as text (`JsonStringEnumConverter` in `Program.cs`), so the wire format reads `"Active"`/`"Retired"` even though the type is safe internally.

## Running tests

**Preferred: the devcontainer** (`.devcontainer/devcontainer.json`). VS Code on this machine is a Flatpak, which sandboxes it enough to cause two separate classes of pain: the `.NET Install Tool` extension misdetects the distro (it reads `/etc/os-release` from inside the Flatpak runtime, not the real host), and env vars set via `~/.bashrc` don't reliably reach it (GUI-launched Flatpak apps don't source login-shell profiles). The devcontainer sidesteps both — .NET ships baked into the image, and every env var it needs is set in `devcontainer.json` itself, not inherited from a shell. Open the folder in VS Code → "Reopen in Container." Every `runArgs`/`mounts`/`postCreateCommand` entry in that file has a comment explaining *why* — this took real trial and error (SELinux relabeling breaking host socket access, rootless podman's UID remapping, sibling-container port reachability, a missing `libfontconfig1` in the base image) to get to 22/22 passing; read the comments before changing any of it.

**Fallback: bare host shell** (if you're not using the devcontainer, e.g. running from a real terminal rather than VS Code):

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$PATH"
export DOCKER_HOST="unix://$XDG_RUNTIME_DIR/podman/podman.sock"
export TESTCONTAINERS_RYUK_DISABLED=true
dotnet test
```

These three env vars are also appended to `~/.bashrc`, but non-interactive shells (including how the Bash tool invokes commands, and how Flatpak VS Code launches) don't reliably source it — export them explicitly per command rather than assuming they're already set. A `.runsettings` file also injects them for VSTest specifically (`dotnet test --settings .runsettings`), which is what lets VS Code's Test Explorer work even outside the devcontainer.

## Live browser testing

Before diagnosing missing browser tools, Chrome/CDP, ports 8931/9222/5078, or an empty `[]` response, read [docs/local-browser-testing.md](docs/local-browser-testing.md). It is the durable restart and troubleshooting runbook for this Fedora + Flatpak VS Code + devcontainer setup.

Important facts for every new agent:

- Codex and Claude Code are interchangeable in this devcontainer: both read the same two HTTP MCP servers (`http://127.0.0.1:8931/mcp` Playwright, `http://127.0.0.1:8932/mcp` project MCP), Codex via `.codex/config.toml`, Claude via `.mcp.json` at the repo root. Either agent can pick up a task the other started without re-registering anything. Do not replace either config with a `/var/home/...` stdio launcher; that host path is inaccessible from inside the devcontainer.
- The devcontainer uses host networking, so either agent can reach the Fedora-hosted Playwright MCP service, Chrome CDP on port 9222, PostgreSQL on port 5432, and the API on port 5078 through `127.0.0.1`.
- After a PC restart, the user runs `./scripts/start-chrome-for-playwright.sh` once in a **Fedora host terminal**. It starts the existing `qr-simple-db`, starts/replaces the Playwright MCP service, and opens disposable-profile Chrome.
- The API itself is started in the **devcontainer terminal** with `dotnet run --project src/QrSimple.Api --launch-profile http`.
- A browser response of `[]` at `/categories` is a successful empty API response, not a broken UI. This project is primarily an API; the rendered public page is `/e/{equipment-id}` after Equipment exists.
- Do not rebuild the devcontainer merely because browser tools are missing. Check the runbook's readiness commands and restart only the Codex extension/new agent after the MCP service is available.

## Project-local MCP for the devcontainer agent

If you are starting a fresh Codex or Claude Code agent for normal `qr-simple` development, first read [docs/local-agent-mcp.md](docs/local-agent-mcp.md). It documents the project-local read-only MCP that lives inside the devcontainer and gives the agent stable workspace/app inspection tools.

Important facts:

- Start it from a **devcontainer terminal** with `./scripts/start-qr-simple-mcp.sh`.
- It listens on `http://127.0.0.1:8932/mcp` and is registered in both `.codex/config.toml` and `.mcp.json`.
- It is read-only by design. Keep browser automation in the separate Playwright MCP.
- The first tools are `workspace_search`, `workspace_read`, `app_health`, `route_inventory`, `route_auth_summary`, and `latest_test_failures`.
- After a PC or VS Code restart, start the MCP again before launching a new agent, Codex or Claude.

### Environment gotchas

This machine is Fedora Silverblue (immutable OS) with no Docker and no `dotnet` preinstalled, and VS Code is a Flatpak. None of this is a config-file fact you'd find by looking — worth knowing before you go hunting:

- **`dotnet`** is a user-local install at `~/.dotnet` (via the official `dotnet-install.sh` script), not `rpm-ostree layer` — that would need a reboot. If `dotnet` is ever genuinely missing, reinstall the same way rather than reaching for the package manager.
- **No Docker daemon** — Testcontainers talks to `podman` instead via `DOCKER_HOST` pointed at the rootless podman socket (`systemctl --user enable --now podman.socket` if it's ever not running — check with `systemctl --user status podman.socket`). `TESTCONTAINERS_RYUK_DISABLED=true` is required because Ryuk (Testcontainers' resource-reaper sidecar) doesn't play well with rootless podman.
- **SkiaSharp native library version must match the managed package exactly.** `SkiaSharp.NativeAssets.Linux` is pinned to `3.119.1` in both `.csproj` files to match the `SkiaSharp` version ZXing.Net.Bindings.SkiaSharp pulls in transitively. If you bump either package and see `System.TypeInitializationException` / "native libSkiaSharp library... is incompatible" at test runtime (not compile time), the pin has drifted — check `dotnet build` restore logs for the actual resolved `SkiaSharp` version and re-pin `SkiaSharp.NativeAssets.Linux` to match.
- **VS Code's sandbox can't run host binaries directly** even though it has `filesystems=host` permission — `/usr/bin/podman` fails with a missing shared library when exec'd through the sandbox's bind-mounted view of it. `scripts/podman-for-vscode.sh` (used as `dev.containers.dockerPath`, set at **User** settings level — workspace-level wasn't picked up in time for the extension's own Docker-detection check) routes through `flatpak-spawn --host` instead, which actually executes on the host. If Dev Containers builds ever start failing with "podman: command not found" or a linker error, check that setting first.
- **`updateRemoteUserUID` must stay `false`** (set in `devcontainer.json`). The CLI's version of that feature generates a temp Dockerfile inside the Flatpak sandbox's own private `/tmp`, then hands the path to a command that actually runs on the host via `flatpak-spawn --host` — the host process can't see a file the sandbox wrote to its own `/tmp` (same path string, two different filesystems), so it fails with "the specified Dockerfile does not exist." `--userns=keep-id` in `runArgs` already does the same UID-matching job correctly, so this feature is redundant here, not just broken — don't re-enable it looking for a "better" fix.

## Status

**Done** (all 22 user stories from issue #1, ~28 passing tests): Equipment CRUD + QR generation, public scan page (quick info, Retired indicator, Document links), bulk CSV import (happy path, duplicate skip+report, update-existing), managed/enforced Category list, Users + Google OAuth wiring + `/me` authorization, role-gated write endpoints, Reader's Equipment list with the Retired filter.

**Admin UI** (Blazor Server, embedded in `QrSimple.Api`, not a separate project — see `src/QrSimple.Api/Components/`): sign in with Google, browse Equipment (`/app`), add Equipment through a form (`/app/equipment/add`), view an Equipment's fields and QR code (`/app/equipment/{id}`). `GET /login` triggers the Google challenge, `POST /logout` signs out — both plain minimal-API endpoints, not Blazor components, since a Blazor circuit can't call `Results.Challenge`/`SignOutAsync` itself. Role checks reuse `UserAuthorization.FindAsync` (also used by `RequireRoleFilter` and `/me`) via `RoleGatedComponentBase`, redirecting to `/app/not-authorized` for a signed-in identity with no `Users` row or an insufficient role. Covered by `tests/QrSimple.Api.Tests/AdminUiTests.cs` (auth/role gating + rendering, via the same real-HTTP `WebApplicationFactory` approach as everything else); actually submitting the interactive form and real Google sign-in aren't testable that way — see that file's comments.

**Known, deliberate gaps** (not oversights — see conversation history / commit messages for the reasoning):
- `POST`/`GET /users` are intentionally ungated. Gating them would create a bootstrap problem: no Admin could ever create the first Admin. Revisit only alongside an actual bootstrap mechanism (e.g. first-user-becomes-admin), not by bolting `RequireRoleFilter` on ad hoc. The admin UI's `/app/not-authorized` page points a newly-signed-in user at this same manual step rather than automating it.
- Real Google OAuth (`Authentication:Google:ClientId`/`ClientSecret` in `appsettings.json`) is a `"REPLACE_ME"` placeholder — run `scripts/setup-google-oauth.sh` once with real Cloud Console credentials before sign-in will actually work end to end. Untested by design otherwise, per the auth split above.
- No category-management UI — the add-equipment form's category dropdown reads existing Categories but can't create one; use `POST /categories` (Admin only) first.
- Explicit v1 non-goals (from issue #1): offline scanning, built-in file upload/hosting for Documents, non-Google sign-in, structured Site hierarchy, multi-Organization UI, audit/change history.

**Next natural slices** if picking this back up: a real bootstrap mechanism for the first Admin, wiring real Google credentials for a live deployment (the wizard above), a category-management UI, or anything from the "explicit non-goals" list above if priorities have changed.
