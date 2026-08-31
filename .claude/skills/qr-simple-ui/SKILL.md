---
name: qr-simple-ui
description: Use when styling, reviewing, or visually verifying qr-simple's admin UI (Blazor pages under src/QrSimple.Api/Components/Pages, rendered at /app) or either public page (src/QrSimple.Api/ScanPage.cs at /e/{id}, and src/QrSimple.Api/RebuildsPage.cs at /e/{id}/rebuilds) — covers the shared design tokens all three surfaces must reuse and the Playwright MCP loop for confirming a change actually rendered.
---

# qr-simple UI

Full environment setup, sign-in, and troubleshooting: `docs/local-browser-testing.md`. Role/permission architecture: `AGENTS.md`'s Architecture section. Read both fully before a fresh session's first UI change.

## One product, three surfaces

`src/QrSimple.Api/wwwroot/app.css` (admin UI, linked from `App.razor`), and the inline `<style>` blocks in `src/QrSimple.Api/ScanPage.cs` (public scan page, `/e/{id}`) and `src/QrSimple.Api/RebuildsPage.cs` (public rebuild history page, `/e/{id}/rebuilds`), are one shared design system, not three independent stylesheets. All three link `wwwroot/brand/tokens.css`, which owns every colour, radius, shadow and the self-hosted Inter `@font-face` rules. Before writing new CSS, check the `--brand-*` properties there and reuse an existing `var(--...)` token; `app.css`'s `:root` block only aliases them for readability. Introducing a literal hex/radius/shadow value on any surface is a bug, not a style choice — add it to `tokens.css` instead, and keep the upstream master in sync (`ssh lxc-ics-mongolia` — it's on a separate, not-version-controlled LXC container, not this dev host's filesystem; `wwwroot/brand/README.md` has the verified-in-sync details). Icons are inlined Lucide via `Components/Shared/Icon.razor` (admin) and matching `const` strings in `ScanPage.cs`/`RebuildsPage.cs`; don't mix in another icon family or a bare Unicode glyph.

The two public pages additionally share `src/QrSimple.Api/PublicPageChrome.cs` — the `<head>` meta/link tags, the `.site-header` markup, and the base `:root`/`body`/`.icon`/`.site-header`/`main`/`.panel` CSS. Added when the second public page shipped (`docs/plans/0002-inspection-records.md`; that file is now `RebuildsPage.cs`) specifically so the two public pages can't drift apart the way copy-pasting that block a second time would invite — if a third public page is ever added, extend `PublicPageChrome`, don't copy-paste the block again.

## CSS gotcha: `width/height: 100%` inside an unsized grid cell doesn't reliably fill it

`.photo-frame` in `ScanPage.cs` (`display: grid; place-items: center; overflow: hidden;` with no explicit `grid-template-rows`/`grid-template-columns`) once clipped the bottom of any equipment photo whose aspect ratio wasn't close to the frame's 16:10. The `<img>` had `width: 100%; height: 100%; object-fit: contain;`, which looks like it should always fill the box and letterbox safely — it didn't, because:

1. With no explicit track, the grid's implicit row/column track is auto-sized, so a percentage `height`/`width` on the item resolves against an *indefinite* size and falls back to the image's own intrinsic aspect ratio instead. Fix: give the container `grid-template-rows: 1fr; grid-template-columns: 1fr;` so the track has a definite size.
2. That alone still wasn't enough — even with an explicit `1fr` track, the grid item's *own* automatic minimum size (content-based, from its intrinsic dimensions) can still win over the track and force it to grow to fit the image, overflowing the frame again. `overflow: hidden` on the *container* does not suppress this (that spec carve-out only applies to the container's own auto-min sizing, not a child grid item's). Fix: add `min-width: 0; min-height: 0;` to the item itself — the same fix flexbox needs for the equivalent `min-height: auto` gotcha.

Net working rule for "fill this box, letterboxed, never cropped" inside a `display: grid` cell: the cell needs an explicit track (`1fr`, not `auto`) *and* the child needs `min-width: 0; min-height: 0;` alongside its `width/height: 100%; object-fit: contain;`. Missing either one reproduces the clipping.

**Why a screenshot didn't catch it:** `overflow: hidden` clipped ~35% off the bottom of the rendered image, but for the specific demo icon used during testing, that clipped strip happened to be mostly the icon's own baked-in padding/whitespace — so a full-page screenshot looked correct even though the layout was broken. It only became visually obvious once real equipment content (the pump's base/stand) fell inside the clipped region. Don't trust "the screenshot looks fine" for anything using `overflow: hidden` + percentage sizing — confirm with `browser_evaluate` and `getBoundingClientRect()` on both the container and the content element (see step 6 below).

## Verify a change actually rendered

1. Start the API with `--launch-profile https` (`dotnet run --project src/QrSimple.Api --launch-profile https`) — costs nothing extra even for scan-page-only work, and admin-UI work needs it for sign-in. Don't start a second process if 5078/7040 is already serving. Restarting after an edit: follow the discrete kill → confirm-port-free → relaunch → poll-log procedure in `docs/local-browser-testing.md`, not a single chained command.
2. Confirm Chrome CDP and the Playwright MCP service are reachable with the readiness checks in `docs/local-browser-testing.md`. If not, tell the user to run `./scripts/start-chrome-for-playwright.sh` from a Fedora host terminal — that script cannot run from inside the devcontainer.
3. `browser_navigate` to the changed page, then `browser_take_screenshot` with no `filename` argument — it renders inline in the conversation (the service runs with `--image-responses allow`). An explicit `filename` currently resolves to the wrong path and fails.
4. `browser_resize` to a phone width (~390×844) and screenshot again — every admin page and both public pages must stay usable there; the `@media` blocks in `app.css`/`ScanPage.cs`/`RebuildsPage.cs` are where that's enforced.
5. Check `browser_console_messages` for errors the change introduced.
6. For an exact style claim ("the header is `#156FC1`"), confirm with `browser_evaluate` computed styles — screenshots compress and shift color, computed styles don't. Resolved custom properties are worth checking too: `getComputedStyle(document.documentElement).getPropertyValue("--brand-primary")` catches a `tokens.css` that silently failed to load. For any element inside an `overflow: hidden` container (photo frames, cropped previews), also compare `getBoundingClientRect()` of the content against its container — a screenshot can look fine while content is silently clipped, if the clipped region happens to be blank; see the grid-sizing gotcha above.

A change isn't done until steps 3–6 pass at both widths, signed in if the page requires it.

## Role-gated rendering

Equipment/User/Category pages render differently by role (Admin/Operator/Reader) and `IsActive` — see AGENTS.md. Before concluding a field or button is "missing," check `/me` for the signed-in session's actual role rather than assuming Admin.
