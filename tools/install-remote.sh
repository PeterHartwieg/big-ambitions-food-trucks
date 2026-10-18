#!/usr/bin/env bash
# Copies build/FoodTrucks to the Windows game PC over ssh and installs it into ModsLocal.
# Usage: tools/install-remote.sh [ssh host]   (or set BA_GAME_HOST)
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
HOST="${1:-${BA_GAME_HOST:?Pass the ssh host of the Windows game PC, or set BA_GAME_HOST}}"
PKG="$REPO/build/FoodTrucks"
[ -f "$PKG/FoodTrucks.dll" ] || { echo "Build first: tools/build-mod.sh" >&2; exit 1; }
. "$REPO/tools/game-lock.sh"
require_lock_free_or_ours
STAGE="FoodTrucks-staged-$(date +%Y%m%d%H%M%S)"
scp -q -r "$PKG" "$HOST:$STAGE" < /dev/null
scp -q "$REPO/build/FoodTrucks.sha256" "$HOST:$STAGE.sha256" < /dev/null
scp -q "$REPO/tools/Install-Windows.ps1" "$HOST:Install-FoodTrucks.ps1" < /dev/null
BA_GAME_HOST="$HOST" python3 "$REPO/tools/bridge-lock" exec --script Install-FoodTrucks.ps1 --parameters "{\"Source\":\"$STAGE\",\"Manifest\":\"$STAGE.sha256\"}"
