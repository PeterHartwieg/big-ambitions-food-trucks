#!/usr/bin/env bash
# Renders icons in the game's item icon style (art/tools/render_icon.py: 512 px, transparent, front-left view):
# - every compact equipment model, as art/equipment/<Name>/Icon_<Name>.png (the order lists' pictures; the build
#   copies them into Vehicles/EquipmentArt/Icons);
# - the mod's own items, into unity/FoodTrucks/Items/Icons/<id>.png, or into the folder given.
# Usage: art/tools/render_icons.sh [items_out_dir]
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
OUT="${1:-$REPO/unity/FoodTrucks/Items/Icons}"
mkdir -p "$OUT"
icon() { # icon <model folder> <out.png>
  local dir="$1" name
  name="$(basename "$dir")"
  "$BLENDER" -b --factory-startup --python "$REPO/art/tools/render_icon.py" -- \
    "$dir/$name.fbx" "$dir/T_${name}_BC.png" "$2" 2>&1 | grep -E "^ICON|Error" || true
}
for dir in "$REPO"/art/equipment/*/; do
  dir="${dir%/}"
  icon "$dir" "$dir/Icon_$(basename "$dir").png"
done
# <item id> <art folder>: the item's icon name is the part of its ID after "itemname_".
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
