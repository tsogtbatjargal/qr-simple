#!/usr/bin/env bash
set -euo pipefail

# Never attach browser automation to the owner's normal Chrome profile. This
# throwaway profile can be deleted safely. Start/restart the local MCP service
# first so this one host-side command prepares the complete browser-control path.
#
# This script does NOT exec Chrome and return; it stays in the foreground as a
# keeper loop (see the bottom of the file for why). Leave the terminal running,
# or use --detach.
readonly script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly cdp_port=9222
readonly chrome_profile=/tmp/qr-simple-chrome-playwright-profile

cdp_is_up() {
  (exec 3<>"/dev/tcp/127.0.0.1/${cdp_port}") 2>/dev/null
}

detach=0
case "${1:-}" in
  --detach) detach=1 ;;
  "") ;;
  *)
    printf 'usage: %s [--detach]\n' "${BASH_SOURCE[0]}" >&2
    exit 2
    ;;
esac

# Starting a second Chrome on the same profile/port fights the first one for 9222
# and leaves CDP flapping. Same principle as "don't start a second API process".
if cdp_is_up; then
  printf 'Chrome CDP is already listening on %s; not starting a second instance.\n' "$cdp_port" >&2
  printf 'Verify with: curl -s http://127.0.0.1:%s/json/version\n' "$cdp_port" >&2
  exit 0
fi

# An agent driving this from the host needs the keeper to outlive the shell that
# launched it. Without setsid the keeper dies with the calling process group and
# CDP goes down mid-run. Re-execs this same script without --detach, so there is
# no recursion.
if (( detach )); then
  log="${QR_SIMPLE_CHROME_LOG:-/tmp/qr-simple-chrome-keeper.log}"
  setsid nohup "${BASH_SOURCE[0]}" >"$log" 2>&1 </dev/null &
  disown || true
  printf 'Started detached. Log: %s\n' "$log"
  printf 'Stop it with: pkill -f "start-chrome-for-playwrigh[t]"\n'
  exit 0
fi

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

trap 'printf "\nChrome keeper stopped.\n" >&2; exit 0' INT TERM

# Chrome quits when its last tab closes, and Playwright closes pages as it drives
# the browser -- so an ordinary `exec flatpak run chrome` takes CDP down partway
# through a verification run, with no crash in the log to explain it. That cost two
# manual restarts on 2026-08-29 before the cause was spotted.
#
# --keep-alive-for-test is Chrome's own automation flag for exactly this case: the
# browser process stays alive with zero windows. The loop covers every other exit
# (window closed by hand, real crash). Ctrl+C stops both.
consecutive_fast_exits=0
while true; do
  started_at=$SECONDS

  flatpak run com.google.Chrome \
    --remote-debugging-port="$cdp_port" \
    --user-data-dir="$chrome_profile" \
    --no-first-run \
    --no-default-browser-check \
    --keep-alive-for-test \
    about:blank || true

  # A Chrome that dies instantly, repeatedly, is a real failure (missing Flatpak,
  # broken profile) -- restarting it forever just hides the error.
  if (( SECONDS - started_at < 5 )); then
    consecutive_fast_exits=$(( consecutive_fast_exits + 1 ))
  else
    consecutive_fast_exits=0
  fi

  if (( consecutive_fast_exits >= 5 )); then
    printf 'Chrome exited immediately 5 times in a row; giving up. Check the output above.\n' >&2
    exit 1
  fi

  printf '[keeper] Chrome exited at %s; restarting in 3s (Ctrl+C to stop).\n' \
    "$(date -Is)" >&2
  sleep 3
done
