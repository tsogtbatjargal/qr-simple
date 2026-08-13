#!/usr/bin/env bash
set -euo pipefail

# Never attach browser automation to the owner's normal Chrome profile. This
# throwaway profile can be deleted safely and must be started again after a
# reboot or after Chrome is closed. Start/restart the local MCP service first
# so this one host-side command prepares the complete browser-control path.
readonly script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

# The existing local database container normally survives reboots in a stopped
# state. Start it when present; if it has not been created yet, let the user or
# agent follow the runbook instead of inventing credentials here.
if podman container exists qr-simple-db; then
  podman start qr-simple-db >/dev/null
else
  printf '%s\n' \
    "warning: qr-simple-db does not exist; see docs/local-browser-testing.md" \
    >&2
fi

"$script_dir/start-playwright-mcp-service.sh" >/dev/null

exec flatpak run com.google.Chrome \
  --remote-debugging-port=9222 \
  --user-data-dir=/tmp/qr-simple-chrome-playwright-profile \
  --no-first-run \
  about:blank
