# Local browser testing

This is the durable handoff for running and controlling `qr-simple` in the real visible Google Chrome browser from a Codex or Claude Code agent inside the VS Code devcontainer. Both agents are wired to the same MCP endpoints (Codex via `.codex/config.toml`, Claude via `.mcp.json`), so this doc applies to either — swap "Codex" for "Claude" below as needed.

Read this before changing the devcontainer, installing Node on Fedora, adding port forwarding, or concluding that Playwright is unavailable.

## How the pieces connect

| Component | Where it runs | Address | Purpose |
| --- | --- | --- | --- |
| PostgreSQL (`qr-simple-db`) | Rootless Podman on Fedora | `127.0.0.1:5432` | Local application database |
| QR Simple API | VS Code devcontainer | `http://127.0.0.1:5078` | ASP.NET Core API and public scan pages |
| Playwright MCP (`qr-simple-playwright-mcp`) | Rootless Podman on Fedora | `http://127.0.0.1:8931/mcp` | Browser tools used by the agent |
| Google Chrome | Fedora Flatpak, disposable profile | CDP `127.0.0.1:9222` | Visible browser controlled by Playwright |
| Codex extension / Claude Code | VS Code devcontainer | Reads `.codex/config.toml` / `.mcp.json` | MCP client |

The devcontainer deliberately uses `--network=host`, so all five components can use the same loopback addresses. `.codex/config.toml` and `.mcp.json` therefore both use an HTTP MCP URL instead of a host-only `/var/home/...` launcher path.

Node is not installed on the Fedora host or in the .NET devcontainer. The Playwright service runs Node 22 in its own small Podman container and keeps npm downloads in the `qr-simple-playwright-npm-cache` volume.

Chrome automation always uses `/tmp/qr-simple-chrome-playwright-profile`. Never point it at the owner's normal Chrome profile.

## After restarting the PC

First, open a normal **Fedora host terminal**, not the VS Code devcontainer terminal:

```bash
cd /var/home/tsogtb/git-projects/qr-simple
./scripts/start-chrome-for-playwright.sh
```

That one command:

1. Starts the existing `qr-simple-db` container when it exists.
2. Starts or replaces the host-networked Playwright MCP service.
3. Opens visible Flatpak Chrome with its disposable CDP profile.

Leave that Chrome window open.

Next, open the repository in VS Code and reopen it in the devcontainer. In a **devcontainer terminal**, start the API:

```bash
dotnet run --project src/QrSimple.Api --launch-profile http
```

Keep that terminal running. If startup says port 5078 is already in use, do not start another copy—the API is already running. Check it with the readiness commands below.

Start a new Codex agent after the MCP service is running. If an already-open Codex agent has no Playwright tools, use Codex Settings → MCP Servers → Restart extension, then create a new agent. Rebuilding the devcontainer is not required for this browser setup.

## After restarting only VS Code

The database, MCP service, and Chrome may still be running. Check readiness from the devcontainer first. Start only the missing component; do not automatically rebuild or launch a second API process.

If Chrome or the MCP service is missing, rerun this from a Fedora host terminal:

```bash
cd /var/home/tsogtb/git-projects/qr-simple
./scripts/start-chrome-for-playwright.sh
```

If the API is missing, start it from the devcontainer terminal with the `dotnet run` command above.

## Readiness checks from the devcontainer

```bash
# Chrome CDP: should return JSON containing Browser and webSocketDebuggerUrl.
curl -sS http://127.0.0.1:9222/json/version

# Playwright MCP: a plain GET intentionally returns HTTP 400 "Invalid request".
# That 400 proves the HTTP MCP server is reachable; MCP clients use POST/SSE.
curl -sS -o /dev/null -w 'Playwright MCP HTTP %{http_code}\n' \
  http://127.0.0.1:8931/mcp

# API: should return HTTP 200. [] means the Categories table is empty.
curl -sS -w '\nQR Simple HTTP %{http_code}\n' \
  http://127.0.0.1:5078/categories
```

Expected healthy results are Chrome JSON, `Playwright MCP HTTP 400`, and `QR Simple HTTP 200`.

The strongest verification is to use the loaded Playwright MCP browser tool to navigate visible Chrome to:

```text
http://127.0.0.1:5078/categories
```

Then inspect the page URL/body and browser console. An `[]` body is expected until categories are created.

## Testing real Google sign-in (admin UI)

The `http` launch profile (port 5078 only, used above) is enough for the plain API and public scan
page, but **not sufficient for testing real Google sign-in** or anything under `/app`. The OAuth
`redirect_uri` is hardcoded to `https://localhost:7040/signin-google` (both in Google Cloud Console
and in `scripts/setup-google-oauth.sh`), so the admin UI's login flow only works when the app is
started with the `https` profile instead:

```bash
dotnet run --project src/QrSimple.Api --launch-profile https
```

This binds `https://localhost:7040` (required for OAuth) *and* `http://localhost:5078` — you don't
lose the plain API port by using it, so default to `https` whenever admin-UI/sign-in testing is a
possibility.

Other gotchas specific to this flow:

- **VS Code's automatic port-forwarding can silently occupy port 7040**, conflicting with the
  app's own HTTPS listener and corrupting the OAuth state-cookie round-trip (surfaces as
  `AuthenticationFailureException: The oauth state was missing or invalid`). Fix: free it via VS
  Code's Ports panel ("Stop Forwarding Port") — do not `kill` the forwarder process directly.
- Even after `dotnet dev-certs https --trust`, Playwright's Chromium (the disposable-profile
  Chrome from `start-chrome-for-playwright.sh`) still shows `net::ERR_CERT_AUTHORITY_INVALID` on
  first navigation to `https://localhost:7040`. Click through the interstitial once (Advanced →
  "Proceed to localhost (unsafe)") — this persists for the rest of that profile's lifetime.
- Signing in alone isn't enough — a `Users` row must exist for that email before `/me` and the
  admin UI treat it as authorized. The first Admin bootstraps via an unauthenticated `POST /users`
  call, allowed only while no Admin exists yet in the table; see `AGENTS.md`'s Architecture
  section and `docs/database-migrations.md`.
- `TestAuthHandler` (the `X-Test-Email` header used by `dotnet test`) is registered only inside
  `ApiFactory` for tests — it does not exist on a real `dotnet run` process. Exercising an
  authorized route live means either a real signed-in Google session, or a Playwright
  `browser_evaluate` page-context `fetch()` call from a tab that's already signed in (it
  automatically carries the session cookie) — don't try to fake an identity header against the
  live server.
- A page that still shows the user as logged in right after clicking "Sign out" is **not proof
  sign-out failed**. Chrome's live Google SSO session will silently re-authenticate via
  `prompt=none` on the very next request that needs auth, making a working sign-out look broken.
  To verify sign-out actually worked, hit a protected endpoint (e.g. `/me`) right after and check
  it starts a fresh auth challenge, or inspect `browser_network_requests` for the full redirect
  chain (`/app` → 302 → `/login` → Google `prompt=none` silent re-auth → back to `/app` 200 is the
  *expected*, correct sequence — not a bug) — don't trust what the rendered page shows moments
  later.

## Restarting the API after a code change

A backgrounded `dotnet run` process has no hot reload — it serves the DLL built at start time.
After editing code, do the restart as **discrete steps**, not one chained command (a single
compound `kill && dotnet run &` is prone to the `nohup`'d process silently never starting):

1. Kill the old process.
2. Confirm the port is actually free.
3. Run a fresh `nohup dotnet run --project src/QrSimple.Api --launch-profile <profile> > <logfile> 2>&1 &` (with `disown`) from the repo root.
4. Poll the log for `Now listening on` before proceeding — don't assume step 3 succeeded silently.

## What the application displays

`qr-simple` is primarily a minimal API, not a general frontend application. `/categories` returns JSON. The public rendered scan page is:

```text
http://127.0.0.1:5078/e/{equipment-id}
```

An Equipment record must exist before that page can be tested. QR images are also generated through API routes; consult `src/QrSimple.Api/Program.cs` and the HTTP integration tests for the current route shapes.

## Troubleshooting

### Codex lists Playwright but exposes no browser tools

1. Confirm `.codex/config.toml` contains `url = "http://127.0.0.1:8931/mcp"`.
2. Run the three readiness checks from inside the devcontainer.
3. Start the host browser stack if ports 8931 or 9222 are unavailable.
4. Restart only the Codex extension and open a new agent.
5. Do not change the MCP command to `/var/home/...`; that path is outside the devcontainer.

### Chrome CDP is unavailable on port 9222

Run `./scripts/start-chrome-for-playwright.sh` from a Fedora host terminal. The Flatpak Chrome GUI cannot be started directly by an unprivileged in-devcontainer process.

### API fails to connect to `127.0.0.1:5432`

The database is stopped. The host startup script normally starts `qr-simple-db`. From a Fedora host terminal, verify it with:

```bash
podman ps --filter name=qr-simple-db
podman start qr-simple-db
```

If `qr-simple-db` does not exist, stop and inspect the project's user-secrets/database configuration before recreating it. Do not invent or commit database credentials.

### API fails because port 5078 is already in use

Another API process is already running. Verify `http://127.0.0.1:5078/categories` instead of starting a duplicate process.

### Dev Containers waits indefinitely even though the container started

This was a Flatpak-to-host Podman event race. `scripts/podman-for-vscode.sh` adds `podman events --since` so the Dev Containers extension cannot miss the start event. Preserve that behavior and keep `updateRemoteUserUID` set to `false`; see `AGENTS.md` and comments in `.devcontainer/devcontainer.json`.

## Relevant files

- `.codex/config.toml` — project-scoped HTTP MCP registration.
- `scripts/start-chrome-for-playwright.sh` — one host command for the database, MCP service, and disposable Chrome.
- `scripts/start-playwright-mcp-service.sh` — Playwright MCP Podman service definition.
- `.devcontainer/devcontainer.json` — host networking and Podman socket setup.
- `scripts/podman-for-vscode.sh` — Flatpak VS Code to host Podman bridge and event-race fix.

## Copy/paste prompt for a new in-devcontainer agent

```text
You are running inside the qr-simple VS Code devcontainer. Read AGENTS.md and docs/local-browser-testing.md completely before taking action.

Continue the live browser verification. Do not rebuild the devcontainer and do not replace the project HTTP MCP URL with a /var/home/... launcher. First run the documented readiness checks for Chrome CDP (9222), Playwright MCP (8931), and the API (5078). Avoid starting a second API process if port 5078 is already serving.

If the API is stopped but PostgreSQL on 5432 is reachable, start it from the devcontainer with:
dotnet run --project src/QrSimple.Api --launch-profile http

Use the loaded Playwright MCP browser tools to control the visible disposable-profile Chrome. Navigate to http://127.0.0.1:5078/categories, inspect the page URL, response/body, console, and network result, and report concrete evidence. An HTTP 200 body of [] is a healthy empty Categories table, not a missing UI. The rendered public page is /e/{equipment-id} and requires an Equipment record.

If ports 8931 or 9222 are unavailable, do not rewrite configuration. Tell me to run this exact command in a Fedora host terminal:
cd /var/home/tsogtb/git-projects/qr-simple && ./scripts/start-chrome-for-playwright.sh

If the readiness checks pass but this agent has no Playwright tools, tell me to restart only the Codex extension and open a new agent. Report what you verified and any remaining blocker precisely.
```
