# Handoff: verify Google sign-in after publishing the OAuth app to production

You are a Codex agent running inside the `qr-simple` VS Code devcontainer. Read `AGENTS.md` and `docs/local-browser-testing.md` completely before doing anything — this handoff assumes both.

## Context

The project owner published the Google OAuth consent screen from "Testing" to **"In production"** in Google Cloud Console (Audience tab → Publish App). No code changed for this — same Client ID/Secret/redirect URI. Scopes are `email`/`profile`/`openid` only, so this needed no verification review.

A prior session (running directly on the Fedora host, not this devcontainer) drove the live-browser verification most of the way: it started the DB/Chrome/Playwright-MCP stack, started the API with the `https` launch profile, navigated to `https://localhost:7040/app`, clicked through the expected self-signed-cert interstitial, and reached Google's real sign-in page cleanly (`accounts.google.com/.../signin/identifier`, correct `client_id`, `redirect_uri=https://localhost:7040/signin-google`, `scope=openid+profile+email` — no `invalid_client`/`access_blocked` error, which already confirms the OAuth client wiring survived the publish). It could not go further: completing real Google sign-in needs the project owner's actual credentials, which no agent has or should attempt to type. Then the owner restarted their PC mid-task, which killed that whole stack; it was restarted once already but the host session's own MCP connection went stale and didn't reconnect, so it handed off here instead of finishing.

## Your task

1. Read the docs above, then run the documented readiness checks (Chrome CDP `9222`, Playwright MCP `8931`, API `5078`/`7040`). Everything may already be running from the prior session, or may need restarting after a further PC restart — check first, don't assume either way.
2. If Chrome/DB/Playwright-MCP are down, you cannot start them yourself from inside the devcontainer — tell the project owner to run this in a **Fedora host terminal** and wait for them to confirm:
   ```bash
   cd /var/home/tsogtb/git-projects/qr-simple && ./scripts/start-chrome-for-playwright.sh
   ```
3. If the API isn't running, start it **with the `https` profile** (plain `http` is not enough — OAuth needs port 7040):
   ```bash
   dotnet run --project src/QrSimple.Api --launch-profile https
   ```
   Follow the doc's discrete-restart-steps pattern if replacing an existing process (kill, confirm port free, `nohup ... &` + `disown`, poll the log for `Now listening on` — don't chain it into one command).
4. Use the Playwright MCP browser tools to navigate to `https://localhost:7040/app`.
   - Expect `net::ERR_CERT_AUTHORITY_INVALID` on first navigation after any Chrome restart — this is documented and expected, not a bug. Click through: **Advanced → "Proceed to localhost (unsafe)"**. Persists for the rest of that Chrome profile's session, so skip if you land straight on Google instead.
   - This should redirect through `/login` to Google's real sign-in page. If you instead see `invalid_client`, `access_blocked`, or any error *before* reaching Google's normal email/account screen, stop and report that precisely — it would mean the publish broke something, contrary to what was already observed.
5. **You cannot complete this step yourself.** Ask the project owner to look at the visible Chrome window (not headless — it should be on their actual screen) and enter their Google credentials there. Do not attempt to fill in any email/password yourself.
6. Once they confirm they've gone through it, verify and report on all of the following, with concrete evidence (URL, screenshot, and/or `browser_network_requests`):
   - Final URL lands back on the app, signed in and **authorized** (not "Not authorized").
   - Whether an "unverified app" / "Google hasn't verified this app" click-through screen appeared during the flow. Note it either way — it's expected to still appear (only publishing status changed, not brand verification), so seeing it is not a bug; report the observation regardless.
   - The redirect chain has no errors (`browser_network_requests`).
7. The `Users` table already has Admin/Operator/Reader rows seeded, including a real Gmail-based Admin — no bootstrap `POST /users` step should be needed for this check. If the signed-in account lands on "Not authorized," check whether that specific email exists in `Users` (see `AGENTS.md`'s Architecture section / `docs/database-migrations.md`) before assuming the publish itself is broken — those are two independent things.
8. Do not rebuild the devcontainer, and do not replace the Playwright/project MCP URLs with a `/var/home/...` launcher — both are documented footguns in `docs/local-browser-testing.md`.

Report back with what you concretely verified and any remaining blocker — not just "done."
