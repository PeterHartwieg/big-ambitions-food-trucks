#!/usr/bin/env bash
# Runs Big Ambitions on the Windows game PC for an agent-driven test session (docs/test-bridge.md).
# Usage: tools/run-remote.sh <action> [ssh host]   (or set BA_GAME_HOST)
#   status       is the game running, which local mods are in, is Food Trucks installed or parked
#   start        start the game on the desktop with -console -foodtrucks-test, open the tunnel, wait for the bridge
#   tunnel       open the ssh tunnel to the bridge (127.0.0.1:8322 on both ends), in the background
#   tunnel-stop  close it
#   wait         wait until the bridge answers /health (up to BA_BRIDGE_WAIT seconds, default 180)
#   stop         ask the bridge to quit the game; kill it if it is still running after 30 s
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
ACTION="${1:?status, start, tunnel, tunnel-stop, wait or stop}"
HOST="${2:-${BA_GAME_HOST:?Pass the ssh host of the Windows game PC, or set BA_GAME_HOST}}"
PORT=8322
URL="http://127.0.0.1:$PORT"
PIDFILE="$REPO/build/bridge-tunnel.pid"
mkdir -p "$REPO/build"

remote() {
  scp -q "$REPO/tools/Game-Windows.ps1" "$HOST:Game-FoodTrucks.ps1" < /dev/null
  ssh "$HOST" "powershell -NoProfile -ExecutionPolicy Bypass -File Game-FoodTrucks.ps1 -Action $1" < /dev/null
}

tunnel_up() { [ -f "$PIDFILE" ] && kill -0 "$(cat "$PIDFILE")" 2>/dev/null; }

open_tunnel() {
  if tunnel_up; then echo "Tunnel already open (pid $(cat "$PIDFILE"))."; return; fi
  if lsof -nP -iTCP:$PORT -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Port $PORT is already taken on this Mac (a mock bridge?). Stop that first." >&2; exit 1
  fi
  ssh -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 -L "$PORT:127.0.0.1:$PORT" "$HOST" < /dev/null &
  echo $! > "$PIDFILE"
  sleep 2
  tunnel_up || { echo "The tunnel to $HOST did not open." >&2; rm -f "$PIDFILE"; exit 1; }
  echo "Tunnel open: $URL -> $HOST (pid $(cat "$PIDFILE"))."
}

wait_bridge() {
  local limit="${BA_BRIDGE_WAIT:-180}" waited=0
  until curl -fsS --max-time 3 "$URL/health" 2>/dev/null; do
    if [ "$waited" -ge "$limit" ]; then
      echo "No answer from the bridge after $limit s. Is Food Trucks installed (not parked), and did the game start with -foodtrucks-test? Its Player.log says 'Test bridge: …'." >&2
      exit 1
    fi
    sleep 3; waited=$((waited + 3))
  done
  echo
}

case "$ACTION" in
  status) remote status ;;
  start)
    remote start
    open_tunnel
    wait_bridge
    ;;
  tunnel) open_tunnel ;;
  tunnel-stop)
    if tunnel_up; then kill "$(cat "$PIDFILE")"; echo "Tunnel closed."; else echo "No tunnel open."; fi
    rm -f "$PIDFILE"
    ;;
  wait) wait_bridge ;;
  stop)
    if curl -fsS --max-time 5 -X POST -H 'Content-Type: application/json' -d '{"cmd":"quit","args":{}}' "$URL/call" >/dev/null 2>&1; then
      echo "Asked the game to quit."
      for _ in $(seq 10); do
        sleep 3
        remote status | grep -q 'Game: not running' && { echo "Game closed."; break; }
      done
    fi
    remote stop
    if tunnel_up; then kill "$(cat "$PIDFILE")"; rm -f "$PIDFILE"; echo "Tunnel closed."; fi
    ;;
  *) echo "Unknown action $ACTION" >&2; exit 2 ;;
esac
