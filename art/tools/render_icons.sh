#!/usr/bin/env bash
# Renders the mod's item icons (art/tools/render_icon.py) into unity/FoodTrucks/Items/Icons, or into the folder given.
# Usage: art/tools/render_icons.sh [out_dir]
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
OUT="${1:-$REPO/unity/FoodTrucks/Items/Icons}"
mkdir -p "$OUT"
# <item id> <art folder>: the item's icon name is the part of its ID after "itemname_".
while read -r id dir; do
  name="$(basename "$dir")"
  "$BLENDER" -b --factory-startup --python "$REPO/art/tools/render_icon.py" -- \
    "$REPO/art/$dir/$name.fbx" "$REPO/art/$dir/T_${name}_BaseColor.png" "$OUT/$id.png" 2>&1 | grep -E "^ICON|Error" || true
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
