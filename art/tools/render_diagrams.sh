#!/usr/bin/env bash
# Renders the BizMan vehicle page's equipment diagrams (art/tools/render_diagrams.py): the truck and the cart
# with empty equipment places, into unity/FoodTrucks/UI/VehicleDiagram_*.png, and the places' image positions
# into data/vehicle-diagrams.json. Writes build/diagram-check.png with the places circled, for a look.
# Usage: art/tools/render_diagrams.sh [art_dir]   (art_dir defaults to this checkout's art/; a worktree without
# the binaries can pass the main checkout's)
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
ART="${1:-$REPO/art}"
mkdir -p "$REPO/unity/FoodTrucks/UI" "$REPO/build"
"$BLENDER" -b --factory-startup --python "$REPO/art/tools/render_diagrams.py" -- \
  "$ART" "$REPO/unity/FoodTrucks/UI" "$REPO/data/vehicle-diagrams.json" "$REPO/build/diagram-check.png" \
  2>&1 | grep -E "^DIAGRAM|^CHECK|Error|Traceback" || true
