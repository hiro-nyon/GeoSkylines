using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using UnityEngine;

namespace GeoSkylines
{
    public class GeoSkylinesExport
    {
        private sealed class TransportLineSnapshot
        {
            public ushort LineId;
            public string Name;
            public string TransportType;
            public List<ushort> Stops = new List<ushort>();
        }

        private readonly WGS84_UTM convertor = new WGS84_UTM(null);
        private readonly ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
        private readonly NetManager netManager = NetManager.instance;
        private readonly BuildingManager buildingManager = BuildingManager.instance;
        private readonly TerrainManager terrainManager = TerrainManager.instance;
        private readonly GeoSkylinesConfig config;
        private readonly Dictionary<ushort, TransportLineSnapshot> transportLines = new Dictionary<ushort, TransportLineSnapshot>();
        private readonly List<string> transportWarnings = new List<string>();

        private UTMResult centerUTM;
        private string zoneLetter = "N";
        private string mapName;
        private double centerLat;
        private double centerLon;
        private float exportXMin = -8640f;
        private float exportXMax = 8640f;
        private float exportYMin = -8640f;
        private float exportYMax = 8640f;
        private bool confloaded;
        private bool includeExtendedAttributes;
        private bool transportCacheLoaded;

        public GeoSkylinesExport()
        {
            config = GeoSkylinesConfig.Load();
            LoadConfiguration();

            if (confloaded)
            {
                centerUTM = convertor.convertLatLngToUtm(centerLat, centerLon);
                zoneLetter = centerLat >= 0 ? "N" : "S";
            }
        }

        public void LoadConfiguration()
        {
            string configPath = config.ConfigPath;
            if (!File.Exists(configPath))
            {
                panel.SetMessage("GeoSkylines", "No configuration file provided!", false);
                confloaded = false;
                return;
            }

            mapName = config.GetValue("MapName", "GeoSkylines");
            centerLat = config.GetDouble("CenterLatitude", 0d);
            centerLon = config.GetDouble("CenterLongitude", 0d);
            includeExtendedAttributes = config.GetBool(GeoSkylinesConfig.ExportIncludeExtendedAttributesKey, false);

            string exportBox = config.GetValue("ExportCoordsBox", string.Empty);
            if (!string.IsNullOrEmpty(exportBox))
            {
                string[] coords = exportBox.Split(',').Select(delegate(string part) { return part.Trim(); }).ToArray();
                if (coords.Length == 4)
                {
                    exportXMin = ParseFloatOrDefault(coords[0], exportXMin);
                    exportYMin = ParseFloatOrDefault(coords[1], exportYMin);
                    exportXMax = ParseFloatOrDefault(coords[2], exportXMax);
                    exportYMax = ParseFloatOrDefault(coords[3], exportYMax);
                }
            }

            confloaded = true;
        }

        public void ExportSegments()
        {
            ExecuteExport(new string[] { "roads", "rails" });
        }

        public void ExportBuildings()
        {
            ExecuteExport(new string[] { "buildings" });
        }

        public void ExportZones()
        {
            ExecuteExport(new string[] { "zones" });
        }

        public void ExportTrees()
        {
            ExecuteExport(new string[] { "trees" });
        }

        public void BatchExportConfiguredLayers()
        {
            ExecuteExport(config.GetExportLayers());
        }

        public string OutputConfiguration()
        {
            string outputDir = config.GetOutputDirectory();
            string formats = string.Join(", ", config.GetExportFormats());
            string layers = string.Join(", ", config.GetExportLayers());

            string confTxt = string.Empty;
            confTxt += "MapName: " + mapName + " (" + centerLon.ToString(CultureInfo.InvariantCulture) + ", " + centerLat.ToString(CultureInfo.InvariantCulture) + ")\n";
            confTxt += "exportXMin: " + exportXMin.ToString(CultureInfo.InvariantCulture) + "\n";
            confTxt += "exportXMax: " + exportXMax.ToString(CultureInfo.InvariantCulture) + "\n";
            confTxt += "exportYMin: " + exportYMin.ToString(CultureInfo.InvariantCulture) + "\n";
            confTxt += "exportYMax: " + exportYMax.ToString(CultureInfo.InvariantCulture) + "\n";
            confTxt += "outputDir: " + outputDir + "\n";
            confTxt += "formats: " + formats + "\n";
            confTxt += "layers: " + layers + "\n";
            confTxt += "runCli: " + config.GetBool(GeoSkylinesConfig.ExportRunCliKey, false).ToString() + "\n";
            return confTxt;
        }

        public static void DisplayLLOnMouseClick()
        {
            GeoSkylinesConfig config = GeoSkylinesConfig.Load();
            string configPath = config.ConfigPath;

            ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
            if (!File.Exists(configPath))
            {
                panel.SetMessage("GeoSkylines", "No configuration file provided!", false);
                return;
            }

            Vector3 screenMousePos = Input.mousePosition;
            Ray mouseRay = Camera.main.ScreenPointToRay(screenMousePos);
            Vector3 mousePos = GeoSkylinesTool.RaycastMouseLocation(mouseRay);

            double centerLat = config.GetDouble("CenterLatitude", 0d);
            double centerLon = config.GetDouble("CenterLongitude", 0d);
            WGS84_UTM convertor = new WGS84_UTM(null);
            UTMResult centerUTM = convertor.convertLatLngToUtm(centerLat, centerLon);
            string zoneLetter = centerLat >= 0 ? "N" : "S";

            double rwoX = mousePos.x + centerUTM.Easting;
            double rwoY = mousePos.z + centerUTM.Northing;
            LatLng rwoLL = convertor.convertUtmToLatLng(rwoX, rwoY, centerUTM.ZoneNumber, zoneLetter);

            string msg = string.Empty;
            msg += "Screen coordinates (x, y): " + screenMousePos + "\n";
            msg += "Game coordinates (x, z): " + mousePos + "\n";
            msg += "World coordinates (lon, lat): " + rwoLL.Lng.ToString(CultureInfo.InvariantCulture) + "," + rwoLL.Lat.ToString(CultureInfo.InvariantCulture);
            panel.SetMessage("Coordinates", msg, false);
        }

        public void OutputPrefabInfo()
        {
            string msg = string.Empty;

            msg += "TreeInfo:\nindex, Prefab name, Title, Prefab category\n";
            int prefabCnt = PrefabCollection<TreeInfo>.LoadedCount();
            for (int i = 0; i < prefabCnt; i++)
            {
                TreeInfo prefab = PrefabCollection<TreeInfo>.GetPrefab((uint)i);
                msg += string.Format("{0}, {1}, {2}, {3}\n", i, prefab.name, prefab.GetGeneratedTitle(), prefab.category);
            }

            msg += "\nNetInfo:\nindex, Prefab name, Title, Prefab category\n";
            prefabCnt = PrefabCollection<NetInfo>.LoadedCount();
            for (int i = 0; i < prefabCnt; i++)
            {
                NetInfo prefab = PrefabCollection<NetInfo>.GetPrefab((uint)i);
                msg += string.Format("{0}, {1}, {2}, {3}\n", i, prefab.name, prefab.GetGeneratedTitle(), prefab.category);
            }

            msg += "\nBuildingInfo:\nindex, Prefab name, Title, Prefab category\n";
            prefabCnt = PrefabCollection<BuildingInfo>.LoadedCount();
            for (int i = 0; i < prefabCnt; i++)
            {
                BuildingInfo prefab = PrefabCollection<BuildingInfo>.GetPrefab((uint)i);
                msg += string.Format("{0}, {1}, {2}, {3}\n", i, prefab.name, prefab.GetGeneratedTitle(), prefab.category);
            }

            UnityEngine.Debug.Log(msg);
            panel.SetMessage("GeoSkylines", "Prefab information written into output_log.txt.", false);
        }

        public void RemoveAllOfSomething(string something)
        {
            if (!string.Equals(something, "train", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            NetSegment[] segments = netManager.m_segments.m_buffer;
            for (int i = 0; i < segments.Length; i++)
            {
                NetSegment segment = segments[i];
                if (segment.m_startNode == 0 || segment.m_endNode == 0 || segment.Info == null)
                {
                    continue;
                }

                string infoTxt = segment.Info.ToString();
                if (infoTxt.Contains("Train Line") || infoTxt.Contains("Train Track"))
                {
                    netManager.ReleaseSegment((ushort)i, false);
                }
            }
        }

        private void ExecuteExport(IEnumerable<string> requestedLayers)
        {
            if (!confloaded)
            {
                return;
            }

            string[] layersToExport = requestedLayers
                .Select(delegate(string layer) { return (layer ?? string.Empty).Trim().ToLowerInvariant(); })
                .Where(delegate(string layer) { return !string.IsNullOrEmpty(layer); })
                .Distinct()
                .ToArray();

            string outputDirectory = config.GetOutputDirectory();
            Directory.CreateDirectory(outputDirectory);

            GeoSkylinesManifest manifest = new GeoSkylinesManifest();
            manifest.MapName = mapName;
            manifest.OutputDirectory = outputDirectory;
            manifest.ConfigPath = config.ConfigPath;
            manifest.Formats.AddRange(config.GetExportFormats());

            string[] configuredFormats = config.GetExportFormats();
            bool requiresGeoJsonForCli = configuredFormats.Any(delegate(string format) { return format == "shp" || format == "parquet"; });

            foreach (string layerName in layersToExport)
            {
                GeoSkylinesLayer layer = BuildLayer(layerName);
                manifest.Layers.Add(layer);

                string baseName = BuildBaseFileName(layer.Name);
                bool wroteGeoJson = false;
                foreach (string format in configuredFormats)
                {
                    if (format == "csv")
                    {
                        string csvPath = Path.Combine(outputDirectory, baseName + ".csv");
                        GeoSkylinesCsvWriter.Write(csvPath, layer);
                        layer.Files.Add(csvPath);
                        WriteLegacyCsvAliasIfNeeded(layer, csvPath);
                    }
                    else if (format == "geojson")
                    {
                        string geoJsonPath = Path.Combine(outputDirectory, baseName + ".geojson");
                        GeoSkylinesGeoJsonWriter.Write(geoJsonPath, layer);
                        layer.Files.Add(geoJsonPath);
                        wroteGeoJson = true;
                    }
                }

                if (requiresGeoJsonForCli && !wroteGeoJson)
                {
                    string geoJsonPath = Path.Combine(outputDirectory, baseName + ".geojson");
                    GeoSkylinesGeoJsonWriter.Write(geoJsonPath, layer);
                    layer.Files.Add(geoJsonPath);
                }
            }

            RunCliIfConfigured(manifest);
            string manifestPath = Path.Combine(outputDirectory, BuildBaseFileName("export_manifest") + ".json");
            GeoSkylinesManifestWriter.Write(manifestPath, manifest);
            int totalFeatures = manifest.Layers.Sum(delegate(GeoSkylinesLayer layer) { return layer.Features.Count; });
            panel.SetMessage("GeoSkylines", "Export completed. Layers: " + manifest.Layers.Count + ", features: " + totalFeatures + ". Output: " + outputDirectory, false);
        }

        private GeoSkylinesLayer BuildLayer(string layerName)
        {
            switch (layerName)
            {
                case "roads":
                    return BuildRoadLikeLayer("roads", false, false);
                case "rails":
                    return BuildRoadLikeLayer("rails", true, false);
                case "pedestrian_streets":
                    return BuildRoadLikeLayer("pedestrian_streets", false, true);
                case "buildings":
                    return BuildBuildingsLayer();
                case "zones":
                    return BuildZonesLayer();
                case "trees":
                    return BuildTreesLayer();
                case "transit_facilities":
                    return BuildFilteredBuildingsLayer("transit_facilities", IsTransitFacility);
                case "transit_hubs":
                    return BuildFilteredBuildingsLayer("transit_hubs", IsTransitHub);
                case "pedestrian_service_points":
                    return BuildFilteredBuildingsLayer("pedestrian_service_points", IsPedestrianServicePoint);
                case "outside_connections":
                    return BuildOutsideConnectionsLayer();
                case "transit_lines":
                    return BuildTransitLinesLayer();
                case "transit_stops":
                    return BuildTransitStopsLayer();
                case "pedestrian_areas":
                    return BuildPedestrianAreasLayer();
                default:
                    GeoSkylinesLayer emptyLayer = new GeoSkylinesLayer(layerName);
                    emptyLayer.Warnings.Add("Unknown layer: " + layerName);
                    return emptyLayer;
            }
        }

        private GeoSkylinesLayer BuildRoadLikeLayer(string layerName, bool railsOnly, bool pedestrianOnly)
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer(layerName);
            NetSegment[] segments = netManager.m_segments.m_buffer;
            for (int i = 0; i < segments.Length; i++)
            {
                NetSegment segment = segments[i];
                if (segment.m_startNode == 0 || segment.m_endNode == 0 || segment.Info == null)
                {
                    continue;
                }

                string infoText = SafeLower(segment.Info.ToString());
                if (infoText.Contains("water pipe"))
                {
                    continue;
                }

                bool isRail = infoText.Contains("train line") || infoText.Contains("train track");
                bool isPedestrian = infoText.Contains("pedestrian") || infoText.Contains("promenade");

                if (railsOnly != isRail)
                {
                    continue;
                }

                if (pedestrianOnly && !isPedestrian)
                {
                    continue;
                }

                if (!railsOnly && !pedestrianOnly && isRail)
                {
                    continue;
                }

                if (!WithinExportCoords(segment.m_middlePosition))
                {
                    continue;
                }

                GeoSkylinesGeometry geometry = BuildSegmentGeometry(segment);
                string featureId = i.ToString(CultureInfo.InvariantCulture);
                GeoSkylinesFeature feature = new GeoSkylinesFeature(layerName, featureId, geometry);
                feature.Attributes.Add("id", featureId);
                feature.Attributes.Add("name", GetSegmentName((ushort)i, segment));
                feature.Attributes.Add("prefab", SafeString(segment.Info.name));
                feature.Attributes.Add("service", segment.Info.m_class.m_service.ToString());
                feature.Attributes.Add("sub_service", segment.Info.m_class.m_subService.ToString());
                feature.Attributes.Add("elevation_start", GetElevationAtNode(segment.m_startNode).ToString("R", CultureInfo.InvariantCulture));
                feature.Attributes.Add("elevation_end", GetElevationAtNode(segment.m_endNode).ToString("R", CultureInfo.InvariantCulture));
                feature.Attributes.Add("transport_hint", ClassifyTransportMode(infoText));
                feature.Attributes.Add("pedestrian_hint", isPedestrian.ToString().ToLowerInvariant());

                if (includeExtendedAttributes)
                {
                    AddMemberAttributes(feature.Attributes, segment, "segment");
                }

                layer.Features.Add(feature);
            }

            return layer;
        }

        private GeoSkylinesLayer BuildBuildingsLayer()
        {
            return BuildFilteredBuildingsLayer("buildings", delegate(Building building) { return true; });
        }

        private GeoSkylinesLayer BuildFilteredBuildingsLayer(string layerName, Func<Building, bool> predicate)
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer(layerName);
            Building[] buildings = buildingManager.m_buildings.m_buffer;
            for (int i = 0; i < buildings.Length; i++)
            {
                Building building = buildings[i];
                if (building.m_position.y == 0 || building.Info == null)
                {
                    continue;
                }

                if (!WithinExportCoords(building.m_position))
                {
                    continue;
                }

                if (!predicate(building))
                {
                    continue;
                }

                GeoSkylinesGeometry geometry = BuildBuildingGeometry(building);
                string featureId = i.ToString(CultureInfo.InvariantCulture);
                GeoSkylinesFeature feature = new GeoSkylinesFeature(layerName, featureId, geometry);
                LatLng centroid = GamePosition2LatLng(building.m_position);
                feature.Attributes.Add("id", featureId);
                feature.Attributes.Add("name", GetBuildingName((ushort)i, building));
                feature.Attributes.Add("prefab", SafeString(building.Info.name));
                feature.Attributes.Add("service", building.Info.m_class.m_service.ToString());
                feature.Attributes.Add("sub_service", building.Info.m_class.m_subService.ToString());
                feature.Attributes.Add("class_level", building.Info.m_class.m_level.ToString());
                feature.Attributes.Add("width", building.Width.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("length", building.Length.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("angle", building.m_angle.ToString("R", CultureInfo.InvariantCulture));
                feature.Attributes.Add("centroid_lon", centroid.Lng.ToString("R", CultureInfo.InvariantCulture));
                feature.Attributes.Add("centroid_lat", centroid.Lat.ToString("R", CultureInfo.InvariantCulture));
                feature.Attributes.Add("transport_hint", ClassifyBuildingTransportHint(building));

                if (includeExtendedAttributes)
                {
                    AddMemberAttributes(feature.Attributes, building, "building");
                }

                layer.Features.Add(feature);
            }

            return layer;
        }

        private GeoSkylinesLayer BuildZonesLayer()
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer("zones");
            ZoneManager zoneManager = ZoneManager.instance;
            for (int i = 0; i < zoneManager.m_blocks.m_buffer.Length; i++)
            {
                ZoneBlock zoneBlock = zoneManager.m_blocks.m_buffer[i];
                Vector3 position = zoneBlock.m_position;
                if (position == Vector3.zero || !WithinExportCoords(position))
                {
                    continue;
                }

                ItemClass.Zone dominantZone = ItemClass.Zone.Unzoned;
                int dominantCount = 0;
                Dictionary<ItemClass.Zone, int> counts = new Dictionary<ItemClass.Zone, int>();
                for (int row = 0; row < zoneBlock.RowCount; row++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        ItemClass.Zone zone = zoneBlock.GetZone(col, row);
                        if (!counts.ContainsKey(zone))
                        {
                            counts.Add(zone, 0);
                        }

                        counts[zone] += 1;
                        if (counts[zone] > dominantCount)
                        {
                            dominantCount = counts[zone];
                            dominantZone = zone;
                        }
                    }
                }

                GeoSkylinesFeature feature = new GeoSkylinesFeature("zones", i.ToString(CultureInfo.InvariantCulture), BuildZoneGeometry(zoneBlock));
                feature.Attributes.Add("id", i.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("zone", dominantZone.ToString());
                feature.Attributes.Add("row_count", zoneBlock.RowCount.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("dominant_cells", dominantCount.ToString(CultureInfo.InvariantCulture));
                layer.Features.Add(feature);
            }

            return layer;
        }

        private GeoSkylinesLayer BuildTreesLayer()
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer("trees");
            TreeManager treeManager = TreeManager.instance;
            TreeInstance[] trees = treeManager.m_trees.m_buffer;
            int exportId = 0;
            for (int i = 0; i < trees.Length; i++)
            {
                TreeInstance tree = trees[i];
                if (tree.Position.y == 0 || !WithinExportCoords(tree.Position))
                {
                    continue;
                }

                exportId += 1;
                LatLng treePoint = GamePosition2LatLng(tree.Position);
                GeoSkylinesGeometry geometry = CreatePointGeometry(treePoint);
                GeoSkylinesFeature feature = new GeoSkylinesFeature("trees", exportId.ToString(CultureInfo.InvariantCulture), geometry);
                feature.Attributes.Add("id", exportId.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("tree_index", i.ToString(CultureInfo.InvariantCulture));

                if (includeExtendedAttributes)
                {
                    AddMemberAttributes(feature.Attributes, tree, "tree");
                }

                layer.Features.Add(feature);
            }

            return layer;
        }

        private GeoSkylinesLayer BuildOutsideConnectionsLayer()
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer("outside_connections");
            NetNode[] nodes = netManager.m_nodes.m_buffer;
            for (int i = 0; i < nodes.Length; i++)
            {
                NetNode node = nodes[i];
                if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
                {
                    continue;
                }

                if ((node.m_flags & NetNode.Flags.Outside) == NetNode.Flags.None)
                {
                    continue;
                }

                if (!WithinExportCoords(node.m_position))
                {
                    continue;
                }

                GeoSkylinesFeature feature = new GeoSkylinesFeature("outside_connections", i.ToString(CultureInfo.InvariantCulture), CreatePointGeometry(GamePosition2LatLng(node.m_position)));
                feature.Attributes.Add("id", i.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("flags", node.m_flags.ToString());
                layer.Features.Add(feature);
            }

            return layer;
        }

        private GeoSkylinesLayer BuildTransitLinesLayer()
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer("transit_lines");
            EnsureTransportCache();

            foreach (string warning in transportWarnings)
            {
                layer.Warnings.Add(warning);
            }

            foreach (TransportLineSnapshot snapshot in transportLines.Values.OrderBy(delegate(TransportLineSnapshot item) { return item.LineId; }))
            {
                List<GeoSkylinesCoordinate> coordinates = new List<GeoSkylinesCoordinate>();
                foreach (ushort stopId in snapshot.Stops)
                {
                    Vector3 stopPosition = netManager.m_nodes.m_buffer[stopId].m_position;
                    if (!WithinExportCoords(stopPosition))
                    {
                        continue;
                    }

                    LatLng point = GamePosition2LatLng(stopPosition);
                    coordinates.Add(new GeoSkylinesCoordinate(point.Lng, point.Lat));
                }

                if (coordinates.Count < 2)
                {
                    continue;
                }

                GeoSkylinesGeometry geometry = new GeoSkylinesGeometry("LineString");
                geometry.Parts.Add(coordinates);
                GeoSkylinesFeature feature = new GeoSkylinesFeature("transit_lines", snapshot.LineId.ToString(CultureInfo.InvariantCulture), geometry);
                feature.Attributes.Add("id", snapshot.LineId.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("name", snapshot.Name);
                feature.Attributes.Add("transport_type", snapshot.TransportType);
                feature.Attributes.Add("stop_count", snapshot.Stops.Count.ToString(CultureInfo.InvariantCulture));
                layer.Features.Add(feature);
            }

            if (layer.Features.Count == 0 && layer.Warnings.Count == 0)
            {
                layer.Warnings.Add("No transport lines were detected in the loaded save.");
            }

            return layer;
        }

        private GeoSkylinesLayer BuildTransitStopsLayer()
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer("transit_stops");
            EnsureTransportCache();

            foreach (string warning in transportWarnings)
            {
                layer.Warnings.Add(warning);
            }

            foreach (TransportLineSnapshot snapshot in transportLines.Values.OrderBy(delegate(TransportLineSnapshot item) { return item.LineId; }))
            {
                for (int index = 0; index < snapshot.Stops.Count; index++)
                {
                    ushort stopId = snapshot.Stops[index];
                    Vector3 stopPosition = netManager.m_nodes.m_buffer[stopId].m_position;
                    if (!WithinExportCoords(stopPosition))
                    {
                        continue;
                    }

                    GeoSkylinesFeature feature = new GeoSkylinesFeature(
                        "transit_stops",
                        snapshot.LineId.ToString(CultureInfo.InvariantCulture) + "_" + stopId.ToString(CultureInfo.InvariantCulture) + "_" + index.ToString(CultureInfo.InvariantCulture),
                        CreatePointGeometry(GamePosition2LatLng(stopPosition)));
                    feature.Attributes.Add("line_id", snapshot.LineId.ToString(CultureInfo.InvariantCulture));
                    feature.Attributes.Add("line_name", snapshot.Name);
                    feature.Attributes.Add("stop_id", stopId.ToString(CultureInfo.InvariantCulture));
                    feature.Attributes.Add("stop_index", index.ToString(CultureInfo.InvariantCulture));
                    feature.Attributes.Add("transport_type", snapshot.TransportType);
                    layer.Features.Add(feature);
                }
            }

            if (layer.Features.Count == 0 && layer.Warnings.Count == 0)
            {
                layer.Warnings.Add("No transit stops were detected in the loaded save.");
            }

            return layer;
        }

        private GeoSkylinesLayer BuildPedestrianAreasLayer()
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer("pedestrian_areas");
            layer.Warnings.Add("Pedestrian area polygons are not exposed reliably via the stable CS1 modding API. This layer is emitted empty in v1; use pedestrian_streets and pedestrian_service_points as the operational layers.");
            return layer;
        }

        private void EnsureTransportCache()
        {
            if (transportCacheLoaded)
            {
                return;
            }

            transportCacheLoaded = true;
            transportWarnings.Clear();

            try
            {
                Type assemblyType = typeof(NetManager).Assembly.GetType("TransportManager");
                if (assemblyType == null)
                {
                    transportWarnings.Add("TransportManager type was not found.");
                    return;
                }

                object manager = GetSingletonInstance(assemblyType);
                if (manager == null)
                {
                    transportWarnings.Add("TransportManager.instance was not available.");
                    return;
                }

                Array lineBuffer = GetManagerBuffer(manager, "m_lines");
                if (lineBuffer == null)
                {
                    transportWarnings.Add("TransportManager.m_lines.m_buffer was not available.");
                    return;
                }

                MethodInfo getLineNameMethod = assemblyType.GetMethod("GetLineName", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(ushort) }, null);
                for (ushort lineId = 0; lineId < lineBuffer.Length; lineId++)
                {
                    object line = lineBuffer.GetValue(lineId);
                    if (!HasCreatedFlag(line))
                    {
                        continue;
                    }

                    TransportLineSnapshot snapshot = new TransportLineSnapshot();
                    snapshot.LineId = lineId;
                    snapshot.Name = InvokeString(manager, getLineNameMethod, lineId) ?? ("Line " + lineId.ToString(CultureInfo.InvariantCulture));
                    snapshot.TransportType = GetTransportTypeFromLine(line);
                    snapshot.Stops = TryReadStops(line, lineId);
                    transportLines[lineId] = snapshot;
                }
            }
            catch (Exception ex)
            {
                transportWarnings.Add("Transport line export failed: " + ex.Message);
            }
        }

        private List<ushort> TryReadStops(object line, ushort lineId)
        {
            List<ushort> stops = new List<ushort>();
            Type lineType = line.GetType();

            MethodInfo countStopsMethod = lineType.GetMethod("CountStops", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getStopMethod = lineType.GetMethod("GetStop", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (countStopsMethod != null && getStopMethod != null)
            {
                int count = 0;
                object[] countArgs = countStopsMethod.GetParameters().Length == 1 ? new object[] { lineId } : new object[0];
                object countObj = SafeInvoke(line, countStopsMethod, countArgs);
                if (countObj != null)
                {
                    int.TryParse(countObj.ToString(), out count);
                }

                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        object[] stopArgs = getStopMethod.GetParameters().Length == 1 ? new object[] { i } : new object[] { lineId, i };
                        ushort stopId = ConvertToUshort(SafeInvoke(line, getStopMethod, stopArgs));
                        if (stopId > 0)
                        {
                            stops.Add(stopId);
                        }
                    }

                    if (stops.Count > 0)
                    {
                        return stops;
                    }
                }
            }

            ushort firstStop = ConvertToUshort(GetMemberValue(line, "m_stops"));
            if (firstStop == 0)
            {
                firstStop = ConvertToUshort(GetMemberValue(line, "m_stop"));
            }

            if (firstStop == 0)
            {
                return stops;
            }

            HashSet<ushort> visited = new HashSet<ushort>();
            ushort currentStop = firstStop;
            while (currentStop != 0 && !visited.Contains(currentStop))
            {
                visited.Add(currentStop);
                stops.Add(currentStop);
                object node = netManager.m_nodes.m_buffer[currentStop];
                ushort nextStop = ConvertToUshort(GetMemberValue(node, "m_nextBuildingNode"));
                if (nextStop == 0 || nextStop == firstStop)
                {
                    break;
                }

                currentStop = nextStop;
            }

            return stops;
        }

        private GeoSkylinesGeometry BuildSegmentGeometry(NetSegment segment)
        {
            ushort startNodeId = segment.m_startNode;
            ushort endNodeId = segment.m_endNode;
            Vector3 startDirection = segment.m_startDirection;
            Vector3 endDirection = segment.m_endDirection;

            if ((segment.m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None)
            {
                startNodeId = segment.m_endNode;
                endNodeId = segment.m_startNode;
                startDirection = segment.m_endDirection;
                endDirection = segment.m_startDirection;
            }

            Vector3 startPos = netManager.m_nodes.m_buffer[startNodeId].m_position;
            Vector3 endPos = netManager.m_nodes.m_buffer[endNodeId].m_position;
            List<GeoSkylinesCoordinate> points = new List<GeoSkylinesCoordinate>();
            points.Add(ToCoordinate(startPos));

            if (Vector3.Angle(startDirection, -endDirection) > 3f)
            {
                Vector3 controlA;
                Vector3 controlB;
                NetSegment.CalculateMiddlePoints(startPos, startDirection, endPos, endDirection, false, false, out controlA, out controlB);
                Bezier3 bezier = new Bezier3(startPos, controlA, controlB, endPos);
                points.Add(ToCoordinate(bezier.Position(0.25f)));
                points.Add(ToCoordinate(bezier.Position(0.5f)));
                points.Add(ToCoordinate(bezier.Position(0.75f)));
            }

            points.Add(ToCoordinate(endPos));

            GeoSkylinesGeometry geometry = new GeoSkylinesGeometry("LineString");
            geometry.Parts.Add(points);
            return geometry;
        }

        private GeoSkylinesGeometry BuildBuildingGeometry(Building building)
        {
            int width = building.Width;
            int length = building.Length;
            Vector3 axisA = new Vector3(Mathf.Cos(building.m_angle), 0f, Mathf.Sin(building.m_angle)) * 8f;
            Vector3 axisB = new Vector3(axisA.z, 0f, -axisA.x);
            Vector3 startCorner = building.m_position - (width * 0.5f * axisA) - (length * 0.5f * axisB);
            Vector3[] corners = new Vector3[]
            {
                startCorner,
                building.m_position + (width * 0.5f * axisA) - (length * 0.5f * axisB),
                building.m_position + (width * 0.5f * axisA) + (length * 0.5f * axisB),
                building.m_position - (width * 0.5f * axisA) + (length * 0.5f * axisB),
                startCorner
            };

            return CreatePolygonGeometry(corners);
        }

        private GeoSkylinesGeometry BuildZoneGeometry(ZoneBlock zoneBlock)
        {
            int width = 4;
            int length = zoneBlock.RowCount;
            Vector3 axisA = new Vector3(Mathf.Cos(zoneBlock.m_angle), 0f, Mathf.Sin(zoneBlock.m_angle)) * 8f;
            Vector3 axisB = new Vector3(axisA.z, 0f, -axisA.x);
            Vector3 startCorner = zoneBlock.m_position - (width * 0.5f * axisA) - (length * 0.5f * axisB);
            Vector3[] corners = new Vector3[]
            {
                startCorner,
                zoneBlock.m_position + (width * 0.5f * axisA) - (length * 0.5f * axisB),
                zoneBlock.m_position + (width * 0.5f * axisA) + (length * 0.5f * axisB),
                zoneBlock.m_position - (width * 0.5f * axisA) + (length * 0.5f * axisB),
                startCorner
            };

            return CreatePolygonGeometry(corners);
        }

        private GeoSkylinesGeometry CreatePolygonGeometry(IEnumerable<Vector3> corners)
        {
            GeoSkylinesGeometry geometry = new GeoSkylinesGeometry("Polygon");
            List<GeoSkylinesCoordinate> ring = new List<GeoSkylinesCoordinate>();
            foreach (Vector3 corner in corners)
            {
                ring.Add(ToCoordinate(corner));
            }

            geometry.Parts.Add(ring);
            return geometry;
        }

        private GeoSkylinesGeometry CreatePointGeometry(LatLng point)
        {
            GeoSkylinesGeometry geometry = new GeoSkylinesGeometry("Point");
            geometry.Parts.Add(new List<GeoSkylinesCoordinate> { new GeoSkylinesCoordinate(point.Lng, point.Lat) });
            return geometry;
        }

        private GeoSkylinesCoordinate ToCoordinate(Vector3 position)
        {
            LatLng point = GamePosition2LatLng(position);
            return new GeoSkylinesCoordinate(point.Lng, point.Lat);
        }

        private LatLng GamePosition2LatLng(Vector3 position)
        {
            double rwoX = position.x + centerUTM.Easting;
            double rwoY = position.z + centerUTM.Northing;
            return convertor.convertUtmToLatLng(rwoX, rwoY, centerUTM.ZoneNumber, zoneLetter);
        }

        private bool WithinExportCoords(Vector3 position)
        {
            return position.x > exportXMin && position.x < exportXMax && position.z > exportYMin && position.z < exportYMax;
        }

        private float ParseFloatOrDefault(string value, float defaultValue)
        {
            float parsed;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            if (float.TryParse(value, out parsed))
            {
                return parsed == 0f ? defaultValue : parsed;
            }

            return defaultValue;
        }

        private string BuildBaseFileName(string layerName)
        {
            string safeMapName = SanitizeFileName(string.IsNullOrEmpty(mapName) ? "GeoSkylines" : mapName);
            string safeLayerName = SanitizeFileName(layerName);
            return safeMapName + "_" + safeLayerName;
        }

        private void WriteLegacyCsvAliasIfNeeded(GeoSkylinesLayer layer, string csvPath)
        {
            string legacyName = null;
            switch (layer.Name)
            {
                case "roads":
                    legacyName = "roads_cs.csv";
                    break;
                case "rails":
                    legacyName = "rails_cs.csv";
                    break;
                case "buildings":
                    legacyName = "buildings_cs.csv";
                    break;
                case "zones":
                    legacyName = "zones_cs.csv";
                    break;
                case "trees":
                    legacyName = "trees_cs.csv";
                    break;
            }

            if (legacyName == null)
            {
                return;
            }

            string legacyPath = Path.Combine(GeoSkylinesConfig.GetFilesDirectory(), legacyName);
            File.Copy(csvPath, legacyPath, true);
        }

        private void RunCliIfConfigured(GeoSkylinesManifest manifest)
        {
            string[] formats = config.GetExportFormats();
            bool needsCli = formats.Contains("shp") || formats.Contains("parquet");
            if (!needsCli)
            {
                return;
            }

            string cliPath = config.GetValue(GeoSkylinesConfig.ExportCliPathKey, string.Empty);
            manifest.CliPath = cliPath;

            if (!config.GetBool(GeoSkylinesConfig.ExportRunCliKey, false))
            {
                manifest.Warnings.Add("CLI conversion was skipped because ExportRunCli=false.");
                return;
            }

            if (string.IsNullOrEmpty(cliPath) || !File.Exists(cliPath))
            {
                manifest.Warnings.Add("CLI conversion was requested but ExportCliPath is missing or invalid.");
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = cliPath;
            startInfo.Arguments = "--input-dir \"" + manifest.OutputDirectory + "\" --formats \"" + string.Join(",", formats.Where(delegate(string format) { return format == "shp" || format == "parquet"; }).ToArray()) + "\"";
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                manifest.CliExitCode = process.ExitCode.ToString(CultureInfo.InvariantCulture);
                manifest.CliOutput = (output + "\n" + error).Trim();
                if (process.ExitCode != 0)
                {
                    manifest.Warnings.Add("CLI conversion failed. See cli_output in the manifest.");
                }
            }
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = value;
            foreach (char ch in invalid)
            {
                safe = safe.Replace(ch, '_');
            }

            return safe.Replace(' ', '_');
        }

        private string GetSegmentName(ushort segmentId, NetSegment segment)
        {
            string roadName = netManager.GetSegmentName(segmentId);
            if (string.IsNullOrEmpty(roadName))
            {
                roadName = SafeString(segment.Info.name);
            }

            return roadName;
        }

        private string GetBuildingName(ushort buildingId, Building building)
        {
            try
            {
                MethodInfo getBuildingNameMethod = buildingManager.GetType().GetMethod("GetBuildingName", BindingFlags.Instance | BindingFlags.Public);
                if (getBuildingNameMethod != null)
                {
                    ParameterInfo[] parameters = getBuildingNameMethod.GetParameters();
                    object result = null;
                    if (parameters.Length == 2)
                    {
                        result = getBuildingNameMethod.Invoke(buildingManager, new object[] { buildingId, InstanceID.Empty });
                    }
                    else if (parameters.Length == 1)
                    {
                        result = getBuildingNameMethod.Invoke(buildingManager, new object[] { buildingId });
                    }

                    string name = SafeString(result);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }

            return SafeString(building.Info.name);
        }

        private float GetElevationAtNode(ushort nodeId)
        {
            Vector3 position = netManager.m_nodes.m_buffer[nodeId].m_position;
            float terrainHeight = terrainManager.SampleRawHeightSmoothWithWater(position, false, 0f);
            return position.y - terrainHeight;
        }

        private bool IsTransitFacility(Building building)
        {
            if (building.Info == null)
            {
                return false;
            }

            string text = SafeLower(BuildSearchText(building.Info));
            string service = building.Info.m_class.m_service.ToString().ToLowerInvariant();
            return service.Contains("publictransport")
                || text.Contains("metro")
                || text.Contains("train")
                || text.Contains("bus")
                || text.Contains("tram")
                || text.Contains("ferry")
                || text.Contains("harbor")
                || text.Contains("monorail")
                || text.Contains("cable car")
                || text.Contains("taxi")
                || text.Contains("airport");
        }

        private bool IsTransitHub(Building building)
        {
            if (!IsTransitFacility(building))
            {
                return false;
            }

            string text = SafeLower(BuildSearchText(building.Info));
            return text.Contains("hub") || text.Contains("exchange") || text.Contains("terminal");
        }

        private bool IsPedestrianServicePoint(Building building)
        {
            if (building.Info == null)
            {
                return false;
            }

            string text = SafeLower(BuildSearchText(building.Info));
            return text.Contains("service point");
        }

        private string ClassifyTransportMode(string infoText)
        {
            if (infoText.Contains("metro"))
            {
                return "metro";
            }
            if (infoText.Contains("tram"))
            {
                return "tram";
            }
            if (infoText.Contains("monorail"))
            {
                return "monorail";
            }
            if (infoText.Contains("ferry"))
            {
                return "ferry";
            }
            if (infoText.Contains("ship") || infoText.Contains("harbor"))
            {
                return "ship";
            }
            if (infoText.Contains("pedestrian") || infoText.Contains("promenade"))
            {
                return "pedestrian";
            }
            if (infoText.Contains("train"))
            {
                return "train";
            }
            return "road";
        }

        private string ClassifyBuildingTransportHint(Building building)
        {
            if (IsPedestrianServicePoint(building))
            {
                return "pedestrian_service_point";
            }
            if (IsTransitHub(building))
            {
                return "transit_hub";
            }
            if (IsTransitFacility(building))
            {
                return "transit_facility";
            }
            return "none";
        }

        private string BuildSearchText(object prefab)
        {
            if (prefab == null)
            {
                return string.Empty;
            }

            object generatedTitle = null;
            MethodInfo method = prefab.GetType().GetMethod("GetGeneratedTitle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                generatedTitle = SafeInvoke(prefab, method, new object[0]);
            }

            string text = SafeString(GetMemberValue(prefab, "name")) + " " + SafeString(generatedTitle) + " " + SafeString(GetMemberValue(prefab, "category"));
            return text;
        }

        private static string SafeLower(string value)
        {
            return (value ?? string.Empty).ToLowerInvariant();
        }

        private static string SafeString(object value)
        {
            return value == null ? string.Empty : value.ToString();
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(instance, null);
            }

            return null;
        }

        private static object GetSingletonInstance(Type type)
        {
            PropertyInfo property = type.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                return property.GetValue(null, null);
            }

            FieldInfo field = type.GetField("instance", BindingFlags.Public | BindingFlags.Static);
            if (field != null)
            {
                return field.GetValue(null);
            }

            return null;
        }

        private static Array GetManagerBuffer(object manager, string arrayFieldName)
        {
            object bufferWrapper = GetMemberValue(manager, arrayFieldName);
            if (bufferWrapper == null)
            {
                return null;
            }

            object rawBuffer = GetMemberValue(bufferWrapper, "m_buffer");
            return rawBuffer as Array;
        }

        private static bool HasCreatedFlag(object instance)
        {
            object flags = GetMemberValue(instance, "m_flags");
            return flags != null && flags.ToString().Contains("Created");
        }

        private static ushort ConvertToUshort(object value)
        {
            if (value == null)
            {
                return 0;
            }

            ushort converted;
            if (ushort.TryParse(value.ToString(), out converted))
            {
                return converted;
            }

            try
            {
                return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static object SafeInvoke(object instance, MethodInfo method, object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch
            {
                return null;
            }
        }

        private static string InvokeString(object instance, MethodInfo method, ushort lineId)
        {
            if (method == null)
            {
                return null;
            }

            object result = SafeInvoke(instance, method, new object[] { lineId });
            return result == null ? null : result.ToString();
        }

        private string GetTransportTypeFromLine(object line)
        {
            object transportType = GetMemberValue(line, "m_transportType");
            if (transportType != null)
            {
                return transportType.ToString();
            }

            object info = GetMemberValue(line, "Info") ?? GetMemberValue(line, "m_info");
            object innerTransportType = GetMemberValue(info, "m_transportType");
            if (innerTransportType != null)
            {
                return innerTransportType.ToString();
            }

            return "unknown";
        }

        private void AddMemberAttributes(IDictionary<string, string> attributes, object instance, string prefix)
        {
            Type type = instance.GetType();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object value = field.GetValue(instance);
                if (value == null)
                {
                    continue;
                }

                string key = prefix + "_" + field.Name.ToLowerInvariant();
                if (!attributes.ContainsKey(key))
                {
                    attributes.Add(key, SafeString(value));
                }
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object value;
                try
                {
                    value = property.GetValue(instance, null);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                string key = prefix + "_" + property.Name.ToLowerInvariant();
                if (!attributes.ContainsKey(key))
                {
                    attributes.Add(key, SafeString(value));
                }
            }
        }
    }
}
