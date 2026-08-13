#!/usr/bin/env bash
set -euo pipefail

readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repo_root="$(cd "${script_dir}/.." && pwd)"
readonly runtime_dir="${QR_SIMPLE_RUNTIME_DIR:-/tmp}"
readonly pid_file="${runtime_dir}/qr-simple-mcp.pid"
readonly log_file="${runtime_dir}/qr-simple-mcp.log"
readonly default_port="8932"

readonly port="${QR_SIMPLE_MCP_PORT:-$default_port}"

if [[ -f "$pid_file" ]]; then
  readonly existing_pid="$(cat "$pid_file" 2>/dev/null || true)"
  if [[ -n "${existing_pid}" ]] && kill -0 "${existing_pid}" 2>/dev/null; then
    echo "qr-simple MCP already running on port ${port} (pid ${existing_pid})."
    exit 0
  fi
fi

mkdir -p "$(dirname "$log_file")"

export QR_SIMPLE_WORKSPACE_ROOT="${repo_root}"
export QR_SIMPLE_API_BASE_URL="${QR_SIMPLE_API_BASE_URL:-http://127.0.0.1:5078}"
export QR_SIMPLE_MCP_PORT="${port}"

(
  cd "$repo_root"
  nohup dotnet run --project src/QrSimple.Mcp >/dev/null 2>"$log_file" &
  echo $! >"$pid_file"
)

echo "Started qr-simple MCP on http://127.0.0.1:${port}/mcp"
echo "Log: ${log_file}"
