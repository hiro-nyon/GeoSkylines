using System;
using System.Collections.Generic;

namespace GeoSkylines
{
    public sealed class GeoSkylinesCoordinate
    {
        public GeoSkylinesCoordinate(double longitude, double latitude)
        {
            Longitude = longitude;
            Latitude = latitude;
        }

        public double Longitude { get; private set; }
        public double Latitude { get; private set; }
    }

    public sealed class GeoSkylinesGeometry
    {
        public GeoSkylinesGeometry(string geometryType)
        {
            GeometryType = geometryType;
            Parts = new List<List<GeoSkylinesCoordinate>>();
        }

        public string GeometryType { get; private set; }
        public List<List<GeoSkylinesCoordinate>> Parts { get; private set; }
    }

    public sealed class GeoSkylinesFeature
    {
        public GeoSkylinesFeature(string layerName, string featureId, GeoSkylinesGeometry geometry)
        {
            LayerName = layerName;
            FeatureId = featureId;
            Geometry = geometry;
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string LayerName { get; private set; }
        public string FeatureId { get; private set; }
        public GeoSkylinesGeometry Geometry { get; private set; }
        public Dictionary<string, string> Attributes { get; private set; }
    }

    public sealed class GeoSkylinesLayer
    {
        public GeoSkylinesLayer(string name)
        {
            Name = name;
            Features = new List<GeoSkylinesFeature>();
            Warnings = new List<string>();
            Files = new List<string>();
        }

        public string Name { get; private set; }
        public List<GeoSkylinesFeature> Features { get; private set; }
        public List<string> Warnings { get; private set; }
        public List<string> Files { get; private set; }
    }

    public sealed class GeoSkylinesManifest
    {
        public GeoSkylinesManifest()
        {
            GeneratedAtUtc = DateTime.UtcNow;
            Layers = new List<GeoSkylinesLayer>();
            Formats = new List<string>();
            Warnings = new List<string>();
        }

        public DateTime GeneratedAtUtc { get; set; }
        public string MapName { get; set; }
        public string OutputDirectory { get; set; }
        public string ConfigPath { get; set; }
        public List<string> Formats { get; private set; }
        public List<GeoSkylinesLayer> Layers { get; private set; }
        public List<string> Warnings { get; private set; }
        public string CliPath { get; set; }
        public string CliExitCode { get; set; }
        public string CliOutput { get; set; }
    }
}
