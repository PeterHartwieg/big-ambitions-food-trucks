#!/usr/bin/env bash
# Runs the surface-look step (art/tools/game_look.py) and then the scorer on model folders.
# Usage: art/tools/apply_look.sh [art/<folder> ...]   (no arguments: the cart, the truck, all equipment and products)
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
cd "$REPO"
if [ $# -eq 0 ]; then
  set -- art/cart art/truck art/equipment/*/ art/products/*/
fi
failed=0
for dir in "$@"; do
  dir="${dir%/}"
  name="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["name"])' "$dir/spec.json")"
  "$BLENDER" -b "$dir/$name.blend" --python art/tools/game_look.py -- "$dir" 2>&1 | grep -E "^LOOK|Error|Traceback" || true
  if "$BLENDER" -b "$dir/$name.blend" --python art/tools/score.py -- "$dir/spec.json" 2>&1 | grep -q "SCORE PASS"; then
    echo "SCORE PASS $name"
  else
    echo "SCORE FAIL $name (see $dir/score.json)"; failed=1
  fi
done
exit $failed
