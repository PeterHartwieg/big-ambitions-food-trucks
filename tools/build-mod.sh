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
# The SDK compiles against the installed game's DLLs; copy only changed files to avoid reimports.
rsync -a --checksum "$GAME_DLLS/" "$SDK/Assets/_BaDependencies/GameDlls/" --include='*.dll' --exclude='*'
python3 "$REPO/tools/write-dll-metas.py" "$SDK/Assets/_BaDependencies/GameDlls"


# Harmony, checksum-pinned. Never committed.
if [ ! -f "$DEPS/0Harmony.dll" ]; then
  tmp="$(mktemp -d)"
  curl -fsSL "$HARMONY_URL" -o "$tmp/harmony.nupkg"
  echo "$HARMONY_SHA256  $tmp/harmony.nupkg" | shasum -a 256 -c - >/dev/null
  unzip -q -o "$tmp/harmony.nupkg" 'lib/net472/0Harmony.dll' LICENSE -d "$tmp"
  cp "$tmp/lib/net472/0Harmony.dll" "$DEPS/0Harmony.dll"
  cp "$tmp/LICENSE" "$REPO/build/Harmony-LICENSE.txt"
  rm -rf "$tmp"
fi

# Core, compiled for netstandard2.1 and shipped next to the mod DLL.
dotnet build "$REPO/Core/FoodTrucks.Core.csproj" -c Release -nologo -v quiet
cp "$REPO/Core/bin/Release/netstandard2.1/FoodTrucks.Core.dll" "$DEPS/FoodTrucks.Core.dll"

# Link the mod folder and the build helpers into the SDK project.
link() { # link <target> <link path>
  if [ -L "$2" ]; then [ "$(readlink "$2")" = "$1" ] || { rm "$2"; ln -s "$1" "$2"; }
  elif [ -e "$2" ]; then echo "$2 exists and is not a symlink; refusing to replace it" >&2; exit 1
  else ln -s "$1" "$2"; fi
}
link "$MOD_DIR" "$SDK/Assets/Mods/FoodTrucks"
link "$REPO/unity/BuildEditor" "$SDK/Assets/Editor/FoodTrucksBuild"

run_unity() { # run_unity <method> <log name>
  local log="$LOG_DIR/$2.log"
  if ! "$UNITY" -batchmode -nographics -projectPath "$SDK" -buildTarget Win64 \
      -executeMethod "$1" -modId FoodTrucks -logFile "$log"; then
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
cp "$REPO/build/Harmony-LICENSE.txt" "$OUT/Dependencies/Harmony-LICENSE.txt" 2>/dev/null || true
cp "$REPO/README.md" "$REPO/LICENSE.txt" "$OUT/"
( cd "$OUT" && find . -type f | sort | xargs shasum -a 256 ) > "$REPO/build/FoodTrucks.sha256"
echo "Built $OUT"
cat "$REPO/build/FoodTrucks.sha256"
