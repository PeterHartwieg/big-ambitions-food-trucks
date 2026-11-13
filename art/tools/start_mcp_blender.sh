#!/usr/bin/env bash
set -euo pipefail
PORT="${1:?port, for example 9877}"
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER=/Applications/Blender.app/Contents/MacOS/Blender
EXPR="
import bpy
bpy.context.preferences.use_preferences_save = False
def _start():
    import bl_ext.user_default.mcp.mcp_to_blender_server as srv
    if srv.is_running():
        bpy.ops.blmcp.server_stop()
    prefs = bpy.context.preferences.addons['bl_ext.user_default.mcp'].preferences
    prefs.port = $PORT
    prefs.use_autostart = False
    bpy.ops.blmcp.server_start()
    print('MCP bridge on port', prefs.port)
bpy.app.timers.register(_start, first_interval=3.0)
"
nohup "$BLENDER" "$REPO/art/start.blend" --python-expr "$EXPR" > "${TMPDIR:-/tmp}/blender-mcp-$PORT.log" 2>&1 &
for _ in $(seq 1 60); do
    if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN > /dev/null 2>&1; then echo "Blender MCP bridge listening on $PORT"; exit 0; fi
    sleep 1
done
echo "No bridge on $PORT after 60 s; see ${TMPDIR:-/tmp}/blender-mcp-$PORT.log" >&2
exit 1
