#!/usr/bin/env bash
set -euo pipefail

# Run Playwright MCP as a host-networked HTTP service. Both Fedora Codex and
# VS Code Codex inside the host-networked devcontainer can reach this endpoint.
# Node stays isolated in this disposable service container.
readonly script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly repo_root="$(dirname -- "$script_dir")"
readonly node_image="docker.io/library/node:22-bookworm-slim"
readonly npm_cache_volume="qr-simple-playwright-npm-cache"
readonly cdp_endpoint="${QR_SIMPLE_CHROME_CDP_ENDPOINT:-http://127.0.0.1:9222}"
readonly service_name="qr-simple-playwright-mcp"
# @playwright/mcp saves screenshots/PDFs/videos into this container's own
# --output-dir (default: its process cwd), which lives only in this disposable
# container's writable layer -- not reachable from the devcontainer or the
# repo checkout. Bind-mounting a directory *inside the repo* fixes that without
# touching .devcontainer/devcontainer.json (which would need a rebuild): the
# whole repo is already the devcontainer's workspace mount, so anything
# written here is visible from both a Fedora host terminal and the
# devcontainer at the same relative path. Verified 2026-08-16.
readonly output_dir="${repo_root}/.playwright-mcp-output"
mkdir -p "$output_dir"
# The container runs as root, which rootless Podman maps to a subordinate
# host UID (not $(id -u)) without --userns=keep-id -- same class of mismatch
# AGENTS.md documents for root-owned obj/bin from host-side dotnet runs, just
# in the other direction. world-writable is fine here: this holds only
# disposable, gitignored screenshots/PDFs/videos, nothing sensitive.
chmod 777 "$output_dir"
# 777 alone still isn't enough on this Fedora Silverblue host: SELinux denies
# the container access to a bind-mounted directory that isn't labeled for
# container use, regardless of Unix permissions (verified: plain chmod 777
# still produced "Permission denied" for a root-in-container `ls`/`touch`).
# The ":Z" volume suffix below tells Podman to relabel the host directory
# with a private container_file_t context. Unlike the podman-socket mount in
# devcontainer.json (which explicitly avoids ":Z" because relabeling breaks
# the HOST's own access to a resource it also needs), this directory exists
# only for this container's output, so relabeling it is safe.

podman run \
  --detach \
  --replace \
  --name "$service_name" \
  --restart unless-stopped \
  --network host \
  --volume "${npm_cache_volume}:/root/.npm" \
  --volume "${output_dir}:/output:Z" \
  "$node_image" \
  npx --yes @playwright/mcp@latest \
  --host 127.0.0.1 \
  --port 8931 \
  --allowed-hosts 127.0.0.1:8931,localhost:8931 \
  --cdp-endpoint "$cdp_endpoint" \
  --output-dir /output \
  --image-responses allow
