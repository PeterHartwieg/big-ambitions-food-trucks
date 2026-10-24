#!/usr/bin/env bash
# Runs Big Ambitions on the Windows game PC for an agent-driven test session (docs/test-bridge.md).
# Usage: tools/run-remote.sh <action> [ssh host]   (or set BA_GAME_HOST)
#   status       is the game running, which local mods are in, is Food Trucks installed or parked
#   start        start the game on the desktop with -console -foodtrucks-test, open the tunnel, wait for the bridge
#   tunnel       open the ssh tunnel to the bridge (127.0.0.1:8330 on both ends), in the background
#   tunnel-stop  close it
#   wait         wait until the bridge answers /health (up to BA_BRIDGE_WAIT seconds, default 180)
#   stop         ask the bridge to quit the game and wait up to 120 s for it to close; never kills
#   stop --force the same, then kill the game if it is still running, but only a game started with -foodtrucks-test
#   lock [minutes] [what]   acquire normal-game ownership (minutes retained for compatibility; no expiry)
# start and stop require the owner token (set BA_LOCK_OWNER and acquire first).
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
ACTION="${1:?status, start, tunnel, tunnel-stop, wait, stop, lock or unlock}"
FORCE=false
if [ "${2:-}" = "--force" ]; then FORCE=true; shift; fi
LOCK_ARGS=()
if [ "$ACTION" = lock ]; then
  # lock [minutes] [what] [ssh host]: the host comes last here, so it can still be left to BA_GAME_HOST.
  [ -n "${2:-}" ] && LOCK_ARGS+=("$2"); [ -n "${3:-}" ] && LOCK_ARGS+=("$3"); set -- "$1" "${4:-}"
fi
HOST="${2:-${BA_GAME_HOST:?Pass the ssh host of the Windows game PC, or set BA_GAME_HOST}}"
PORT=8330
URL="http://127.0.0.1:$PORT"
PIDFILE="$REPO/build/bridge-tunnel.pid"
mkdir -p "$REPO/build"
. "$REPO/tools/game-lock.sh"

remote() {
  scp -q "$REPO/tools/Game-Windows.ps1" "$HOST:Game-FoodTrucks.ps1" < /dev/null
  BA_GAME_HOST="$HOST" python3 "$REPO/tools/bridge-lock" exec --script Game-FoodTrucks.ps1 --parameters "{\"Action\":\"$1\"}"
}

tunnel_up() { [ -f "$PIDFILE" ] && kill -0 "$(cat "$PIDFILE")" 2>/dev/null; }

open_tunnel() {
  if tunnel_up; then echo "Tunnel already open (pid $(cat "$PIDFILE"))."; return; fi
  if lsof -nP -iTCP:$PORT -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Port $PORT is already taken on this Mac (a mock bridge?). Stop that first." >&2; exit 1
  fi
  # Its own output goes to a file: an ssh holding this script's stdout would keep a pipe on run-remote.sh open until the tunnel closes.
  ssh -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 -L "$PORT:127.0.0.1:$PORT" "$HOST" < /dev/null > "$REPO/build/bridge-tunnel.log" 2>&1 &
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
  status)
    remote status
    text="$(lock_text)"; if [ -n "$text" ]; then echo "Lock:"; sed 's/^/  /' <<<"$text"; else echo "Lock: free."; fi
    ;;
  lock) take_lock ${LOCK_ARGS[@]+"${LOCK_ARGS[@]}"} ;;
  unlock) drop_lock ;;
  start)
    require_lock_free_or_ours
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
    require_lock_free_or_ours
    status="$(remote status)"
    if grep -q 'Game: not running' <<<"$status"; then
      echo "Game: not running."
    elif ! grep -q 'with -foodtrucks-test' <<<"$status"; then
      echo "The running game was started without -foodtrucks-test (not a test session). Not stopped." >&2; exit 1
    else
      # A lost reply doesn't mean the quit failed: the game may be finishing a save. Watch the process either way.
      BA_GAME_HOST="$HOST" BA_BRIDGE_URL="$URL" python3 "$REPO/tools/ft" call quit >/dev/null 2>&1 \
        && echo "Asked the game to quit." || echo "No reply to quit (no tunnel, or the game is busy); watching the process."
      closed=false
      for _ in $(seq 24); do
        sleep 5
        if remote status | grep -q 'Game: not running'; then closed=true; break; fi
      done
      if $closed; then echo "Game closed."
      elif $FORCE; then remote stop
      else echo "The game is still running after 120 s (saving, or the bridge isn't answering). Not killed: check the PC, or run stop --force." >&2; exit 1
      fi
    fi
    if tunnel_up; then kill "$(cat "$PIDFILE")"; rm -f "$PIDFILE"; echo "Tunnel closed."; fi
    ;;
  *) echo "Unknown action $ACTION" >&2; exit 2 ;;
esac
