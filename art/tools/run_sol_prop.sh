#!/usr/bin/env bash
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$(cd "$1" && pwd)"
PORT="${2:?port}"
shift 2
IMAGES=()
for img in "$@"; do IMAGES+=(-i "$img"); done
PROMPT="$(sed -e "s#{REPO}#$REPO#g" -e "s#{OUT}#$OUT#g" "$OUT/prompt.md")"
START=$(date +%s)
echo "start $(date '+%Y-%m-%d %H:%M:%S') port $PORT" > "$OUT/codex.log"
codex exec -m gpt-6.1-sol -c model_reasoning_effort='"high"' -s workspace-write -C "$REPO" \
    -c mcp_servers.blender.default_tools_approval_mode='"approve"' \
    -c "mcp_servers.blender.env={BLENDER_MCP_PORT=\"$PORT\"}" \
    -o "$OUT/report.md" ${IMAGES[@]+"${IMAGES[@]}"} -- "$PROMPT" < /dev/null >> "$OUT/codex.log" 2>&1 || echo "codex exit $?" >> "$OUT/codex.log"
echo "end $(date '+%Y-%m-%d %H:%M:%S'), $(( ($(date +%s) - START) / 60 )) min" >> "$OUT/codex.log"
