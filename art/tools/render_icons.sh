#!/usr/bin/env bash
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
OUT="${1:-$REPO/unity/FoodTrucks/Items/Icons}"
mkdir -p "$OUT"
icon() {
  local dir="$1" name
  name="$(basename "$dir")"
  "$BLENDER" -b --factory-startup --python "$REPO/art/tools/render_icon.py" -- \
    "$dir/$name.fbx" "$dir/T_${name}_BC.png" "$2" 2>&1 | grep -E "^ICON|Error" || true
}
for dir in "$REPO"/art/equipment/*/; do
  dir="${dir%/}"
  icon "$dir" "$dir/Icon_$(basename "$dir").png"
done
while read -r id dir; do
  icon "$REPO/art/$dir" "$OUT/$id.png"
done <<'LIST'
dumplings products/Dumplings
tacos products/Tacos
choppedcheese products/ChoppedCheese
grilledcorn products/CornCob
dumplingsteamer equipment/DumplingSteamer
griddle equipment/Griddle
corngrill equipment/CornGrill
storagecabinet equipment/StorageCabinet
LIST
