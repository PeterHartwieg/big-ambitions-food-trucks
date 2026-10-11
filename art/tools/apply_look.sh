#!/usr/bin/env bash
# Runs the surface-look step (art/tools/game_look.py) and then the scorer on model folders.
# Usage: art/tools/apply_look.sh [art/<folder> | art/<folder>/<Name>.spec.json ...]
#   A folder holds one model (its spec.json); a spec file names one of several models in a folder (the commissary).
#   No arguments: the cart, the truck, all equipment and products, the props and the commissary props.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
BLENDER="${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}"
cd "$REPO"
if [ $# -eq 0 ]; then
  set -- art/cart art/truck art/equipment/*/ art/products/*/ art/props/*/ art/commissary/*.spec.json
fi
failed=0
for arg in "$@"; do
  arg="${arg%/}"
  if [ -f "$arg" ]; then dir="$(dirname "$arg")"; spec="$arg"; else dir="$arg"; spec="$arg/spec.json"; fi
  [ -f "$spec" ] || { echo "SKIP $arg (no spec.json)"; continue; }   # e.g. art/equipment/renders
  name="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["name"])' "$spec")"
  "$BLENDER" -b "$dir/$name.blend" --python art/tools/game_look.py -- "$dir" "$spec" 2>&1 | grep -E "^LOOK|Error|Traceback" || true
  if "$BLENDER" -b "$dir/$name.blend" --python art/tools/score.py -- "$spec" 2>&1 | grep -q "SCORE PASS"; then
    echo "SCORE PASS $name"
  else
    echo "SCORE FAIL $name (see the score file next to $spec)"; failed=1
  fi
done
exit $failed
