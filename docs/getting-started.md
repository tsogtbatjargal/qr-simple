# Getting started (fresh machine, or after a devcontainer rebuild)

This is the one doc meant to be followed top-to-bottom, in order, on a machine
that has none of this set up yet — including "the same machine, but the
devcontainer just got rebuilt and host-side things broke." Everything here was
verified against how this repo is actually run today (2026-08-16), not
reconstructed from memory — see the git history/commit messages if a step
here and `AGENTS.md` ever disagree.

**Scope note**: this repo has actually been developed on Fedora Silverblue +
Flatpak VS Code + rootless Podman, and several steps below are specific to
that combination — see `AGENTS.md`'s "Environment gotchas" section for why
each one exists. On a different OS/setup the intent is the same, but some
commands (especially the Flatpak-bridging ones) won't apply as written.

## 1. Clone the repo

```bash
git clone git@github.com:tsogtbatjargal/qr-simple.git
cd qr-simple
```

## 2. Host-side one-time setup

These live outside any devcontainer, so they're **not** wiped by a devcontainer
rebuild — do them once per machine.

1. Install VS Code (Flatpak) + the "Dev Containers" extension, and rootless
   Podman. .NET itself is **not** needed on the host — the devcontainer
   provides it.
2. Make sure the rootless Podman socket is running:
   ```bash
   systemctl --user enable --now podman.socket
   ```
3. Point VS Code's Dev Containers extension at Podman (not Docker), **at the
   User settings level** — Command Palette → "Preferences: Open User Settings
   (JSON)" — and add:
   ```json
   "dev.containers.dockerPath": "/absolute/path/to/qr-simple/scripts/podman-for-vscode.sh"
   ```
   The repo's `.vscode/settings.json` already sets this at the *workspace*
   level, but that alone isn't enough — the extension's own Docker-detection
   check runs before it picks up workspace settings, so it needs to also be
   set at the User level or Dev Containers won't find Podman at all.
4. Create the local dev Postgres container (one-time — this is a real,
   long-lived container the devcontainer connects to; it's separate from the
   ephemeral Testcontainers Postgres that `dotnet test` spins up for itself).
   Nothing in this repo currently scripts this creation step — this is the
   actual command, matching the container's current live config:
   ```bash
   podman run -d --name qr-simple-db \
     -e POSTGRES_PASSWORD=postgres \
     -p 5432:5432 \
     -v qr-simple-db-data:/var/lib/postgresql/data \
     postgres:16-alpine
   ```

## 3. Open the devcontainer

VS Code → "Reopen in Container." First build pulls the base image and restores
NuGet, so it's slow the first time. `postCreateCommand` (in
`.devcontainer/devcontainer.json`) automatically:
- installs `libfontconfig1` (needed by SkiaSharp/QR generation)
- runs `dotnet restore`
- runs `dotnet dev-certs https --trust` (needed for the `https` launch
  profile / local Google sign-in — this cert is **not** persisted across
  rebuilds, so this step matters every single time)

Claude Code and Codex extensions are declared in `devcontainer.json` too, so
they auto-install here rather than needing a manual reinstall.

In the devcontainer terminal:

```bash
dotnet tool restore
```

Installs `dotnet-ef` locally (see `docs/database-migrations.md`).

## 4. Wire up real Google OAuth

```bash
./scripts/setup-google-oauth.sh
```

If you already have a Client ID/Secret from another machine (or an earlier
setup on this one), reuse them instead of creating a new Google Cloud OAuth
client — the client isn't machine-scoped, and `localhost:7040/signin-google`
is already registered as an authorized redirect URI on it, so nothing needs
to change in Google Cloud Console when switching machines. This writes into
`dotnet user-secrets`, which is bind-mounted from the host
(`devcontainer.json`'s `mounts` entry) specifically so it survives future
rebuilds too — verified 2026-08-16.

## 5. Confirm it actually works

```bash
dotnet test QrSimple.slnx
```

60 tests should pass (as of 2026-08-16). If this fails, stop here and fix it
before moving on — everything below assumes a working build.

## 6. First sign-in against a fresh database

A newly created `qr-simple-db` has an empty `Users` table, so your first
Google sign-in will land on "Not authorized" until an Admin exists. One-time
bootstrap (only works while zero Admins exist yet):

```bash
dotnet run --project src/QrSimple.Api --launch-profile http
# in another terminal:
curl -X POST http://localhost:5078/users \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","role":"Admin"}'
```

Then run with `--launch-profile https` and sign in at `https://localhost:7040`.

## 7. Adding more testers later

Every new tester needs **two separate, manual additions** — there's no API to link them, and skipping either one fails differently:

1. **Google Cloud Console → APIs & Services → Google Auth Platform → Audience tab → Test users.** While the OAuth consent screen is in "Testing" publishing status (the default from `setup-google-oauth.sh`), only emails on this list (max 100) can complete Google sign-in at all — everyone else is blocked before ever reaching the app. This is Console-UI-only; there's no public API for it (the IAP-brand APIs are unrelated and don't apply here, and were shut down in March 2026 regardless). A test user's grant also expires 7 days after consent — a re-consent prompt later isn't a bug.
2. **The app's own `Users` table** (`/app/users` in the admin UI, once an Admin exists). This is what actually authorizes them inside the app (Admin/Operator/Reader) — a signed-in identity with no `Users` row lands on "Not authorized".

If every tester shares one Google Workspace domain **and** the GCP project belongs to that org, switching the consent screen's User Type from External to Internal (same Audience tab) removes step 1's per-email requirement entirely — anyone in the org can sign in. Personal Gmail testers don't have this option; add them one at a time.

**Or skip the allowlist entirely: publish to production.** Google Cloud Console → APIs & Services → Google Auth Platform → Audience tab → **Publish App**. Since this app only requests basic scopes (`email`/`profile`/`openid`), this needs no verification review and is free — the confirm dialog completes immediately. Nothing in the codebase changes (same Client ID/Secret/redirect URI). Consequences: any Google account can now reach sign-in (still gated by qr-simple's own `Users` table for authorization — an unlisted signer just lands on "Not authorized"), and every signer sees an "unverified app" click-through warning until/unless you also do the optional, free brand verification (adds your logo/app name to the consent screen). Reversible from the same Audience page.

## Optional: live browser testing / agent MCP tooling

Only needed for live Playwright-driven browser verification or running the
project-local read-only MCP an agent uses for workspace/app inspection. Both
assume steps 1–5 above are already done. See `docs/local-browser-testing.md`
and `docs/local-agent-mcp.md`.

## Deploying

Not part of per-machine dev setup — see `AGENTS.md`'s "Deployment" section.
