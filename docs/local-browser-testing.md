# Local browser testing

This is the durable handoff for running and controlling `qr-simple` in the real visible Google Chrome browser from Codex inside the VS Code devcontainer.

Read this before changing the devcontainer, installing Node on Fedora, adding port forwarding, or concluding that Playwright is unavailable.

## How the pieces connect

| Component | Where it runs | Address | Purpose |
| --- | --- | --- | --- |
| PostgreSQL (`qr-simple-db`) | Rootless Podman on Fedora | `127.0.0.1:5432` | Local application database |
| QR Simple API | VS Code devcontainer | `http://127.0.0.1:5078` | ASP.NET Core API and public scan pages |
| Playwright MCP (`qr-simple-playwright-mcp`) | Rootless Podman on Fedora | `http://127.0.0.1:8931/mcp` | Browser tools used by Codex |
| Google Chrome | Fedora Flatpak, disposable profile | CDP `127.0.0.1:9222` | Visible browser controlled by Playwright |
| Codex extension | VS Code devcontainer | Reads `.codex/config.toml` | MCP client |

The devcontainer deliberately uses `--network=host`, so all five components can use the same loopback addresses. `.codex/config.toml` therefore uses an HTTP MCP URL instead of a host-only `/var/home/...` launcher path.

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
