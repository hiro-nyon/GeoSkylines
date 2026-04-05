#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
MANAGED_DIR_DEFAULT="$HOME/Library/Application Support/Steam/steamapps/common/Cities_Skylines/Cities.app/Contents/Resources/Data/Managed"
MODS_DIR_DEFAULT="$HOME/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods"
MANAGED_DIR="${CITIES_SKYLINES_MANAGED_DIR:-$MANAGED_DIR_DEFAULT}"
MODS_DIR="${CITIES_SKYLINES_MODS_DIR:-$MODS_DIR_DEFAULT}"
CONFIGURATION="${1:-Debug}"
SKIP_CLI="${SKIP_CLI:-0}"

if command -v msbuild >/dev/null 2>&1; then
  BUILD_TOOL="msbuild"
elif command -v xbuild >/dev/null 2>&1; then
  BUILD_TOOL="xbuild"
else
  echo "Neither msbuild nor xbuild was found. Install mono first: brew install mono" >&2
  exit 1
fi

if command -v dotnet >/dev/null 2>&1; then
  DOTNET_BIN="dotnet"
elif [[ -x "/usr/local/share/dotnet/dotnet" ]]; then
  DOTNET_BIN="/usr/local/share/dotnet/dotnet"
elif [[ -x "/opt/homebrew/share/dotnet/dotnet" ]]; then
  DOTNET_BIN="/opt/homebrew/share/dotnet/dotnet"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
  DOTNET_BIN="$HOME/.dotnet/dotnet"
else
  DOTNET_BIN=""
fi

echo "Building mod with Managed dir: $MANAGED_DIR"
"$BUILD_TOOL" "$ROOT_DIR/GeoSkylines/GeoSkylines.csproj" \
  /p:Configuration="$CONFIGURATION" \
  /p:CitiesSkylinesManagedDir="$MANAGED_DIR" \
  /p:CitiesSkylinesModsDir="$MODS_DIR"

mkdir -p "$MODS_DIR/GeoSkylines"
cp "$ROOT_DIR/GeoSkylines/bin/$CONFIGURATION/GeoSkylines.dll" "$MODS_DIR/GeoSkylines/GeoSkylines.dll"
if [[ -f "$ROOT_DIR/GeoSkylines/bin/$CONFIGURATION/GeoSkylines.pdb" ]]; then
  cp "$ROOT_DIR/GeoSkylines/bin/$CONFIGURATION/GeoSkylines.pdb" "$MODS_DIR/GeoSkylines/GeoSkylines.pdb"
fi

if [[ "$SKIP_CLI" == "1" ]]; then
  echo "Skipping export CLI build because SKIP_CLI=1"
elif [[ -z "$DOTNET_BIN" ]]; then
  echo "dotnet not found. Install the SDK to build GeoSkylines.ExportCli." >&2
  exit 2
else
  echo "Building export CLI"
  "$DOTNET_BIN" build "$ROOT_DIR/GeoSkylines.ExportCli/GeoSkylines.ExportCli.csproj" -c "$CONFIGURATION"
fi

echo "Done"
