#!/usr/bin/env bash
# Copies Player.log and Player-prev.log from the Windows game PC into build/logs/game/<label>-<time>/.
# Usage: tools/fetch-log-remote.sh <label> [ssh host]   (or set BA_GAME_HOST)
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
LABEL="${1:?label, for example phase-b}"
HOST="${2:-${BA_GAME_HOST:?Pass the ssh host of the Windows game PC, or set BA_GAME_HOST}}"
OUT="$REPO/build/logs/game/$LABEL-$(date +%H%M%S)"
mkdir -p "$OUT"
for f in Player.log Player-prev.log; do
  scp -q "$HOST:AppData/LocalLow/Hovgaard Games/Big Ambitions/$f" "$OUT/$f" < /dev/null || true
done
grep -h "Mod:Food Trucks\|FoodTrucks\|Food Trucks" "$OUT/Player.log" | cut -c1-240 || true
echo "Saved to $OUT"
