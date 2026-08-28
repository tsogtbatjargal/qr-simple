---
name: qr-simple-ui
description: Use when styling, reviewing, or visually verifying qr-simple's admin UI (Blazor pages under src/QrSimple.Api/Components/Pages, rendered at /app) or the public scan page (src/QrSimple.Api/ScanPage.cs, rendered at /e/{id}) — covers the shared design tokens both surfaces must reuse and the Playwright MCP loop for confirming a change actually rendered.
---

# qr-simple UI

Full environment setup, sign-in, and troubleshooting: `docs/local-browser-testing.md`. Role/permission architecture: `AGENTS.md`'s Architecture section. Read both fully before a fresh session's first UI change.

## One product, two surfaces

`src/QrSimple.Api/wwwroot/app.css` (admin UI, linked from `App.razor`) and the inline `<style>` block in `src/QrSimple.Api/ScanPage.cs` (public scan page) are one shared design system, not two independent stylesheets. Both link `wwwroot/brand/tokens.css`, which owns every colour, radius, shadow and the self-hosted Inter `@font-face` rules. Before writing new CSS, check the `--brand-*` properties there and reuse an existing `var(--...)` token; `app.css`'s `:root` block only aliases them for readability. Introducing a literal hex/radius/shadow value on either surface is a bug, not a style choice — add it to `tokens.css` instead, and keep the upstream master at `/home/tsogo/icsmongolia/brand-kit/` in sync. Icons are inlined Lucide via `Components/Shared/Icon.razor` (admin) and matching `const` strings in `ScanPage.cs`; don't mix in another icon family or a bare Unicode glyph.

## Verify a change actually rendered

1. Start the API with `--launch-profile https` (`dotnet run --project src/QrSimple.Api --launch-profile https`) — costs nothing extra even for scan-page-only work, and admin-UI work needs it for sign-in. Don't start a second process if 5078/7040 is already serving. Restarting after an edit: follow the discrete kill → confirm-port-free → relaunch → poll-log procedure in `docs/local-browser-testing.md`, not a single chained command.
2. Confirm Chrome CDP and the Playwright MCP service are reachable with the readiness checks in `docs/local-browser-testing.md`. If not, tell the user to run `./scripts/start-chrome-for-playwright.sh` from a Fedora host terminal — that script cannot run from inside the devcontainer.
3. `browser_navigate` to the changed page, then `browser_take_screenshot` with no `filename` argument — it renders inline in the conversation (the service runs with `--image-responses allow`). An explicit `filename` currently resolves to the wrong path and fails.
4. `browser_resize` to a phone width (~390×844) and screenshot again — every admin page and the scan page must stay usable there; the `@media` blocks in `app.css`/`ScanPage.cs` are where that's enforced.
5. Check `browser_console_messages` for errors the change introduced.
6. For an exact style claim ("the header is `#156FC1`"), confirm with `browser_evaluate` computed styles — screenshots compress and shift color, computed styles don't. Resolved custom properties are worth checking too: `getComputedStyle(document.documentElement).getPropertyValue("--brand-primary")` catches a `tokens.css` that silently failed to load.

A change isn't done until steps 3–6 pass at both widths, signed in if the page requires it.

## Role-gated rendering

Equipment/User/Category pages render differently by role (Admin/Operator/Reader) and `IsActive` — see AGENTS.md. Before concluding a field or button is "missing," check `/me` for the signed-in session's actual role rather than assuming Admin.
