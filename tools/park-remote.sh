#!/usr/bin/env bash
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
ACTION="${1:?park or restore}"
HOST="${2:-${BA_GAME_HOST:?Pass the ssh host of the Windows game PC, or set BA_GAME_HOST}}"
scp -q "$REPO/tools/Park-Windows.ps1" "$HOST:Park-FoodTrucks.ps1" < /dev/null
BRIDGE_TOOLS="$REPO/tools"
BA_GAME_HOST="$HOST" python3 "$BRIDGE_TOOLS/bridge-lock" exec --script Park-FoodTrucks.ps1 --parameters "{\"Action\":\"$ACTION\"}"
