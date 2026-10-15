#!/usr/bin/env bash
# Builds the Food Trucks mod package with the official SDK in Unity batch mode.
# Usage: tools/build-mod.sh [--generate]   (--generate re-creates the generated Unity assets first)
# Output: build/FoodTrucks/ (the folder that goes into the game's ModsLocal).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
# A dedicated SDK clone, so other mods in a personal SDK project cannot break this build.
SDK="${BA_SDK:-$REPO/build/sdk}"
SDK_URL="https://github.com/hovgaardgames/bigambitions.git"
SDK_COMMIT="1e03ddd5071b77a32cfd8bba89f0c130ba6c7d73"
GAME_DLLS="${BA_GAME_DLLS:-$REPO/build/game-dlls}"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity}"
HARMONY_URL="https://api.nuget.org/v3-flatcontainer/lib.harmony/2.4.2/lib.harmony.2.4.2.nupkg"
HARMONY_SHA256="d64592e53090464559fce48612c9ca7c8dc73113841376b7aa3455f46fc5d579"
HARMONY_DLL_SHA256="7b9e756306fa3d7620e02a857c8927a6ab04973f9bd8a77d3866700a6deac55c"
MOD_DIR="$REPO/unity/FoodTrucks"
DEPS="$MOD_DIR/Dependencies"
LOG_DIR="$REPO/build/logs"
mkdir -p "$LOG_DIR" "$DEPS"

[ -x "$UNITY" ] || { echo "Unity 2022.3.62f2 not found at $UNITY" >&2; exit 1; }
[ -f "$GAME_DLLS/BigAmbitions.dll" ] || { echo "Game DLLs not found in $GAME_DLLS; run tools/fetch-game-dlls.sh" >&2; exit 1; }
if [ ! -d "$SDK/.git" ]; then
  git clone -q "$SDK_URL" "$SDK"
  git -C "$SDK" checkout -q "$SDK_COMMIT"
fi
# Refuse a clone at another commit or with edited SDK sources, rather than resetting it.
[ "$(git -C "$SDK" rev-parse HEAD)" = "$SDK_COMMIT" ] || { echo "$SDK is not at the pinned SDK commit $SDK_COMMIT" >&2; exit 1; }
# The settings files Unity rewrites during a build (see run_unity); checked here like every input, so the restore
# after the build can never discard a real change.
SDK_RESAVED=("Assets/Settings/HDRP Balanced.asset" "Assets/Settings/HDRP High Fidelity.asset" "Assets/Settings/HDRP Performant.asset" ProjectSettings/GraphicsSettings.asset ProjectSettings/QualitySettings.asset)
SDK_INPUTS=(Assets/Editor Assets/Mods/Example-Vehicle "Assets/_BaDependencies/Vehicle Dependencies" Packages ProjectSettings "${SDK_RESAVED[@]}")
if [ -n "$(git -C "$SDK" status --porcelain --untracked-files=no -- "${SDK_INPUTS[@]}")" ]; then
  echo "$SDK has local changes in ${SDK_INPUTS[*]}; refusing to build" >&2; exit 1
fi
# The SDK compiles against the installed game's DLLs; copy only changed files to avoid reimports.
rsync -a --checksum "$GAME_DLLS/" "$SDK/Assets/_BaDependencies/GameDlls/" --include='*.dll' --exclude='*'
python3 "$REPO/tools/write-dll-metas.py" "$SDK/Assets/_BaDependencies/GameDlls"


# Harmony and its required notice, recovered together from the checksum-pinned package. Never committed.
if [ ! -f "$DEPS/0Harmony.dll" ] || [ ! -f "$REPO/build/Harmony-LICENSE.txt" ]; then
  tmp="$(mktemp -d)"
  curl -fsSL "$HARMONY_URL" -o "$tmp/harmony.nupkg"
  echo "$HARMONY_SHA256  $tmp/harmony.nupkg" | shasum -a 256 -c - >/dev/null
  unzip -q -o "$tmp/harmony.nupkg" 'lib/net472/0Harmony.dll' LICENSE -d "$tmp"
  cp "$tmp/lib/net472/0Harmony.dll" "$DEPS/0Harmony.dll"
  cp "$tmp/LICENSE" "$REPO/build/Harmony-LICENSE.txt"
  rm -rf "$tmp"
fi
echo "$HARMONY_DLL_SHA256  $DEPS/0Harmony.dll" | shasum -a 256 -c - >/dev/null || { echo "Cached 0Harmony.dll does not match the pinned hash; delete it and rebuild" >&2; exit 1; }

# Core, compiled for netstandard2.1 and shipped next to the mod DLL.
dotnet build "$REPO/Core/FoodTrucks.Core.csproj" -c Release -nologo -v quiet
cp "$REPO/Core/bin/Release/netstandard2.1/FoodTrucks.Core.dll" "$DEPS/FoodTrucks.Core.dll"

# The truck's final model and textures from art/, copied in so the generator can build the prefab.
# Only their .meta files are committed (stable GUIDs for the prefab); the binaries stay in art/.
# The game finds a vehicle's prefab by file name in any bundle, so the model goes in as FoodTruck_Model.fbx:
# under its own name it could be picked instead of FoodTruck.prefab, and spawning it fails.
TRUCK_ART="$MOD_DIR/Vehicles/TruckArt"
mkdir -p "$TRUCK_ART"
rm -f "$TRUCK_ART/FoodTruck.fbx" "$TRUCK_ART/FoodTruck.fbx.meta"
rsync -a --checksum "$REPO/art/truck/FoodTruck.fbx" "$TRUCK_ART/FoodTruck_Model.fbx"
# The surface maps from art/tools/game_look.py (BC, M, N) and the lights; the build script's own D and MaskMap
# are only its sources (art/README.md, "Surface look").
for suffix in BC M N E EM_1 EM_2; do
  rsync -a --checksum "$REPO/art/truck/T_FoodTruck_$suffix.png" "$TRUCK_ART/"
done
rm -f "$TRUCK_ART"/T_FoodTruck_D.png* "$TRUCK_ART"/T_FoodTruck_MaskMap.png*
# The cart's, the same way.
CART_ART="$MOD_DIR/Vehicles/CartArt"
mkdir -p "$CART_ART"
rm -f "$CART_ART/FoodCart.fbx" "$CART_ART/FoodCart.fbx.meta" "$CART_ART"/T_FoodCart_BaseColor.png*
rsync -a --checksum "$REPO/art/cart/FoodCart.fbx" "$CART_ART/FoodCart_Model.fbx"
rsync -a --checksum "$REPO"/art/cart/T_FoodCart_{BC,M,N}.png "$CART_ART/"
# And the equipment models shown on the cart's counter, one folder each in art/equipment, with their order-list
# icons (Icons/Icon_<Name>.png, from art/tools/render_icons.sh).
EQUIPMENT_ART="$MOD_DIR/Vehicles/EquipmentArt"
mkdir -p "$EQUIPMENT_ART/Icons"
# Each FBX goes in as <Name>_Model.fbx: the game finds a mod item's prefab by file name in any bundle, so no
# asset may share a machine item's name (griddle, corngrill, dumplingsteamer). Copies under the old name go.
for dir in "$REPO"/art/equipment/*/; do
  name="$(basename "$dir")"
  [ -f "$dir$name.fbx" ] || continue  # e.g. art/equipment/renders
  rm -f "$EQUIPMENT_ART/$name.fbx" "$EQUIPMENT_ART/$name.fbx.meta" "$EQUIPMENT_ART/T_${name}_BaseColor.png"*
  rsync -a --checksum "$dir$name.fbx" "$EQUIPMENT_ART/${name}_Model.fbx"
  rsync -a --checksum "$dir"T_${name}_{BC,M,N}.png "$EQUIPMENT_ART/"
  rsync -a --checksum "${dir}Icon_$name.png" "$EQUIPMENT_ART/Icons/"
done
# Fixed props on the vehicles (the cash register), one folder each in art/props, the same way.
PROP_ART="$MOD_DIR/Vehicles/PropArt"
mkdir -p "$PROP_ART"
for dir in "$REPO"/art/props/*/; do
  name="$(basename "$dir")"
  rsync -a --checksum "$dir$name.fbx" "$PROP_ART/${name}_Model.fbx"
  rsync -a --checksum "$dir"T_${name}_{BC,M,N}.png "$PROP_ART/"
done

# The commissary bay's paint, lettering, sign and props (art/commissary), the same way.
BAY_ART="$MOD_DIR/Commissary/BayArt"
mkdir -p "$BAY_ART"
for fbx in "$REPO"/art/commissary/*.fbx; do
  name="$(basename "$fbx" .fbx)"
  rsync -a --checksum "$fbx" "$BAY_ART/${name}_Model.fbx"
done
# The road paint's own texture, and the look step's three maps for the sign (and its per-commissary plates) and the
# props; the flat T_*_BaseColor.png sources that game_look.py reads stay in art/.
rsync -a --checksum "$REPO"/art/commissary/T_BayPaint_BaseColor.png "$REPO"/art/commissary/T_*_BC.png \
  "$REPO"/art/commissary/T_*_M.png "$REPO"/art/commissary/T_*_N.png "$BAY_ART/"

# Link the mod folder and the build helpers into the SDK project.
link() { # link <target> <link path>
  if [ -L "$2" ]; then [ "$(readlink "$2")" = "$1" ] || { rm "$2"; ln -s "$1" "$2"; }
  elif [ -e "$2" ]; then echo "$2 exists and is not a symlink; refusing to replace it" >&2; exit 1
  else ln -s "$1" "$2"; fi
}
link "$MOD_DIR" "$SDK/Assets/Mods/FoodTrucks"
link "$REPO/unity/BuildEditor" "$SDK/Assets/Editor/FoodTrucksBuild"

# BatchBuild switches the SDK's HDRP assets to both lit shader modes for the build (the vehicle shader's Forward
# pass, which the game's hover outline needs) and back; Unity still rewrites SDK_RESAVED with its own upgrades, so
# they are put back after every Unity run, or the SDK_INPUTS check above would refuse the next build.

run_unity() { # run_unity <method> <log name>
  local log="$LOG_DIR/$2.log" ok=1
  "$UNITY" -batchmode -nographics -projectPath "$SDK" -buildTarget Win64 \
      -executeMethod "$1" -modId FoodTrucks -logFile "$log" || ok=0
  git -C "$SDK" checkout -q -- "${SDK_RESAVED[@]}"
  if [ "$ok" = 0 ]; then
    grep -E "FoodTrucksBuild|error CS|Exception" "$log" | tail -40 >&2
    echo "Unity step $1 failed; full log: $log" >&2
    exit 1
  fi
  grep "FoodTrucksBuild" "$log" || true
}

if [ "${1:-}" = "--generate" ]; then
  run_unity FoodTrucks.BuildEditor.ModAssets.Generate generate
fi
run_unity FoodTrucks.BuildEditor.BatchBuild.Build build

OUT="$REPO/build/FoodTrucks"
rm -rf "$OUT"
cp -R "$SDK/Output/FoodTrucks" "$OUT"
cp "$REPO/build/Harmony-LICENSE.txt" "$OUT/Dependencies/Harmony-LICENSE.txt"
cp "$REPO/README.md" "$REPO/LICENSE.txt" "$OUT/"
# The demand and economy tables, read at startup from the mod folder so players can tune them.
mkdir -p "$OUT/data"
cp "$REPO"/data/*.json "$OUT/data/"
# The game loads a prefab by file name from any bundle and may take another GameObject asset with the same name
# instead (a vehicle's model rather than its prefab), so no model or other prefab may share a prefab's name.
python3 "$REPO/tools/validate-bundles.py" "$OUT"
( cd "$OUT" && find . -type f | sort | xargs shasum -a 256 ) > "$REPO/build/FoodTrucks.sha256"
echo "Built $OUT"
cat "$REPO/build/FoodTrucks.sha256"
