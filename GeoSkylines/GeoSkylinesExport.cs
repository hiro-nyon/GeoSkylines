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
            public string RuntimeType;
            public List<ushort> Stops = new List<ushort>();
        }

        private sealed class GeoSkylinesExportLayerDefinition
        {
            public string Name;
            public string SourceSystem;
            public bool Experimental;
            public string LegacyCsvAlias;
            public Func<GeoSkylinesLayer> Builder;
        }

        private sealed class NetworkClassification
        {
            public string LayerName;
            public string AIType;
            public string TransportType;
            public string ClassificationStatus;
            public string LaneTypes;
            public string VehicleTypes;
        }

        private sealed class BuildingClassification
        {
            public string AIType;
            public string PrimaryLayer;
            public string ClassificationStatus;
            public bool IsTransitFacility;
            public bool IsTransitHub;
            public bool IsWaterFacility;
            public bool IsFishingFacility;
            public bool IsPedestrianServicePoint;
            public bool IsAirportBuilding;
            public bool IsCampusBuilding;
            public bool IsIndustryBuilding;
            public bool IsOutsideConnectionBuilding;
        }

        private struct GridPoint
        {
            public int X;
            public int Z;

            public GridPoint(int x, int z)
            {
                X = x;
                Z = z;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Z;
                }
            }

            public override bool Equals(object obj)
            {
                if (!(obj is GridPoint))
                {
                    return false;
                }

                GridPoint other = (GridPoint)obj;
                return X == other.X && Z == other.Z;
            }
        }

        private struct GridEdgeKey
        {
            public GridPoint A;
            public GridPoint B;

            public GridEdgeKey(GridPoint a, GridPoint b)
            {
                if (a.X < b.X || (a.X == b.X && a.Z <= b.Z))
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A.GetHashCode() * 397) ^ B.GetHashCode();
                }
            }

            public override bool Equals(object obj)
            {
                if (!(obj is GridEdgeKey))
                {
                    return false;
                }

                GridEdgeKey other = (GridEdgeKey)obj;
                return A.Equals(other.A) && B.Equals(other.B);
            }
        }

        private struct DirectedGridEdge
        {
            public GridPoint Start;
            public GridPoint End;

            public DirectedGridEdge(GridPoint start, GridPoint end)
            {
                Start = start;
                End = end;
            }
        }

        private sealed class AreaAccumulator
        {
            public string LayerName;
            public int AreaId;
            public string Name;
            public string AreaType;
            public HashSet<long> Cells = new HashSet<long>();
        }

        private readonly WGS84_UTM convertor = new WGS84_UTM(null);
        private readonly ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
        private readonly NetManager netManager = NetManager.instance;
        private readonly BuildingManager buildingManager = BuildingManager.instance;
        private readonly TerrainManager terrainManager = TerrainManager.instance;
        private readonly DistrictManager districtManager = DistrictManager.instance;
        private readonly ZoneManager zoneManager = ZoneManager.instance;
        private readonly TreeManager treeManager = TreeManager.instance;
        private readonly PropManager propManager = PropManager.instance;
        private readonly GeoSkylinesConfig config;
        private readonly Dictionary<string, GeoSkylinesExportLayerDefinition> layerDefinitions = new Dictionary<string, GeoSkylinesExportLayerDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ushort, TransportLineSnapshot> transportLines = new Dictionary<ushort, TransportLineSnapshot>();
        private readonly List<string> transportWarnings = new List<string>();
        private readonly Dictionary<string, GeoSkylinesLayer> networkLayerCache = new Dictionary<string, GeoSkylinesLayer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GeoSkylinesLayer> buildingLayerCache = new Dictionary<string, GeoSkylinesLayer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GeoSkylinesLayer> areaLayerCache = new Dictionary<string, GeoSkylinesLayer>(StringComparer.OrdinalIgnoreCase);

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
        private bool networkCacheLoaded;
        private bool buildingCacheLoaded;
        private bool areaCacheLoaded;
        private GeoSkylinesLayer treesLayerCache;
        private GeoSkylinesLayer zonesLayerCache;
        private GeoSkylinesLayer propsLayerCache;
        private GeoSkylinesLayer waterSourcesLayerCache;

        public GeoSkylinesExport()
        {
            config = GeoSkylinesConfig.Load();
            LoadConfiguration();
            RegisterLayerDefinitions();

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
                panel.SetMessage("GeoSkylines", "No configuration file found.\nExpected path: " + configPath, false);
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
            ExecuteExport(new string[] { "roads", "rails", "metro_tracks", "tram_tracks", "monorail_tracks", "trolleybus_roads", "ship_paths", "fishing_paths", "canals", "water_pipes", "pedestrian_streets", "pedestrian_paths", "transport_guideways", "outside_connection_segments", "network_unknown" });
        }

        public void ExportBuildings()
        {
            ExecuteExport(new string[] { "buildings", "transit_facilities", "transit_hubs", "water_facilities", "fishing_facilities", "pedestrian_service_points", "airport_buildings", "campus_buildings", "industry_buildings", "outside_connection_buildings" });
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
                panel.SetMessage("GeoSkylines", "No configuration file found.\nExpected path: " + configPath, false);
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

                string aiType = GetNetAIType(segment.Info);
                if (aiType == "TrainTrackAI")
                {
                    netManager.ReleaseSegment((ushort)i, false);
                }
            }
        }

        private void RegisterLayerDefinitions()
        {
            RegisterLayer("roads", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("roads"); }, "roads_cs.csv");
            RegisterLayer("rails", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("rails"); }, "rails_cs.csv");
            RegisterLayer("metro_tracks", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("metro_tracks"); }, null);
            RegisterLayer("tram_tracks", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("tram_tracks"); }, null);
            RegisterLayer("monorail_tracks", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("monorail_tracks"); }, null);
            RegisterLayer("trolleybus_roads", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("trolleybus_roads"); }, null);
            RegisterLayer("pedestrian_streets", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("pedestrian_streets"); }, null);
            RegisterLayer("pedestrian_paths", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("pedestrian_paths"); }, null);
            RegisterLayer("ship_paths", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("ship_paths"); }, null);
            RegisterLayer("fishing_paths", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("fishing_paths"); }, null);
            RegisterLayer("canals", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("canals"); }, null);
            RegisterLayer("water_pipes", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("water_pipes"); }, null);
            RegisterLayer("transport_guideways", "net_manager", true, delegate { return BuildNetworkSegmentsLayer("transport_guideways"); }, null);
            RegisterLayer("outside_connection_nodes", "net_manager", false, delegate { return BuildNetworkNodesLayer("outside_connection_nodes"); }, null);
            RegisterLayer("outside_connection_segments", "net_manager", false, delegate { return BuildNetworkSegmentsLayer("outside_connection_segments"); }, null);
            RegisterLayer("network_nodes", "net_manager", false, delegate { return BuildNetworkNodesLayer("network_nodes"); }, null);
            RegisterLayer("network_unknown", "net_manager", true, delegate { return BuildNetworkSegmentsLayer("network_unknown"); }, null);
            RegisterLayer("districts", "district_manager", false, delegate { return BuildAreaLayer("districts"); }, null);
            RegisterLayer("park_areas", "district_manager", false, delegate { return BuildAreaLayer("park_areas"); }, null);
            RegisterLayer("industry_areas", "district_manager", false, delegate { return BuildAreaLayer("industry_areas"); }, null);
            RegisterLayer("campus_areas", "district_manager", false, delegate { return BuildAreaLayer("campus_areas"); }, null);
            RegisterLayer("airport_areas", "district_manager", false, delegate { return BuildAreaLayer("airport_areas"); }, null);
            RegisterLayer("pedestrian_areas", "district_manager", false, delegate { return BuildAreaLayer("pedestrian_areas"); }, null);
            RegisterLayer("buildings", "building_manager", false, delegate { return BuildBuildingLayer("buildings"); }, "buildings_cs.csv");
            RegisterLayer("transit_facilities", "building_manager", false, delegate { return BuildBuildingLayer("transit_facilities"); }, null);
            RegisterLayer("transit_hubs", "building_manager", true, delegate { return BuildBuildingLayer("transit_hubs"); }, null);
            RegisterLayer("water_facilities", "building_manager", false, delegate { return BuildBuildingLayer("water_facilities"); }, null);
            RegisterLayer("fishing_facilities", "building_manager", false, delegate { return BuildBuildingLayer("fishing_facilities"); }, null);
            RegisterLayer("pedestrian_service_points", "building_manager", false, delegate { return BuildBuildingLayer("pedestrian_service_points"); }, null);
            RegisterLayer("airport_buildings", "building_manager", false, delegate { return BuildBuildingLayer("airport_buildings"); }, null);
            RegisterLayer("campus_buildings", "building_manager", false, delegate { return BuildBuildingLayer("campus_buildings"); }, null);
            RegisterLayer("industry_buildings", "building_manager", false, delegate { return BuildBuildingLayer("industry_buildings"); }, null);
            RegisterLayer("outside_connection_buildings", "building_manager", false, delegate { return BuildBuildingLayer("outside_connection_buildings"); }, null);
            RegisterLayer("transit_lines", "transport_manager", true, delegate { return BuildTransitLinesLayer(); }, null);
            RegisterLayer("transit_stops", "transport_manager", true, delegate { return BuildTransitStopsLayer(); }, null);
            RegisterLayer("trees", "tree_manager", false, delegate { return BuildTreesLayer(); }, "trees_cs.csv");
            RegisterLayer("props", "prop_manager", true, delegate { return BuildPropsLayer(); }, null);
            RegisterLayer("water_sources", "water_manager", false, delegate { return BuildWaterSourcesLayer(); }, null);
            RegisterLayer("zones", "zone_manager", false, delegate { return BuildZonesLayer(); }, "zones_cs.csv");
        }

        private void RegisterLayer(string name, string sourceSystem, bool experimental, Func<GeoSkylinesLayer> builder, string legacyCsvAlias)
        {
            GeoSkylinesExportLayerDefinition definition = new GeoSkylinesExportLayerDefinition();
            definition.Name = name;
            definition.SourceSystem = sourceSystem;
            definition.Experimental = experimental;
            definition.Builder = builder;
            definition.LegacyCsvAlias = legacyCsvAlias;
            layerDefinitions[name] = definition;
        }

        private void ExecuteExport(IEnumerable<string> layersToExport)
        {
            if (!confloaded)
            {
                return;
            }

            string outputDirectory = config.GetOutputDirectory();
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            GeoSkylinesManifest manifest = new GeoSkylinesManifest();
            manifest.MapName = mapName;
            manifest.OutputDirectory = outputDirectory;
            manifest.ConfigPath = config.ConfigPath;
            manifest.Formats.AddRange(config.GetExportFormats());

            string[] configuredFormats = config.GetExportFormats();
            bool requiresGeoJsonForCli = configuredFormats.Any(delegate(string format) { return format == "shp" || format == "parquet"; });

            foreach (string layerName in layersToExport.Distinct(StringComparer.OrdinalIgnoreCase))
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
            GeoSkylinesExportLayerDefinition definition;
            if (!layerDefinitions.TryGetValue(layerName, out definition))
            {
                GeoSkylinesLayer emptyLayer = new GeoSkylinesLayer(layerName);
                emptyLayer.Warnings.Add("Unknown layer: " + layerName);
                return emptyLayer;
            }

            GeoSkylinesLayer layer = definition.Builder();
            if (layer.SourceSystem == "unknown")
            {
                layer.SourceSystem = definition.SourceSystem;
            }

            layer.Experimental = definition.Experimental;
            return layer;
        }

        private GeoSkylinesLayer CreateLayer(string name)
        {
            GeoSkylinesLayer layer = new GeoSkylinesLayer(name);
            GeoSkylinesExportLayerDefinition definition;
            if (layerDefinitions.TryGetValue(name, out definition))
            {
                layer.SourceSystem = definition.SourceSystem;
                layer.Experimental = definition.Experimental;
            }

            return layer;
        }

        private GeoSkylinesLayer BuildNetworkSegmentsLayer(string targetLayer)
        {
            EnsureNetworkLayerCache();
            return networkLayerCache[targetLayer];
        }

        private GeoSkylinesLayer BuildNetworkNodesLayer(string targetLayer)
        {
            EnsureNetworkLayerCache();
            return networkLayerCache[targetLayer];
        }

        private void EnsureNetworkLayerCache()
        {
            if (networkCacheLoaded)
            {
                return;
            }

            networkCacheLoaded = true;
            string[] networkLayers = new string[]
            {
                "roads",
                "rails",
                "metro_tracks",
                "tram_tracks",
                "monorail_tracks",
                "trolleybus_roads",
                "pedestrian_streets",
                "pedestrian_paths",
                "ship_paths",
                "fishing_paths",
                "canals",
                "water_pipes",
                "transport_guideways",
                "outside_connection_nodes",
                "outside_connection_segments",
                "network_nodes",
                "network_unknown"
            };

            foreach (string layerName in networkLayers)
            {
                networkLayerCache[layerName] = CreateLayer(layerName);
            }

            NetSegment[] segments = netManager.m_segments.m_buffer;
            for (int i = 0; i < segments.Length; i++)
            {
                NetSegment segment = segments[i];
                if (segment.m_startNode == 0 || segment.m_endNode == 0 || segment.Info == null)
                {
                    continue;
                }

                if (!WithinExportCoords(segment.m_middlePosition))
                {
                    continue;
                }

                NetworkClassification classification = ClassifyNetworkSegment((ushort)i, segment);
                GeoSkylinesLayer layer = networkLayerCache[classification.LayerName];
                GeoSkylinesFeature feature = new GeoSkylinesFeature(classification.LayerName, i.ToString(CultureInfo.InvariantCulture), BuildSegmentGeometry(segment));
                feature.Attributes.Add("id", i.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("name", GetSegmentName((ushort)i, segment));
                feature.Attributes.Add("prefab", SafeString(segment.Info.name));
                feature.Attributes.Add("service", segment.Info.m_class.m_service.ToString());
                feature.Attributes.Add("sub_service", segment.Info.m_class.m_subService.ToString());
                feature.Attributes.Add("ai_type", classification.AIType);
                feature.Attributes.Add("lane_types", classification.LaneTypes);
                feature.Attributes.Add("transport_type", classification.TransportType);
                feature.Attributes.Add("vehicle_types", classification.VehicleTypes);
                feature.Attributes.Add("segment_flags", segment.m_flags.ToString());
                feature.Attributes.Add("segment_flags2", segment.m_flags2.ToString());
                feature.Attributes.Add("node_start_flags", netManager.m_nodes.m_buffer[segment.m_startNode].m_flags.ToString());
                feature.Attributes.Add("node_end_flags", netManager.m_nodes.m_buffer[segment.m_endNode].m_flags.ToString());
                feature.Attributes.Add("classification_layer", classification.LayerName);
                feature.Attributes.Add("classification_status", classification.ClassificationStatus);
                feature.Attributes.Add("elevation_start", GetElevationAtNode(segment.m_startNode).ToString("R", CultureInfo.InvariantCulture));
                feature.Attributes.Add("elevation_end", GetElevationAtNode(segment.m_endNode).ToString("R", CultureInfo.InvariantCulture));

                if (includeExtendedAttributes)
                {
                    AddMemberAttributes(feature.Attributes, segment, "segment");
                }

                layer.Features.Add(feature);
            }

            NetNode[] nodes = netManager.m_nodes.m_buffer;
            for (int i = 0; i < nodes.Length; i++)
            {
                NetNode node = nodes[i];
                if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
                {
                    continue;
                }

                if (!WithinExportCoords(node.m_position))
                {
                    continue;
                }

                string connectedLayers = string.Join("|", GetConnectedSegmentLayers(node).Distinct().OrderBy(delegate(string item) { return item; }).ToArray());
                byte districtId = districtManager.GetDistrict(node.m_position);
                byte parkId = districtManager.GetPark(node.m_position);

                if ((node.m_flags & NetNode.Flags.Outside) != NetNode.Flags.None)
                {
                    GeoSkylinesFeature outsideFeature = new GeoSkylinesFeature("outside_connection_nodes", i.ToString(CultureInfo.InvariantCulture), CreatePointGeometry(GamePosition2LatLng(node.m_position)));
                    outsideFeature.Attributes.Add("id", i.ToString(CultureInfo.InvariantCulture));
                    outsideFeature.Attributes.Add("flags", node.m_flags.ToString());
                    outsideFeature.Attributes.Add("flags2", node.m_flags2.ToString());
                    outsideFeature.Attributes.Add("connected_layers", connectedLayers);
                    outsideFeature.Attributes.Add("district_id", districtId.ToString(CultureInfo.InvariantCulture));
                    outsideFeature.Attributes.Add("park_id", parkId.ToString(CultureInfo.InvariantCulture));
                    networkLayerCache["outside_connection_nodes"].Features.Add(outsideFeature);
                    continue;
                }

                GeoSkylinesFeature feature = new GeoSkylinesFeature("network_nodes", i.ToString(CultureInfo.InvariantCulture), CreatePointGeometry(GamePosition2LatLng(node.m_position)));
                feature.Attributes.Add("id", i.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("flags", node.m_flags.ToString());
                feature.Attributes.Add("flags2", node.m_flags2.ToString());
                feature.Attributes.Add("connected_layers", connectedLayers);
                feature.Attributes.Add("district_id", districtId.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("park_id", parkId.ToString(CultureInfo.InvariantCulture));
                networkLayerCache["network_nodes"].Features.Add(feature);
            }
        }

        private NetworkClassification ClassifyNetworkSegment(ushort segmentId, NetSegment segment)
        {
            NetworkClassification classification = new NetworkClassification();
            classification.AIType = GetNetAIType(segment.Info);
            classification.LaneTypes = GetLaneTypes(segment.Info);
            classification.VehicleTypes = GetVehicleTypes(segment.Info);
            classification.TransportType = GetPrimaryTransportType(segment.Info, classification.AIType, classification.VehicleTypes);
            classification.ClassificationStatus = "net_ai";

            if (IsOutsideConnectionSegment(segment))
            {
                classification.LayerName = "outside_connection_segments";
                classification.ClassificationStatus = "outside_connection";
                return classification;
            }

            switch (classification.AIType)
            {
                case "CanalAI":
                    classification.LayerName = "canals";
                    return classification;
                case "ShipPathAI":
                    classification.LayerName = "ship_paths";
                    return classification;
                case "FishingPathAI":
                    classification.LayerName = "fishing_paths";
                    return classification;
                case "WaterPipeAI":
                    classification.LayerName = "water_pipes";
                    return classification;
                case "PedestrianZoneRoadAI":
                case "PedestrianZoneBridgeAI":
                    classification.LayerName = "pedestrian_streets";
                    return classification;
                case "PedestrianWayAI":
                case "PedestrianPathAI":
                case "PedestrianBridgeAI":
                case "PedestrianTunnelAI":
                    classification.LayerName = "pedestrian_paths";
                    return classification;
                case "TransportPathAI":
                case "TransportLineAI":
                case "CableCarPathAI":
                    classification.LayerName = "transport_guideways";
                    return classification;
                case "TrainTrackAI":
                    classification.LayerName = "rails";
                    return classification;
                case "MetroTrackAI":
                    classification.LayerName = "metro_tracks";
                    return classification;
                case "MonorailTrackAI":
                    classification.LayerName = "monorail_tracks";
                    return classification;
            }

            string subService = segment.Info.m_class.m_subService.ToString();
            if (subService.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                classification.LayerName = "rails";
                classification.ClassificationStatus = "sub_service";
                return classification;
            }

            if (subService.IndexOf("Metro", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                classification.LayerName = "metro_tracks";
                classification.ClassificationStatus = "sub_service";
                return classification;
            }

            if (subService.IndexOf("Monorail", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                classification.LayerName = "monorail_tracks";
                classification.ClassificationStatus = "sub_service";
                return classification;
            }

            if (subService.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0 || classification.VehicleTypes.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                classification.LayerName = "tram_tracks";
                classification.ClassificationStatus = "sub_service";
                return classification;
            }

            if (subService.IndexOf("Trolleybus", StringComparison.OrdinalIgnoreCase) >= 0 || classification.VehicleTypes.IndexOf("Trolleybus", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                classification.LayerName = "trolleybus_roads";
                classification.ClassificationStatus = "sub_service";
                return classification;
            }

            if (classification.AIType == "RoadAI" || classification.AIType == "RaceRoadAI")
            {
                classification.LayerName = "roads";
                return classification;
            }

            classification.LayerName = "network_unknown";
            classification.ClassificationStatus = "unknown";
            return classification;
        }

        private bool IsOutsideConnectionSegment(NetSegment segment)
        {
            NetNode startNode = netManager.m_nodes.m_buffer[segment.m_startNode];
            NetNode endNode = netManager.m_nodes.m_buffer[segment.m_endNode];
            return (startNode.m_flags & NetNode.Flags.Outside) != NetNode.Flags.None
                || (endNode.m_flags & NetNode.Flags.Outside) != NetNode.Flags.None;
        }

        private IEnumerable<string> GetConnectedSegmentLayers(NetNode node)
        {
            List<string> layers = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                ushort segmentId = ConvertToUshort(GetMemberValue(node, "m_segment" + i.ToString(CultureInfo.InvariantCulture)));
                if (segmentId == 0)
                {
                    continue;
                }

                NetSegment segment = netManager.m_segments.m_buffer[segmentId];
                if (segment.Info == null)
                {
                    continue;
                }

                layers.Add(ClassifyNetworkSegment(segmentId, segment).LayerName);
            }

            return layers;
        }

        private GeoSkylinesLayer BuildBuildingLayer(string layerName)
        {
            EnsureBuildingLayerCache();
            return buildingLayerCache[layerName];
        }

        private void EnsureBuildingLayerCache()
        {
            if (buildingCacheLoaded)
            {
                return;
            }

            buildingCacheLoaded = true;
            string[] buildingLayers = new string[]
            {
                "buildings",
                "transit_facilities",
                "transit_hubs",
                "water_facilities",
                "fishing_facilities",
                "pedestrian_service_points",
                "airport_buildings",
                "campus_buildings",
                "industry_buildings",
                "outside_connection_buildings"
            };

            foreach (string layerName in buildingLayers)
            {
                buildingLayerCache[layerName] = CreateLayer(layerName);
            }

            Building[] buildings = buildingManager.m_buildings.m_buffer;
            for (int i = 0; i < buildings.Length; i++)
            {
                Building building = buildings[i];
                if (building.m_position == Vector3.zero || building.Info == null)
                {
                    continue;
                }

                if (!WithinExportCoords(building.m_position))
                {
                    continue;
                }

                BuildingClassification classification = ClassifyBuilding((ushort)i, building);
                AddBuildingFeature(buildingLayerCache["buildings"], "buildings", (ushort)i, building, classification);

                if (classification.IsTransitFacility)
                {
                    AddBuildingFeature(buildingLayerCache["transit_facilities"], "transit_facilities", (ushort)i, building, classification);
                }
                if (classification.IsTransitHub)
                {
                    AddBuildingFeature(buildingLayerCache["transit_hubs"], "transit_hubs", (ushort)i, building, classification);
                }
                if (classification.IsWaterFacility)
                {
                    AddBuildingFeature(buildingLayerCache["water_facilities"], "water_facilities", (ushort)i, building, classification);
                }
                if (classification.IsFishingFacility)
                {
                    AddBuildingFeature(buildingLayerCache["fishing_facilities"], "fishing_facilities", (ushort)i, building, classification);
                }
                if (classification.IsPedestrianServicePoint)
                {
                    AddBuildingFeature(buildingLayerCache["pedestrian_service_points"], "pedestrian_service_points", (ushort)i, building, classification);
                }
                if (classification.IsAirportBuilding)
                {
                    AddBuildingFeature(buildingLayerCache["airport_buildings"], "airport_buildings", (ushort)i, building, classification);
                }
                if (classification.IsCampusBuilding)
                {
                    AddBuildingFeature(buildingLayerCache["campus_buildings"], "campus_buildings", (ushort)i, building, classification);
                }
                if (classification.IsIndustryBuilding)
                {
                    AddBuildingFeature(buildingLayerCache["industry_buildings"], "industry_buildings", (ushort)i, building, classification);
                }
                if (classification.IsOutsideConnectionBuilding)
                {
                    AddBuildingFeature(buildingLayerCache["outside_connection_buildings"], "outside_connection_buildings", (ushort)i, building, classification);
                }
            }
        }

        private void AddBuildingFeature(GeoSkylinesLayer layer, string layerName, ushort buildingId, Building building, BuildingClassification classification)
        {
            GeoSkylinesGeometry geometry = BuildBuildingGeometry(building);
            string featureId = buildingId.ToString(CultureInfo.InvariantCulture);
            GeoSkylinesFeature feature = new GeoSkylinesFeature(layerName, featureId, geometry);
            LatLng centroid = GamePosition2LatLng(building.m_position);
            byte districtId = districtManager.GetDistrict(building.m_position);
            byte parkId = districtManager.GetPark(building.m_position);
            feature.Attributes.Add("id", featureId);
            feature.Attributes.Add("name", GetBuildingName(buildingId, building));
            feature.Attributes.Add("prefab", SafeString(building.Info.name));
            feature.Attributes.Add("service", building.Info.m_class.m_service.ToString());
            feature.Attributes.Add("sub_service", building.Info.m_class.m_subService.ToString());
            feature.Attributes.Add("class_level", building.Info.m_class.m_level.ToString());
            feature.Attributes.Add("ai_type", classification.AIType);
            feature.Attributes.Add("classification_layer", classification.PrimaryLayer);
            feature.Attributes.Add("classification_status", classification.ClassificationStatus);
            feature.Attributes.Add("width", building.Width.ToString(CultureInfo.InvariantCulture));
            feature.Attributes.Add("length", building.Length.ToString(CultureInfo.InvariantCulture));
            feature.Attributes.Add("angle", building.m_angle.ToString("R", CultureInfo.InvariantCulture));
            feature.Attributes.Add("centroid_lon", centroid.Lng.ToString("R", CultureInfo.InvariantCulture));
            feature.Attributes.Add("centroid_lat", centroid.Lat.ToString("R", CultureInfo.InvariantCulture));
            feature.Attributes.Add("district_id", districtId.ToString(CultureInfo.InvariantCulture));
            feature.Attributes.Add("park_id", parkId.ToString(CultureInfo.InvariantCulture));

            if (includeExtendedAttributes)
            {
                AddMemberAttributes(feature.Attributes, building, "building");
            }

            layer.Features.Add(feature);
        }

        private BuildingClassification ClassifyBuilding(ushort buildingId, Building building)
        {
            BuildingClassification classification = new BuildingClassification();
            object ai = GetMemberValue(building.Info, "m_buildingAI");
            classification.AIType = ai == null ? "unknown" : ai.GetType().Name;
            classification.PrimaryLayer = "buildings";
            classification.ClassificationStatus = "fallback";

            string service = building.Info.m_class.m_service.ToString();
            string text = SafeLower(BuildSearchText(building.Info));

            classification.IsOutsideConnectionBuilding = classification.AIType == "OutsideConnectionAI";
            classification.IsWaterFacility = classification.AIType == "WaterFacilityAI" || classification.AIType == "WaterCleanerAI" || classification.AIType == "WaterJunctionAI" || service.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0;
            classification.IsFishingFacility = classification.AIType == "FishingHarborAI";
            classification.IsAirportBuilding = classification.AIType.StartsWith("Airport", StringComparison.OrdinalIgnoreCase) || classification.AIType == "PrivateAirportAI";
            classification.IsCampusBuilding = classification.AIType.IndexOf("Campus", StringComparison.OrdinalIgnoreCase) >= 0;
            classification.IsIndustryBuilding = classification.AIType.IndexOf("Industry", StringComparison.OrdinalIgnoreCase) >= 0;
            classification.IsPedestrianServicePoint = text.Contains("service point");
            classification.IsTransitFacility = classification.AIType == "TransportStationAI" || service.IndexOf("PublicTransport", StringComparison.OrdinalIgnoreCase) >= 0 || classification.IsAirportBuilding;
            classification.IsTransitHub = classification.IsTransitFacility && (text.Contains("hub") || text.Contains("exchange") || text.Contains("terminal"));

            if (classification.IsOutsideConnectionBuilding)
            {
                classification.PrimaryLayer = "outside_connection_buildings";
                classification.ClassificationStatus = "ai_type";
            }
            else if (classification.IsAirportBuilding)
            {
                classification.PrimaryLayer = "airport_buildings";
                classification.ClassificationStatus = "ai_type";
            }
            else if (classification.IsCampusBuilding)
            {
                classification.PrimaryLayer = "campus_buildings";
                classification.ClassificationStatus = "ai_type";
            }
            else if (classification.IsIndustryBuilding)
            {
                classification.PrimaryLayer = "industry_buildings";
                classification.ClassificationStatus = "ai_type";
            }
            else if (classification.IsWaterFacility)
            {
                classification.PrimaryLayer = "water_facilities";
                classification.ClassificationStatus = "ai_type";
            }
            else if (classification.IsFishingFacility)
            {
                classification.PrimaryLayer = "fishing_facilities";
                classification.ClassificationStatus = "ai_type";
            }
            else if (classification.IsPedestrianServicePoint)
            {
                classification.PrimaryLayer = "pedestrian_service_points";
                classification.ClassificationStatus = "prefab_text";
            }
            else if (classification.IsTransitHub)
            {
                classification.PrimaryLayer = "transit_hubs";
                classification.ClassificationStatus = "prefab_text";
            }
            else if (classification.IsTransitFacility)
            {
                classification.PrimaryLayer = "transit_facilities";
                classification.ClassificationStatus = "service";
            }

            return classification;
        }

        private GeoSkylinesLayer BuildAreaLayer(string layerName)
        {
            EnsureAreaLayerCache();
            return areaLayerCache[layerName];
        }

        private void EnsureAreaLayerCache()
        {
            if (areaCacheLoaded)
            {
                return;
            }

            areaCacheLoaded = true;
            string[] areaLayers = new string[] { "districts", "park_areas", "industry_areas", "campus_areas", "airport_areas", "pedestrian_areas" };
            foreach (string layerName in areaLayers)
            {
                areaLayerCache[layerName] = CreateLayer(layerName);
            }

            Dictionary<string, Dictionary<int, AreaAccumulator>> accumulators = new Dictionary<string, Dictionary<int, AreaAccumulator>>(StringComparer.OrdinalIgnoreCase);
            foreach (string layerName in areaLayers)
            {
                accumulators[layerName] = new Dictionary<int, AreaAccumulator>();
            }

            float sampleSize = DistrictManager.DISTRICTGRID_CELL_SIZE;
            int minX = Mathf.FloorToInt(exportXMin / sampleSize);
            int maxX = Mathf.CeilToInt(exportXMax / sampleSize);
            int minZ = Mathf.FloorToInt(exportYMin / sampleSize);
            int maxZ = Mathf.CeilToInt(exportYMax / sampleSize);

            for (int x = minX; x < maxX; x++)
            {
                float worldX = (x * sampleSize) + (sampleSize * 0.5f);
                for (int z = minZ; z < maxZ; z++)
                {
                    float worldZ = (z * sampleSize) + (sampleSize * 0.5f);
                    Vector3 worldPosition = new Vector3(worldX, 0f, worldZ);

                    byte districtId = districtManager.GetDistrict(worldPosition);
                    if (districtId > 0)
                    {
                        AddAreaCell(accumulators["districts"], districtId, "districts", districtManager.GetDistrictName(districtId), "district", x, z);
                    }

                    byte parkId = districtManager.GetPark(worldPosition);
                    if (parkId > 0)
                    {
                        DistrictPark park = districtManager.m_parks.m_buffer[parkId];
                        string parkLayer = MapParkTypeToLayer(park.m_parkType);
                        if (parkLayer != null)
                        {
                            AddAreaCell(accumulators[parkLayer], parkId, parkLayer, districtManager.GetParkName(parkId), park.m_parkType.ToString(), x, z);
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, Dictionary<int, AreaAccumulator>> kvp in accumulators)
            {
                GeoSkylinesLayer layer = areaLayerCache[kvp.Key];
                foreach (AreaAccumulator area in kvp.Value.Values.OrderBy(delegate(AreaAccumulator item) { return item.AreaId; }))
                {
                    GeoSkylinesGeometry geometry = BuildAreaGeometry(area.Cells, sampleSize);
                    GeoSkylinesFeature feature = new GeoSkylinesFeature(layer.Name, area.AreaId.ToString(CultureInfo.InvariantCulture), geometry);
                    feature.Attributes.Add("id", area.AreaId.ToString(CultureInfo.InvariantCulture));
                    feature.Attributes.Add("name", area.Name);
                    feature.Attributes.Add("area_type", area.AreaType);
                    feature.Attributes.Add("sample_cell_count", area.Cells.Count.ToString(CultureInfo.InvariantCulture));
                    layer.Features.Add(feature);
                }

                if (layer.Features.Count == 0)
                {
                    layer.Warnings.Add("No features were detected for " + layer.Name + " in the loaded save.");
                }
            }
        }

        private void AddAreaCell(Dictionary<int, AreaAccumulator> target, int areaId, string layerName, string name, string areaType, int x, int z)
        {
            AreaAccumulator accumulator;
            if (!target.TryGetValue(areaId, out accumulator))
            {
                accumulator = new AreaAccumulator();
                accumulator.AreaId = areaId;
                accumulator.LayerName = layerName;
                accumulator.Name = string.IsNullOrEmpty(name) ? (areaType + " " + areaId.ToString(CultureInfo.InvariantCulture)) : name;
                accumulator.AreaType = areaType;
                target.Add(areaId, accumulator);
            }

            accumulator.Cells.Add(MakeCellKey(x, z));
        }

        private static string MapParkTypeToLayer(DistrictPark.ParkType parkType)
        {
            switch (parkType)
            {
                case DistrictPark.ParkType.Generic:
                case DistrictPark.ParkType.AmusementPark:
                case DistrictPark.ParkType.Zoo:
                case DistrictPark.ParkType.NatureReserve:
                    return "park_areas";
                case DistrictPark.ParkType.Industry:
                case DistrictPark.ParkType.Farming:
                case DistrictPark.ParkType.Forestry:
                case DistrictPark.ParkType.Ore:
                case DistrictPark.ParkType.Oil:
                    return "industry_areas";
                case DistrictPark.ParkType.GenericCampus:
                case DistrictPark.ParkType.TradeSchool:
                case DistrictPark.ParkType.LiberalArts:
                case DistrictPark.ParkType.University:
                    return "campus_areas";
                case DistrictPark.ParkType.Airport:
                    return "airport_areas";
                case DistrictPark.ParkType.PedestrianZone:
                    return "pedestrian_areas";
                default:
                    return null;
            }
        }

        private GeoSkylinesGeometry BuildAreaGeometry(HashSet<long> cells, float cellSize)
        {
            Dictionary<GridEdgeKey, DirectedGridEdge> edgeMap = new Dictionary<GridEdgeKey, DirectedGridEdge>();
            foreach (long key in cells)
            {
                int x = ExtractCellX(key);
                int z = ExtractCellZ(key);
                GridPoint p0 = new GridPoint(x, z);
                GridPoint p1 = new GridPoint(x + 1, z);
                GridPoint p2 = new GridPoint(x + 1, z + 1);
                GridPoint p3 = new GridPoint(x, z + 1);
                AddOrRemoveEdge(edgeMap, new DirectedGridEdge(p0, p1));
                AddOrRemoveEdge(edgeMap, new DirectedGridEdge(p1, p2));
                AddOrRemoveEdge(edgeMap, new DirectedGridEdge(p2, p3));
                AddOrRemoveEdge(edgeMap, new DirectedGridEdge(p3, p0));
            }

            List<List<GridPoint>> loops = TraceLoops(edgeMap);
            List<List<GridPoint>> outerLoops = new List<List<GridPoint>>();
            List<List<GridPoint>> holeLoops = new List<List<GridPoint>>();
            foreach (List<GridPoint> loop in loops)
            {
                if (ComputeSignedArea(loop) >= 0d)
                {
                    outerLoops.Add(loop);
                }
                else
                {
                    holeLoops.Add(loop);
                }
            }

            List<List<List<GridPoint>>> groupedPolygons = new List<List<List<GridPoint>>>();
            foreach (List<GridPoint> outerLoop in outerLoops)
            {
                groupedPolygons.Add(new List<List<GridPoint>> { outerLoop });
            }

            foreach (List<GridPoint> holeLoop in holeLoops)
            {
                GridPoint samplePoint = holeLoop[0];
                bool assigned = false;
                for (int i = 0; i < groupedPolygons.Count; i++)
                {
                    if (IsPointInPolygon(groupedPolygons[i][0], samplePoint))
                    {
                        groupedPolygons[i].Add(holeLoop);
                        assigned = true;
                        break;
                    }
                }

                if (!assigned)
                {
                    groupedPolygons.Add(new List<List<GridPoint>> { holeLoop });
                }
            }

            if (groupedPolygons.Count == 1)
            {
                GeoSkylinesGeometry polygon = new GeoSkylinesGeometry("Polygon");
                foreach (List<GridPoint> ring in groupedPolygons[0])
                {
                    polygon.Parts.Add(ConvertRing(ring, cellSize));
                }
                return polygon;
            }

            GeoSkylinesGeometry multiPolygon = new GeoSkylinesGeometry("MultiPolygon");
            foreach (List<List<GridPoint>> polygonRings in groupedPolygons)
            {
                List<List<GeoSkylinesCoordinate>> polygon = new List<List<GeoSkylinesCoordinate>>();
                foreach (List<GridPoint> ring in polygonRings)
                {
                    polygon.Add(ConvertRing(ring, cellSize));
                }
                multiPolygon.Polygons.Add(polygon);
            }
            return multiPolygon;
        }

        private static void AddOrRemoveEdge(Dictionary<GridEdgeKey, DirectedGridEdge> edgeMap, DirectedGridEdge edge)
        {
            GridEdgeKey key = new GridEdgeKey(edge.Start, edge.End);
            if (edgeMap.ContainsKey(key))
            {
                edgeMap.Remove(key);
            }
            else
            {
                edgeMap.Add(key, edge);
            }
        }

        private static List<List<GridPoint>> TraceLoops(Dictionary<GridEdgeKey, DirectedGridEdge> edgeMap)
        {
            Dictionary<GridPoint, List<GridPoint>> adjacency = new Dictionary<GridPoint, List<GridPoint>>();
            foreach (DirectedGridEdge edge in edgeMap.Values)
            {
                List<GridPoint> targets;
                if (!adjacency.TryGetValue(edge.Start, out targets))
                {
                    targets = new List<GridPoint>();
                    adjacency.Add(edge.Start, targets);
                }
                targets.Add(edge.End);
            }

            HashSet<GridEdgeKey> visited = new HashSet<GridEdgeKey>();
            List<List<GridPoint>> loops = new List<List<GridPoint>>();
            foreach (DirectedGridEdge edge in edgeMap.Values)
            {
                GridEdgeKey startKey = new GridEdgeKey(edge.Start, edge.End);
                if (visited.Contains(startKey))
                {
                    continue;
                }

                List<GridPoint> loop = new List<GridPoint>();
                GridPoint start = edge.Start;
                GridPoint current = edge.Start;
                GridPoint next = edge.End;
                loop.Add(start);
                while (true)
                {
                    visited.Add(new GridEdgeKey(current, next));
                    loop.Add(next);
                    if (next.Equals(start))
                    {
                        break;
                    }

                    List<GridPoint> candidates = adjacency[next];
                    GridPoint continuation = candidates[0];
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        GridEdgeKey candidateKey = new GridEdgeKey(next, candidates[i]);
                        if (!visited.Contains(candidateKey))
                        {
                            continuation = candidates[i];
                            break;
                        }
                    }

                    current = next;
                    next = continuation;
                }

                loops.Add(SimplifyLoop(loop));
            }

            return loops;
        }

        private static List<GridPoint> SimplifyLoop(List<GridPoint> points)
        {
            if (points.Count < 4)
            {
                return points;
            }

            List<GridPoint> simplified = new List<GridPoint>();
            for (int i = 0; i < points.Count; i++)
            {
                GridPoint previous = points[(i - 1 + points.Count) % points.Count];
                GridPoint current = points[i];
                GridPoint next = points[(i + 1) % points.Count];

                int dx1 = current.X - previous.X;
                int dz1 = current.Z - previous.Z;
                int dx2 = next.X - current.X;
                int dz2 = next.Z - current.Z;
                if ((dx1 == dx2 && dz1 == dz2) && i != points.Count - 1)
                {
                    continue;
                }

                simplified.Add(current);
            }

            if (!simplified[0].Equals(simplified[simplified.Count - 1]))
            {
                simplified.Add(simplified[0]);
            }

            return simplified;
        }

        private static double ComputeSignedArea(List<GridPoint> ring)
        {
            double area = 0d;
            for (int i = 0; i < ring.Count - 1; i++)
            {
                area += (ring[i].X * ring[i + 1].Z) - (ring[i + 1].X * ring[i].Z);
            }
            return area * 0.5d;
        }

        private static bool IsPointInPolygon(List<GridPoint> ring, GridPoint point)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                if (((ring[i].Z > point.Z) != (ring[j].Z > point.Z))
                    && (point.X < (ring[j].X - ring[i].X) * (point.Z - ring[i].Z) / (double)(ring[j].Z - ring[i].Z) + ring[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private List<GeoSkylinesCoordinate> ConvertRing(List<GridPoint> ring, float cellSize)
        {
            List<GeoSkylinesCoordinate> converted = new List<GeoSkylinesCoordinate>();
            for (int i = 0; i < ring.Count; i++)
            {
                float worldX = ring[i].X * cellSize;
                float worldZ = ring[i].Z * cellSize;
                converted.Add(ToCoordinate(new Vector3(worldX, 0f, worldZ)));
            }
            return converted;
        }

        private static long MakeCellKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        private static int ExtractCellX(long key)
        {
            return (int)(key >> 32);
        }

        private static int ExtractCellZ(long key)
        {
            return (int)(key & 0xffffffff);
        }

        private GeoSkylinesLayer BuildZonesLayer()
        {
            if (zonesLayerCache != null)
            {
                return zonesLayerCache;
            }

            GeoSkylinesLayer layer = CreateLayer("zones");
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

            zonesLayerCache = layer;
            return zonesLayerCache;
        }

        private GeoSkylinesLayer BuildTreesLayer()
        {
            if (treesLayerCache != null)
            {
                return treesLayerCache;
            }

            GeoSkylinesLayer layer = CreateLayer("trees");
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
                GeoSkylinesFeature feature = new GeoSkylinesFeature("trees", exportId.ToString(CultureInfo.InvariantCulture), CreatePointGeometry(GamePosition2LatLng(tree.Position)));
                feature.Attributes.Add("id", exportId.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("tree_index", i.ToString(CultureInfo.InvariantCulture));

                if (includeExtendedAttributes)
                {
                    AddMemberAttributes(feature.Attributes, tree, "tree");
                }

                layer.Features.Add(feature);
            }

            treesLayerCache = layer;
            return treesLayerCache;
        }

        private GeoSkylinesLayer BuildPropsLayer()
        {
            if (propsLayerCache != null)
            {
                return propsLayerCache;
            }

            GeoSkylinesLayer layer = CreateLayer("props");
            PropInstance[] props = propManager.m_props.m_buffer;
            for (int i = 0; i < props.Length; i++)
            {
                PropInstance prop = props[i];
                if ((((PropInstance.Flags)prop.m_flags) & PropInstance.Flags.Created) == PropInstance.Flags.None)
                {
                    continue;
                }

                Vector3 position = prop.Position;
                if (!WithinExportCoords(position))
                {
                    continue;
                }

                GeoSkylinesFeature feature = new GeoSkylinesFeature("props", i.ToString(CultureInfo.InvariantCulture), CreatePointGeometry(GamePosition2LatLng(position)));
                feature.Attributes.Add("id", i.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("flags", prop.m_flags.ToString());
                feature.Attributes.Add("info_index", prop.m_infoIndex.ToString(CultureInfo.InvariantCulture));
                PropInfo info = PrefabCollection<PropInfo>.GetPrefab(prop.m_infoIndex);
                feature.Attributes.Add("prefab", info == null ? string.Empty : SafeString(info.name));

                if (includeExtendedAttributes)
                {
                    AddMemberAttributes(feature.Attributes, prop, "prop");
                }

                layer.Features.Add(feature);
            }

            propsLayerCache = layer;
            return propsLayerCache;
        }

        private GeoSkylinesLayer BuildWaterSourcesLayer()
        {
            if (waterSourcesLayerCache != null)
            {
                return waterSourcesLayerCache;
            }

            GeoSkylinesLayer layer = CreateLayer("water_sources");
            object waterSourcesWrapper = GetMemberValue(WaterManager.instance, "m_waterSources");
            Array waterSourceBuffer = waterSourcesWrapper == null ? null : GetMemberValue(waterSourcesWrapper, "m_buffer") as Array;
            if (waterSourceBuffer == null)
            {
                layer.Warnings.Add("WaterManager.m_waterSources.m_buffer was not available.");
                waterSourcesLayerCache = layer;
                return waterSourcesLayerCache;
            }

            for (int i = 0; i < waterSourceBuffer.Length; i++)
            {
                object source = waterSourceBuffer.GetValue(i);
                ushort type = ConvertToUshort(GetMemberValue(source, "m_type"));
                uint water = ConvertToUInt(GetMemberValue(source, "m_water"));
                uint inputRate = ConvertToUInt(GetMemberValue(source, "m_inputRate"));
                uint outputRate = ConvertToUInt(GetMemberValue(source, "m_outputRate"));
                Vector3 inputPosition = ConvertToVector3(GetMemberValue(source, "m_inputPosition"));
                Vector3 outputPosition = ConvertToVector3(GetMemberValue(source, "m_outputPosition"));
                if (type == 0 && water == 0 && inputRate == 0 && outputRate == 0 && inputPosition == Vector3.zero && outputPosition == Vector3.zero)
                {
                    continue;
                }

                if (!WithinExportCoords(inputPosition) && !WithinExportCoords(outputPosition))
                {
                    continue;
                }

                GeoSkylinesGeometry geometry;
                if (inputPosition == outputPosition)
                {
                    geometry = CreatePointGeometry(GamePosition2LatLng(inputPosition));
                }
                else
                {
                    GeoSkylinesGeometry line = new GeoSkylinesGeometry("LineString");
                    line.Parts.Add(new List<GeoSkylinesCoordinate> { ToCoordinate(inputPosition), ToCoordinate(outputPosition) });
                    geometry = line;
                }

                GeoSkylinesFeature feature = new GeoSkylinesFeature("water_sources", i.ToString(CultureInfo.InvariantCulture), geometry);
                feature.Attributes.Add("id", i.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("type", MapWaterSourceType(type));
                feature.Attributes.Add("target", ConvertToUshort(GetMemberValue(source, "m_target")).ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("water", water.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("pollution", ConvertToUInt(GetMemberValue(source, "m_pollution")).ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("input_rate", inputRate.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("output_rate", outputRate.ToString(CultureInfo.InvariantCulture));
                feature.Attributes.Add("flow", ConvertToUInt(GetMemberValue(source, "m_flow")).ToString(CultureInfo.InvariantCulture));
                layer.Features.Add(feature);
            }

            waterSourcesLayerCache = layer;
            return waterSourcesLayerCache;
        }

        private string MapWaterSourceType(ushort type)
        {
            switch (type)
            {
                case 1:
                    return "natural";
                case 2:
                    return "facility";
                case 3:
                    return "cleaner";
                default:
                    return "none";
            }
        }

        private GeoSkylinesLayer BuildTransitLinesLayer()
        {
            GeoSkylinesLayer layer = CreateLayer("transit_lines");
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

                    coordinates.Add(ToCoordinate(stopPosition));
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
                feature.Attributes.Add("source_runtime_type", snapshot.RuntimeType);
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
            GeoSkylinesLayer layer = CreateLayer("transit_stops");
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
                    feature.Attributes.Add("source_runtime_type", snapshot.RuntimeType);
                    layer.Features.Add(feature);
                }
            }

            if (layer.Features.Count == 0 && layer.Warnings.Count == 0)
            {
                layer.Warnings.Add("No transit stops were detected in the loaded save.");
            }

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
                    snapshot.RuntimeType = line.GetType().Name;
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
            GeoSkylinesExportLayerDefinition definition;
            if (!layerDefinitions.TryGetValue(layer.Name, out definition) || string.IsNullOrEmpty(definition.LegacyCsvAlias))
            {
                return;
            }

            string legacyPath = Path.Combine(GeoSkylinesConfig.GetFilesDirectory(), definition.LegacyCsvAlias);
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

        private string GetNetAIType(NetInfo info)
        {
            object ai = GetMemberValue(info, "m_netAI");
            return ai == null ? "unknown" : ai.GetType().Name;
        }

        private string GetLaneTypes(NetInfo info)
        {
            if (info == null || info.m_lanes == null)
            {
                return string.Empty;
            }

            return string.Join("|", info.m_lanes.Select(delegate(NetInfo.Lane lane) { return lane.m_laneType.ToString(); }).Distinct().OrderBy(delegate(string item) { return item; }).ToArray());
        }

        private string GetVehicleTypes(NetInfo info)
        {
            if (info == null || info.m_lanes == null)
            {
                return string.Empty;
            }

            return string.Join("|", info.m_lanes.Select(delegate(NetInfo.Lane lane) { return lane.m_vehicleType.ToString(); }).Distinct().Where(delegate(string item) { return !string.IsNullOrEmpty(item) && item != "None"; }).OrderBy(delegate(string item) { return item; }).ToArray());
        }

        private string GetPrimaryTransportType(NetInfo info, string aiType, string vehicleTypes)
        {
            string subService = info.m_class.m_subService.ToString();
            if (aiType == "ShipPathAI")
            {
                return "ship";
            }
            if (aiType == "FishingPathAI")
            {
                return "fishing";
            }
            if (aiType == "CanalAI")
            {
                return "canal";
            }
            if (aiType == "WaterPipeAI")
            {
                return "water_pipe";
            }
            if (aiType == "PedestrianZoneRoadAI" || aiType == "PedestrianZoneBridgeAI")
            {
                return "pedestrian_zone";
            }
            if (aiType == "PedestrianWayAI" || aiType == "PedestrianPathAI" || aiType == "PedestrianBridgeAI" || aiType == "PedestrianTunnelAI")
            {
                return "pedestrian";
            }
            if (subService.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0 || aiType == "TrainTrackAI")
            {
                return "train";
            }
            if (subService.IndexOf("Metro", StringComparison.OrdinalIgnoreCase) >= 0 || aiType == "MetroTrackAI")
            {
                return "metro";
            }
            if (subService.IndexOf("Monorail", StringComparison.OrdinalIgnoreCase) >= 0 || aiType == "MonorailTrackAI")
            {
                return "monorail";
            }
            if (subService.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0 || vehicleTypes.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "tram";
            }
            if (subService.IndexOf("Trolleybus", StringComparison.OrdinalIgnoreCase) >= 0 || vehicleTypes.IndexOf("Trolleybus", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "trolleybus";
            }
            if (aiType == "TransportPathAI" || aiType == "TransportLineAI" || aiType == "CableCarPathAI")
            {
                return "guideway";
            }
            return "road";
        }

        private float GetElevationAtNode(ushort nodeId)
        {
            Vector3 position = netManager.m_nodes.m_buffer[nodeId].m_position;
            float terrainHeight = terrainManager.SampleRawHeightSmoothWithWater(position, false, 0f);
            return position.y - terrainHeight;
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

        private static uint ConvertToUInt(object value)
        {
            if (value == null)
            {
                return 0;
            }

            uint converted;
            if (uint.TryParse(value.ToString(), out converted))
            {
                return converted;
            }

            try
            {
                return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static Vector3 ConvertToVector3(object value)
        {
            if (value is Vector3)
            {
                return (Vector3)value;
            }

            return Vector3.zero;
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
