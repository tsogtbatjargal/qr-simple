# Devcontainer agent MCP

This is the project-local MCP used by the new Codex agent inside the `qr-simple` devcontainer.

It exists to give the agent stable read-only tools for the repo and app without depending on the host-only Playwright/browser setup.

## What it exposes

- `workspace_search` - search text files in the workspace.
- `workspace_read` - read a workspace file with line numbers.
- `app_health` - check whether the API is reachable and inspect `/categories`.
- `route_inventory` - summarize the HTTP routes declared in `src/QrSimple.Api/Program.cs`.
- `route_auth_summary` - compact view of routes that require auth and their roles.
- `latest_test_failures` - inspect the newest TRX file and summarize test failures.

## Start it

Run this from a terminal inside the devcontainer:

```bash
./scripts/start-qr-simple-mcp.sh
```

The server listens on `http://127.0.0.1:8932/mcp` and the Codex config in `.codex/config.toml` points to that endpoint.

If you want to override the port or API base URL:

```bash
QR_SIMPLE_MCP_PORT=8932 QR_SIMPLE_API_BASE_URL=http://127.0.0.1:5078 ./scripts/start-qr-simple-mcp.sh
```

## Readiness check

From the devcontainer, a plain GET should return an HTTP 400 response because the MCP server is reachable but expects a protocol request:

```bash
curl -sS -o /dev/null -w 'MCP HTTP %{http_code}\n' http://127.0.0.1:8932/mcp
```

## New-agent prompt

Use this when opening a fresh Codex agent in the devcontainer:

```text
You are running inside the qr-simple VS Code devcontainer. Read AGENTS.md and docs/local-agent-mcp.md completely before taking action.

Start by calling the project-local MCP tools to inspect the workspace and app state. Use workspace_search and workspace_read before guessing about file locations. Use app_health, route_inventory, route_auth_summary, and latest_test_failures to confirm the API is up and to understand the current HTTP surface and the most recent test state.

Do not rebuild the devcontainer just because the MCP is missing; check whether ./scripts/start-qr-simple-mcp.sh has been run in this devcontainer first. If the API is down but the database is reachable, start the API from the devcontainer with:
dotnet run --project src/QrSimple.Api --launch-profile http

Keep the existing Playwright/browser MCP separate. This MCP is only for repo and app inspection inside the devcontainer.
```
