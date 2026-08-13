#!/usr/bin/env bash
set -euo pipefail

# Run Playwright MCP as a host-networked HTTP service. Both Fedora Codex and
# VS Code Codex inside the host-networked devcontainer can reach this endpoint.
# Node stays isolated in this disposable service container.
readonly node_image="docker.io/library/node:22-bookworm-slim"
readonly npm_cache_volume="qr-simple-playwright-npm-cache"
readonly cdp_endpoint="${QR_SIMPLE_CHROME_CDP_ENDPOINT:-http://127.0.0.1:9222}"
readonly service_name="qr-simple-playwright-mcp"

podman run \
  --detach \
  --replace \
  --name "$service_name" \
  --restart unless-stopped \
  --network host \
  --volume "${npm_cache_volume}:/root/.npm" \
  "$node_image" \
  npx --yes @playwright/mcp@latest \
  --host 127.0.0.1 \
  --port 8931 \
  --allowed-hosts 127.0.0.1:8931,localhost:8931 \
  --cdp-endpoint "$cdp_endpoint"
