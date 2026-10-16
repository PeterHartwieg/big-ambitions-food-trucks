#!/usr/bin/env bash
# Renders the BizMan vehicle page's vehicle drawings in layers (art/tools/render_diagrams.py): the truck and the cart
# with empty places (unity/FoodTrucks/UI/VehicleDiagram_*.png), one layer per place and display model
# (VehicleLayer_*.png), data/vehicle-diagrams.json, and build/diagram-check.png with some places filled. Old layers are
# removed first; a new PNG gets a .meta (Sprite, 2048, bundle foodtrucks) with a fresh GUID, existing metas are kept.
# Usage: art/tools/render_diagrams.sh [art_dir]   (art_dir defaults to this checkout's art/)
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
