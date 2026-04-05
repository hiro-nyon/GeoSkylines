# GeoSkylines

GIS import/export tooling for `Cities: Skylines 1`.

Current release target: `1.0.0-beta.1`

## What It Does
- Direct export from the mod to `CSV` and `GeoJSON`
- Optional conversion to `SHP` and `GeoParquet` through `GeoSkylines.ExportCli`
- Batch export with `Right Ctrl + E`
- Legacy import workflows remain available for roads, rails, trees, zones, water, and services

## Export Layers
- `roads`
- `rails`
- `buildings`
- `zones`
- `trees`
- `transit_lines`
- `transit_stops`
- `transit_facilities`
- `pedestrian_areas`
- `pedestrian_streets`
- `pedestrian_service_points`
- `transit_hubs`
- `outside_connections`

## Output Formats
- `csv`
- `geojson`
- `shp` via CLI
- `parquet` via CLI

Every batch export writes:
- `<MapName>_<layer>.<ext>`
- `export_manifest.json`

## Quick Start
1. Copy `GeoSkylines.dll` into your `Cities_Skylines/Addons/Mods/GeoSkylines` folder.
2. Create `Files/import_export.conf` in the game working directory.
3. Set `ExportFormats`, `ExportLayers`, and optionally `ExportCliPath`.
4. In game, enable the mod and run `Right Ctrl + E`.

An example config is available at [examples/import_export.conf.example](examples/import_export.conf.example).

## Hotkeys
- `Right Ctrl + E`: batch export using configured layers and formats
- `Right Ctrl + G`: export road and rail network layers
- `Right Ctrl + H`: export buildings
- `Right Ctrl + J`: export zones
- `Right Ctrl + K`: export trees
- `Right Ctrl + P`: dump prefab metadata to the game log
- `Right Ctrl + Left Click`: inspect lat/lon under the cursor

Legacy import hotkeys remain in place for roads, rails, water, trees, zones, and services.

## Config Keys
Canonical config file: `import_export.conf`

Legacy support:
- `import_export.txt` is still read if `import_export.conf` is absent.
- Saving settings from the in-game UI migrates to `import_export.conf`.

Export-specific keys:
- `ExportOutputDirectory`
- `ExportFormats`
- `ExportLayers`
- `ExportCliPath`
- `ExportRunCli`
- `ExportIncludeExtendedAttributes`

Core geographic keys still required for coordinate conversion:
- `MapName`
- `CenterLatitude`
- `CenterLongitude`

## macOS Development
Requirements:
- `mono` for the legacy CS1 mod project
- `dotnet` SDK for the CLI
- a local Cities: Skylines 1 install

Default macOS paths:
- Managed DLLs: `~/Library/Application Support/Steam/steamapps/common/Cities_Skylines/Cities.app/Contents/Resources/Data/Managed`
- Mods folder: `~/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods`

Build:

```bash
./scripts/build-mac.sh Debug
```

Override locations:

```bash
CITIES_SKYLINES_MANAGED_DIR=/path/to/Managed \
CITIES_SKYLINES_MODS_DIR=/path/to/Mods \
./scripts/build-mac.sh Release
```

Build only the mod:

```bash
SKIP_CLI=1 ./scripts/build-mac.sh Debug
```

## Windows Development
Build both projects:

```powershell
.\scripts\build-windows.ps1 -Configuration Release
```

## Release Packaging
Create a release zip on macOS:

```bash
./scripts/package-release.sh Release
```

This writes `release/GeoSkylines-<version>.zip` with:
- mod DLLs
- CLI binaries
- `README.md`
- `LICENSE`
- `NOTICE`
- `CHANGELOG.md`
- example config and sample exports

## Samples
- [examples/sample_roads.geojson](examples/sample_roads.geojson)
- [examples/sample_roads.fieldmap.json](examples/sample_roads.fieldmap.json)
- [examples/sample_export_manifest.json](examples/sample_export_manifest.json)

## Known Limitations
- `pedestrian_areas` is currently a placeholder layer and may export zero features with a warning.
- `transit_lines` and `transit_stops` use reflection to tolerate CS1 API differences, so edge cases should be tested against real saves.
- `SHP` and `GeoParquet` require `GeoSkylines.ExportCli`; the mod only writes `CSV` and `GeoJSON` directly.
- Phase 2 layers such as airport areas, utility networks, park areas, campus areas, and industry areas are not implemented yet.

## Attribution
This repository modernizes the original GeoSkylines idea and codebase. See [NOTICE](NOTICE) for release-time attribution notes.

Useful upstream references:
- Original GeoSkylines: [gonzikcz/GeoSkylines](https://github.com/gonzikcz/GeoSkylines)
- burningmime curves: [burningmime/curves](https://github.com/burningmime/curves)

## Release Checklist
- Build the mod and CLI in `Release`
- Run a real in-game batch export
- Validate `GeoJSON`, `SHP`, and `GeoParquet` in QGIS
- Confirm the packaged zip contains the expected binaries and docs
