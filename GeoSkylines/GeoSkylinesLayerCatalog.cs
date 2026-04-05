using System.Collections.Generic;

namespace GeoSkylines
{
    public sealed class GeoSkylinesLayerGroup
    {
        public GeoSkylinesLayerGroup(string name, string[] layers)
        {
            Name = name;
            Layers = layers;
        }

        public string Name { get; private set; }
        public string[] Layers { get; private set; }
    }

    public static class GeoSkylinesLayerCatalog
    {
        public static readonly string[] AvailableFormats = new string[] { "csv", "geojson", "shp", "parquet" };

        public static readonly string[] AllLayers = new string[]
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
            "network_unknown",
            "districts",
            "park_areas",
            "industry_areas",
            "campus_areas",
            "airport_areas",
            "pedestrian_areas",
            "buildings",
            "transit_facilities",
            "transit_hubs",
            "water_facilities",
            "fishing_facilities",
            "pedestrian_service_points",
            "airport_buildings",
            "campus_buildings",
            "industry_buildings",
            "outside_connection_buildings",
            "transit_lines",
            "transit_stops",
            "trees",
            "props",
            "water_sources",
            "zones"
        };

        public static readonly GeoSkylinesLayerGroup[] LayerGroups = new GeoSkylinesLayerGroup[]
        {
            new GeoSkylinesLayerGroup("Networks", new string[]
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
            }),
            new GeoSkylinesLayerGroup("Areas", new string[]
            {
                "districts",
                "park_areas",
                "industry_areas",
                "campus_areas",
                "airport_areas",
                "pedestrian_areas",
                "zones"
            }),
            new GeoSkylinesLayerGroup("Buildings", new string[]
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
            }),
            new GeoSkylinesLayerGroup("Operations", new string[]
            {
                "transit_lines",
                "transit_stops",
                "trees",
                "props",
                "water_sources"
            })
        };

        public static readonly HashSet<string> KnownLayers = new HashSet<string>(AllLayers);
    }
}
