#!/usr/bin/env bash
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
ART="${1:-$REPO/art}"
UI="$REPO/unity/FoodTrucks/UI"
mkdir -p "$UI" "$REPO/build"
rm -f "$UI"/VehicleLayer_*.png
"$BLENDER" -b --factory-startup --python "$REPO/art/tools/render_diagrams.py" -- \
  "$ART" "$UI" "$REPO/data/vehicle-diagrams.json" "$REPO/data/economy.json" "$REPO/build/diagram-check.png" \
  2>&1 | grep -E "^DIAGRAM|^CHECK|^EMPTY|Error|Traceback" || true
python3 "$REPO/art/tools/diagram_metas.py" "$UI"
