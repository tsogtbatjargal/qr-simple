# ICS Mongolia Brand Kit

Shared brand/theme foundation for ICS Mongolia products (qr-simple, and future projects).

## Contents

- `tokens.css` — CSS custom properties (colors, radius, shadow, font). Copy these
  `:root` declarations into a project's stylesheet, or link this file directly.
  Primary blue (`#166fc0`) is extracted from the actual ICS logo mark — treat it
  as the source of truth over any color that's crept into a specific site's CSS.
- `logos/` — official logo + favicon set, as PNG (no SVG source currently exists).
- `icons/` — a starter subset of Lucide icons (MIT licensed, https://lucide.dev),
  as plain SVG files. Pull more from the same set as needed — don't mix icon
  families within one product.

## Using this in a project

1. Copy this folder (or the pieces you need) into the project's static-asset
   directory (e.g. a Blazor app's `wwwroot/`).
2. Reference `var(--brand-*)` tokens instead of hardcoding hex values.
3. Icons are single-color SVGs using `currentColor` — set `color` on a wrapping
   element to recolor them, don't edit the SVG fill directly.

## Status

Foundation only, as of 2026-08-25. qr-simple has these files copied into
`src/QrSimple.Api/wwwroot/brand/`, but its CSS (`app.css` / `ScanPage.cs`)
hasn't been rewired to consume the new tokens yet — that's separate follow-up
work, done locally rather than on this container.
