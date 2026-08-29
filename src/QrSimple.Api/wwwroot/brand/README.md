# ICS Mongolia Brand Kit

Shared brand/theme foundation for ICS Mongolia products (qr-simple, and future projects).

## Contents

- `tokens.css` — CSS custom properties (colors, radius, shadow, font) **and** the
  `@font-face` rules for the self-hosted Inter files. Link this file and you get
  both the palette and the typeface. Primary blue (`#156FC1`) is the company's
  stated brand colour and matches the logo mark — treat it as the source of truth
  over any colour that's crept into a specific app's CSS. Orange (`#ff7a1a`) was
  the old primary and is fully retired; do not reintroduce it.
- `fonts/` — Inter as variable woff2, one file per unicode subset. `cyrillic-ext`
  is not optional: Mongolian's Ө (U+04E8) and Ү (U+04AE) fall outside the base
  `cyrillic` range and drop to a fallback font without it.
- `logos/` — official logo + favicon set, as PNG (no SVG source currently exists).
  `ics-logo.png` is 1381×678 and dark-on-transparent, so it needs a light plate
  behind it rather than being placed straight onto the blue header.
- `icons/` — a subset of Lucide icons (ISC licensed, https://lucide.dev), as plain
  SVG files. Pull more from the same set as needed — don't mix icon families
  within one product.

## Using this in a project

1. Copy this folder (or the pieces you need) into the project's static-asset
   directory (e.g. a Blazor app's `wwwroot/`). The `@font-face` URLs are relative
   to `tokens.css`, so copying the folder wholesale keeps them resolving.
2. Link `tokens.css` **before** the app stylesheet, then reference `var(--brand-*)`
   tokens instead of hardcoding hex values.
3. Icons are single-color SVGs using `currentColor`. Inline them rather than using
   `<img>` so `color` and font-size can drive them; see
   `Components/Shared/Icon.razor` for how qr-simple does it. Setting `fill: none;
   stroke: currentColor` explicitly is required — inline SVG otherwise defaults to
   `fill: black; stroke: none` and renders a stroke-only path as a solid blob.

## Status

Wired up as of 2026-08-28. Both qr-simple surfaces — the admin UI (`wwwroot/app.css`)
and the public scan page (`ScanPage.cs`) — link `tokens.css` and consume the tokens;
neither carries a literal hex value any more. The favicons, header logo, and Inter
are all live.

Upstream master copy of this kit lives outside the repo at
`/home/tsogo/icsmongolia/brand-kit/`; keep the two in sync when either changes.
That path is on a separate, not-version-controlled LXC container named
`lxc-ics-mongolia`, not on this dev host/devcontainer's own filesystem — reach it
with plain `ssh lxc-ics-mongolia`. Verified 2026-08-28: `tokens.css` and every
runtime asset qr-simple actually references (fonts, icons, the shipped logos/
favicons) are byte-identical between the two copies. The master also carries a
few files qr-simple intentionally doesn't ship (another product's logo, two
master-resolution source PNGs the shipped logo/favicons are derived from) —
that's expected, not drift to fix.
