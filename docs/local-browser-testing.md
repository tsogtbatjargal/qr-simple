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
3. Opens visible Flatpak Chrome with its disposable CDP profile, and **keeps it alive** — the command does not return, it stays in the foreground restarting Chrome if it ever exits (see the CDP troubleshooting entry below for why that's necessary).

Leave that terminal running. Add `--detach` if you'd rather it run in the background (`./scripts/start-chrome-for-playwright.sh --detach`), which is the right form when an agent starts it from the host. Closing the Chrome window is no longer fatal — the keeper reopens it within a few seconds.

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
- **Sign-out changed on 2026-08-31; the warning that used to sit here no longer applies.** Clicking
  "Sign out" now lands on the app's own login page at `/login?signedOut=true` — ICS logo,
  "Equipment Registry", and a "You've been signed out." banner — instead of bouncing on to Google.
  The sign-in button challenges with `prompt=select_account`, so Google shows the account picker
  rather than silently re-authenticating the live SSO session. Signing out and straight back in
  should therefore *require* an explicit account choice; landing back in the previous account with
  no prompt **is** a bug now. Historical note, in case you're reading an older transcript: before
  that change, `/app` → 302 → `/login` → Google `prompt=none` silent re-auth → back to `/app` 200
  was the expected sequence, and a page still showing the user signed in right after sign-out was
  not proof of failure.

## Restarting the API after a code change

A backgrounded `dotnet run` process has no hot reload — it serves the DLL built at start time.
After editing code, do the restart as **discrete steps**, not one chained command (a single
compound `kill && dotnet run &` is prone to the `nohup`'d process silently never starting):

1. Kill the old process.
2. Confirm the port is actually free.
3. Run a fresh `nohup dotnet run --project src/QrSimple.Api --launch-profile <profile> > <logfile> 2>&1 &` (with `disown`) from the repo root.
4. Poll the log for `Now listening on` before proceeding — don't assume step 3 succeeded silently.

## What the application displays

`qr-simple` has two rendered surfaces plus plain JSON routes. `/categories` (and most other non-`/app` routes) returns JSON with no UI.

The public scan page, styled directly in `src/QrSimple.Api/ScanPage.cs` (no external stylesheet — the `<style>` block is inline in that file):

```text
http://127.0.0.1:5078/e/{equipment-id}
```

An Equipment record must exist before that page can be tested.

The Blazor Server admin UI at `/app` (requires the `https` launch profile and a signed-in, authorized session — see the sign-in section above) is styled via `src/QrSimple.Api/wwwroot/app.css`, which deliberately reuses `ScanPage.cs`'s exact design tokens — as of 2026-08-28 both surfaces link `wwwroot/brand/tokens.css` (ICS brand blue `#156FC1`, `#f4f8fd` background, 14px card radius, shared shadow tokens, self-hosted Inter) rather than duplicating literal values, so the two surfaces read as one product. If you're checking that a UI change actually applied, verify with `browser_evaluate`/computed styles rather than assuming the stylesheet loaded — Blazor requires `app.MapStaticAssets()` in `Program.cs`, not `UseStaticFiles()`, to serve `wwwroot` in a published build.

QR images are also generated through API routes; consult `src/QrSimple.Api/Program.cs` and the HTTP integration tests for the current route shapes.

### Playwright MCP screenshots

`scripts/start-playwright-mcp-service.sh` passes `--image-responses allow`, so `browser_take_screenshot` returns the PNG inline in the tool response by default — it renders directly in the conversation, no filesystem access needed. That flag exists because, over this HTTP-transport MCP connection, the default `imageResponses: auto` setting doesn't reliably detect that the client can display images, and silently drops the image data, leaving only a markdown link to a file — which used to be a dead end (see below), so don't remove the flag.

For the underlying file (full-page/large screenshots may also get scaled down in the inline response, and `browser_navigate`'s automatic accessibility snapshot is file-only, no inline form) the script also passes `--output-dir /output`, bind-mounted from `.playwright-mcp-output/` *inside the repo* (`:Z` suffix — see the script's comments for why plain `chmod 777` wasn't enough on this SELinux-enforcing Fedora host). Because the whole repo is already the devcontainer's workspace mount, files written there are visible from both a Fedora host terminal and the devcontainer at the same relative path — no `.devcontainer/devcontainer.json` change or rebuild needed. That directory is gitignored; treat its contents as disposable.

One caveat: passing an explicit `filename` to `browser_take_screenshot` currently resolves against the wrong base path in this setup and fails with `ENOENT` on a host path that doesn't exist inside the container — omit `filename` and let it auto-generate a timestamped one instead.

### `browser_file_upload` needs the container-internal `/output/...` path, not the devcontainer's `.playwright-mcp-output/...` path

Even though a failed `browser_file_upload` call's own error message lists `/workspaces/qr-simple` as an "allowed root" alongside `/output`, only `/output/<filename>` actually resolves — passing `/workspaces/qr-simple/.playwright-mcp-output/<filename>` fails with `ENOENT` even though that exact file demonstrably exists on the devcontainer filesystem (verified with `ls`/`stat` immediately before the call). Write test files into `.playwright-mcp-output/` from the devcontainer as usual (that part of the bind mount works, same as screenshot output), but pass the path to `browser_file_upload` as `/output/<filename>` — the container-internal path, not the devcontainer-relative one. Discovered building the Photo/Document file-upload feature (see `docs/plans/0001-document-file-upload.md`), where every `/workspaces/qr-simple/...` attempt 404'd and every `/output/...` attempt worked immediately after, with no other change.

### `browser_click` intermittently times out on visibly-clickable Blazor elements

`browser_click` occasionally fails with `TimeoutError: ... waiting for element to be visible, enabled and stable` against a button or `<input type=file>` that a same-second `browser_evaluate` computed-style check confirms is fully visible, enabled, and has a stable layout (`display`, `visibility`, `opacity`, `offsetParent` all normal) — a plain retry of the identical `browser_click` call sometimes succeeds, sometimes doesn't. Root cause not confirmed (candidates: Blazor Server's diffing briefly re-touching the DOM node during a render, or Playwright's own "stability" heuristic being oversensitive over this HTTP-transport MCP setup specifically). Reliable workaround: trigger the interaction via `browser_evaluate` instead — `document.querySelector(...)`/`Array.from(document.querySelectorAll('button')).find(...)` then a plain `.click()` — which has not been observed to fail the same way. For a native `<input type=file>` specifically, a JS `.click()` still correctly triggers the browser's real file-chooser dialog (Chromium treats it as a trusted gesture), so `browser_file_upload` still works normally afterward.

### `browser_evaluate` polling loops can lie about elapsed time

If you're verifying a client-side timer (e.g. a toast auto-dismissing after N milliseconds) by running a `setTimeout`-based polling loop inside `browser_evaluate` and comparing your loop's own iteration count to the real delay, don't trust it. The disposable-profile Chrome tab driven by this Playwright setup can have its JS timers throttled (backgrounded-tab-style deprioritization) even while nominally "active," so a loop doing `await sleep(200)` ten times does not reliably mean 2000ms actually elapsed — it can silently take much longer in wall-clock time while still reporting the same iteration count. This produced a false "the timer fired 5x too early" alarm once (`ToastHost`'s 3.5s dismiss looked like it fired at ~700ms). The fix was adding real server-side `Console.WriteLine(DateTime.UtcNow)` timestamps around the actual timer (`Task.Delay` runs server-side in Blazor Server, unaffected by browser tab throttling) and reading those from the `dotnet run` log instead of trusting the browser-side loop's own clock. If a client-side timing test looks wrong by a suspiciously large margin, suspect the measurement (browser timer throttling) before the code.

### This setup is Chrome-only — the one `::placeholder` rule written for Firefox is knowingly untested

`scripts/start-chrome-for-playwright.sh` drives Chrome over CDP, and there is no Firefox on the
host or in the devcontainer. That is fine for everything the admin UI does *except* one rule:
`::placeholder { color: var(--muted); opacity: .65 }` in `wwwroot/app.css` exists specifically
because **Firefox** applies a default opacity to `::placeholder` and Chrome does not. Chrome
passing that check therefore proves nothing about the case the line was written for.

Decision (2026-09-01, the owner's): **skip it.** Do not add a Firefox/geckodriver stack to this
setup for it, and do not report it as an outstanding test failure on every verification pass —
it is a deliberate gap, not an oversight. The rule is a one-line defensive default whose worst
case is a placeholder that reads slightly darker than intended on one browser; the cost of a
second browser stack in this container is not worth that. If Firefox ever becomes available for
another reason, checking the computed `opacity`/`color` on a placeholder there is a two-minute
job — but don't build the environment just to do it.

## Troubleshooting

### The running API process isn't visible from this devcontainer's `ps`/`sudo ps`

The port-sharing table above says the API runs "in VS Code devcontainer," but in practice the process that's actually bound to 5078/7040 may belong to a *different* devcontainer instance or terminal (yours, or another agent's) than the one the current agent is in — `--network=host` means every container on the Fedora host shares the same loopback and can `curl` the same ports, but each devcontainer instance still has its own PID namespace. `ps aux`, `sudo ps aux`, and even matching `/proc/net/tcp` socket inodes against every `/proc/[0-9]*/fd` in this container can all come up completely empty for a process that is demonstrably answering on those ports. This is not a permissions bug — the process genuinely isn't in this container's PID namespace, so it cannot be killed or restarted from here.

Don't burn time hunting for it (repeated `ps`/`lsof`/`fuser`/`sudo` variants, `/proc` scans) once a straightforward `ps aux | grep dotnet` and a socket-inode cross-check both come back empty — that's already the answer. Confirm build freshness instead by fetching the live page and grepping the response for a string unique to the new code (e.g. a new CSS rule or literal added in the change), and if it's stale, ask the user to restart the process themselves in whichever terminal owns it (discrete kill → confirm-port-free → relaunch → poll-log, per the section above) rather than trying to signal it from an agent session that can't see it.

### Codex lists Playwright but exposes no browser tools

1. Confirm `.codex/config.toml` contains `url = "http://127.0.0.1:8931/mcp"`.
2. Run the three readiness checks from inside the devcontainer.
3. Start the host browser stack if ports 8931 or 9222 are unavailable.
4. Restart only the Codex extension and open a new agent.
5. Do not change the MCP command to `/var/home/...`; that path is outside the devcontainer.

### Claude Code shows `playwright`/`qr_simple` as "failed to connect" even after you start the host stack

A Claude Code session resolves its configured MCP servers (`.mcp.json`) once, at session start. If Chrome/the Playwright MCP service/the project MCP weren't up yet at that moment, the session records those servers as failed and does **not** retry — bringing up `./scripts/start-chrome-for-playwright.sh` and `./scripts/start-qr-simple-mcp.sh` afterward, and confirming with the readiness `curl`s that they're genuinely listening, does not make the tools reappear mid-session. `ToolSearch` for `browser_navigate`/etc. keeps coming back empty even though `curl http://127.0.0.1:9222/json/version` and `curl -o /dev/null -w '%{http_code}' http://127.0.0.1:8931/mcp` both succeed. This is a one-time-resolution problem, not a stale-cache one you can force-refresh from inside the session.

Fix: start the host stack first, *then* start or restart the Claude Code session (this is the Claude-specific equivalent of "restart only the Codex extension" below — same root cause, different client). If you're mid-session and can't restart it, hand the live-browser-verification step to a different session/agent that starts after the stack is up (e.g. the devcontainer's own Codex/Claude agent), rather than trying to coax the current session's MCP connections back to life.

### Chrome can't be started from inside the devcontainer

Run `./scripts/start-chrome-for-playwright.sh` from a Fedora host terminal. The Flatpak Chrome GUI cannot be started directly by an unprivileged in-devcontainer process.

### Chrome CDP (9222) dies partway through a verification run

Symptom: the Playwright MCP tools resolve fine, but `browser_navigate` starts failing with `ECONNREFUSED 127.0.0.1:9222` mid-session. `chrome.log` just stops — no crash, no error explaining it. A process check is misleading here: `pgrep -f com.google.Chrome` can still match leftover `chrome_crashpad_handler` helpers after the actual browser is gone, so it looks alive. Check `pgrep -f "remote-debugging-port"` or `ss -ltnp | grep 9222` instead.

Cause: **Chrome quits when its last tab closes, and Playwright closes pages as it drives the browser.** The old version of `scripts/start-chrome-for-playwright.sh` ended in `exec flatpak run com.google.Chrome ... about:blank`, so once automation closed that final tab the whole browser exited and took CDP with it. Closing the visible window by hand does the same thing.

Fix (already in the script as of 2026-08-29): Chrome is launched with `--keep-alive-for-test`, its own automation flag that keeps the browser process alive with zero windows, and the script now runs as a **keeper loop** rather than `exec`ing Chrome — so any other exit (window closed, real crash) is followed by a relaunch about 3 seconds later. It gives up after five immediate consecutive exits rather than spinning forever on a genuinely broken Chrome.

Consequences worth knowing:

- **The script no longer returns.** It stays in the foreground; leave the terminal running, or start it with `./scripts/start-chrome-for-playwright.sh --detach` (logs to `/tmp/qr-simple-chrome-keeper.log`, stop with `pkill -f "start-chrome-for-playwrigh[t]"`). `--detach` is the right form for an agent driving this from the host, because a keeper started from an ordinary agent shell dies with that shell's process group.
- **Running it twice is now a no-op** — it detects a live CDP on 9222 and refuses, instead of starting a second Chrome that fights the first for the port.
- **If CDP does blink mid-run, wait a few seconds and reconnect** rather than asking for a manual restart; the keeper will already have brought it back. The profile lives on disk at `/tmp/qr-simple-chrome-playwright-profile`, so a signed-in Google session survives a keeper restart.

Shell gotcha when cleaning any of this up from an agent session: `pkill -f "remote-debugging-port=9222"` **matches its own shell's command line** and kills the invocation that ran it (exit code 144, command appears to die for no reason). Use a bracket to break the self-match — `pkill -f "remote-debugging-port=922[2]"` — the same trick the `--detach` output suggests.

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
- `scripts/start-chrome-for-playwright.sh` — one host command for the database, MCP service, and disposable Chrome. Runs as a foreground keeper that restarts Chrome if it exits; takes `--detach`.
- `scripts/start-playwright-mcp-service.sh` — Playwright MCP Podman service definition.
- `.playwright-mcp-output/` — gitignored; screenshots/PDFs/videos/snapshots written by the Playwright MCP service, readable from host and devcontainer alike.
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
