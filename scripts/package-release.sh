#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(tr -d '\r\n' < "$ROOT_DIR/VERSION")"
CONFIGURATION="${1:-Release}"
SKIP_BUILD="${SKIP_BUILD:-0}"
RELEASE_DIR="$ROOT_DIR/release/GeoSkylines-$VERSION"

if [[ "$SKIP_BUILD" != "1" ]]; then
  "$ROOT_DIR/scripts/build-mac.sh" "$CONFIGURATION"
fi

rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR/mod/GeoSkylines" "$RELEASE_DIR/cli" "$RELEASE_DIR/examples"

cp "$ROOT_DIR/GeoSkylines/bin/$CONFIGURATION/GeoSkylines.dll" "$RELEASE_DIR/mod/GeoSkylines/GeoSkylines.dll"
if [[ -f "$ROOT_DIR/GeoSkylines/bin/$CONFIGURATION/GeoSkylines.pdb" ]]; then
  cp "$ROOT_DIR/GeoSkylines/bin/$CONFIGURATION/GeoSkylines.pdb" "$RELEASE_DIR/mod/GeoSkylines/GeoSkylines.pdb"
fi

cp "$ROOT_DIR/GeoSkylines.ExportCli/bin/$CONFIGURATION/net8.0/GeoSkylines.ExportCli"* "$RELEASE_DIR/cli/"
cp "$ROOT_DIR/README.md" "$ROOT_DIR/LICENSE" "$ROOT_DIR/NOTICE" "$ROOT_DIR/CHANGELOG.md" "$ROOT_DIR/VERSION" "$RELEASE_DIR/"
cp "$ROOT_DIR/examples/import_export.conf.example" "$ROOT_DIR/examples/sample_roads.geojson" "$ROOT_DIR/examples/sample_roads.fieldmap.json" "$ROOT_DIR/examples/sample_export_manifest.json" "$RELEASE_DIR/examples/"

(cd "$ROOT_DIR/release" && zip -qry "GeoSkylines-$VERSION.zip" "GeoSkylines-$VERSION")
echo "Created $ROOT_DIR/release/GeoSkylines-$VERSION.zip"
