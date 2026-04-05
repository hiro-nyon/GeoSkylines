using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GeoSkylines
{
    public static class GeoSkylinesCsvWriter
    {
        public static void Write(string outputPath, GeoSkylinesLayer layer)
        {
            List<string> columns = new List<string>();
            columns.Add("Id");
            columns.Add("Geometry");

            foreach (GeoSkylinesFeature feature in layer.Features)
            {
                foreach (string key in feature.Attributes.Keys.OrderBy(delegate(string item) { return item; }, StringComparer.OrdinalIgnoreCase))
                {
                    if (!columns.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        columns.Add(key);
                    }
                }
            }

            using (StreamWriter writer = new StreamWriter(outputPath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join(",", columns.Select(EscapeCsv).ToArray()));
                foreach (GeoSkylinesFeature feature in layer.Features)
                {
                    List<string> row = new List<string>();
                    row.Add(feature.FeatureId);
                    row.Add(ToWkt(feature.Geometry));
                    foreach (string column in columns.Skip(2))
                    {
                        string value;
                        feature.Attributes.TryGetValue(column, out value);
                        row.Add(value ?? string.Empty);
                    }

                    writer.WriteLine(string.Join(",", row.Select(EscapeCsv).ToArray()));
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            bool requiresQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            if (!requiresQuotes)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string ToWkt(GeoSkylinesGeometry geometry)
        {
            if (geometry == null)
            {
                return string.Empty;
            }

            if (geometry.GeometryType == "Point")
            {
                if (geometry.Parts.Count == 0 || geometry.Parts[0].Count == 0)
                {
                    return string.Empty;
                }

                GeoSkylinesCoordinate point = geometry.Parts[0][0];
                return string.Format(CultureInfo.InvariantCulture, "POINT ({0} {1})", point.Longitude, point.Latitude);
            }

            if (geometry.GeometryType == "LineString")
            {
                if (geometry.Parts.Count == 0 || geometry.Parts[0].Count == 0)
                {
                    return string.Empty;
                }

                return "LINESTRING (" + JoinCoordinates(geometry.Parts[0]) + ")";
            }

            if (geometry.GeometryType == "MultiLineString")
            {
                if (geometry.Parts.Count == 0)
                {
                    return string.Empty;
                }

                return "MULTILINESTRING (" + string.Join(", ", geometry.Parts.Select(delegate(List<GeoSkylinesCoordinate> part)
                {
                    return "(" + JoinCoordinates(part) + ")";
                }).ToArray()) + ")";
            }

            if (geometry.GeometryType == "Polygon")
            {
                if (geometry.Parts.Count == 0)
                {
                    return string.Empty;
                }

                return "POLYGON (" + string.Join(", ", geometry.Parts.Select(delegate(List<GeoSkylinesCoordinate> ring)
                {
                    return "(" + JoinCoordinates(ring) + ")";
                }).ToArray()) + ")";
            }

            if (geometry.GeometryType == "MultiPolygon")
            {
                if (geometry.Polygons == null || geometry.Polygons.Count == 0)
                {
                    return string.Empty;
                }

                return "MULTIPOLYGON (" + string.Join(", ", geometry.Polygons.Select(delegate(List<List<GeoSkylinesCoordinate>> polygon)
                {
                    return "(" + string.Join(", ", polygon.Select(delegate(List<GeoSkylinesCoordinate> ring)
                    {
                        return "(" + JoinCoordinates(ring) + ")";
                    }).ToArray()) + ")";
                }).ToArray()) + ")";
            }

            return string.Empty;
        }

        private static string JoinCoordinates(IEnumerable<GeoSkylinesCoordinate> coordinates)
        {
            return string.Join(", ", coordinates.Select(delegate(GeoSkylinesCoordinate coordinate)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} {1}", coordinate.Longitude, coordinate.Latitude);
            }).ToArray());
        }
    }

    public static class GeoSkylinesGeoJsonWriter
    {
        public static void Write(string outputPath, GeoSkylinesLayer layer)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"type\":\"FeatureCollection\",\"name\":");
            WriteJsonString(builder, layer.Name);
            builder.Append(",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"EPSG:4326\"}},\"features\":[");

            for (int i = 0; i < layer.Features.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                WriteFeature(builder, layer.Features[i]);
            }

            builder.Append("]}");
            File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
        }

        private static void WriteFeature(StringBuilder builder, GeoSkylinesFeature feature)
        {
            builder.Append("{\"type\":\"Feature\",\"id\":");
            WriteJsonString(builder, feature.FeatureId);
            builder.Append(",\"geometry\":");
            WriteGeometry(builder, feature.Geometry);
            builder.Append(",\"properties\":{");

            bool first = true;
            foreach (KeyValuePair<string, string> attribute in feature.Attributes.OrderBy(delegate(KeyValuePair<string, string> item) { return item.Key; }, StringComparer.OrdinalIgnoreCase))
            {
                if (!first)
                {
                    builder.Append(",");
                }

                WriteJsonString(builder, attribute.Key);
                builder.Append(":");
                WriteJsonValue(builder, attribute.Value);
                first = false;
            }

            builder.Append("}}");
        }

        private static void WriteGeometry(StringBuilder builder, GeoSkylinesGeometry geometry)
        {
            builder.Append("{\"type\":");
            WriteJsonString(builder, geometry.GeometryType);
            builder.Append(",\"coordinates\":");

            if (geometry.GeometryType == "Point")
            {
                WriteCoordinate(builder, geometry.Parts[0][0]);
            }
            else if (geometry.GeometryType == "LineString")
            {
                WriteCoordinateArray(builder, geometry.Parts[0]);
            }
            else if (geometry.GeometryType == "MultiLineString")
            {
                builder.Append("[");
                for (int i = 0; i < geometry.Parts.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(",");
                    }

                    WriteCoordinateArray(builder, geometry.Parts[i]);
                }

                builder.Append("]");
            }
            else if (geometry.GeometryType == "Polygon")
            {
                builder.Append("[");
                for (int i = 0; i < geometry.Parts.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(",");
                    }

                    WriteCoordinateArray(builder, geometry.Parts[i]);
                }
                builder.Append("]");
            }
            else if (geometry.GeometryType == "MultiPolygon")
            {
                builder.Append("[");
                for (int polygonIndex = 0; polygonIndex < geometry.Polygons.Count; polygonIndex++)
                {
                    if (polygonIndex > 0)
                    {
                        builder.Append(",");
                    }

                    builder.Append("[");
                    List<List<GeoSkylinesCoordinate>> polygon = geometry.Polygons[polygonIndex];
                    for (int ringIndex = 0; ringIndex < polygon.Count; ringIndex++)
                    {
                        if (ringIndex > 0)
                        {
                            builder.Append(",");
                        }

                        WriteCoordinateArray(builder, polygon[ringIndex]);
                    }
                    builder.Append("]");
                }
                builder.Append("]");
            }
            else
            {
                builder.Append("null");
            }

            builder.Append("}");
        }

        private static void WriteCoordinateArray(StringBuilder builder, IList<GeoSkylinesCoordinate> coordinates)
        {
            builder.Append("[");
            for (int i = 0; i < coordinates.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                WriteCoordinate(builder, coordinates[i]);
            }
            builder.Append("]");
        }

        private static void WriteCoordinate(StringBuilder builder, GeoSkylinesCoordinate coordinate)
        {
            builder.Append("[");
            builder.Append(coordinate.Longitude.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",");
            builder.Append(coordinate.Latitude.ToString("R", CultureInfo.InvariantCulture));
            builder.Append("]");
        }

        private static void WriteJsonValue(StringBuilder builder, string value)
        {
            bool boolValue;
            if (bool.TryParse(value, out boolValue))
            {
                builder.Append(boolValue ? "true" : "false");
                return;
            }

            long longValue;
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue))
            {
                builder.Append(longValue.ToString(CultureInfo.InvariantCulture));
                return;
            }

            double doubleValue;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                builder.Append(doubleValue.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            WriteJsonString(builder, value);
        }

        private static void WriteJsonString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append("\"");
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }
            builder.Append("\"");
        }
    }

    public static class GeoSkylinesManifestWriter
    {
        public static void Write(string outputPath, GeoSkylinesManifest manifest)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{");
            AppendProperty(builder, "generated_at_utc", manifest.GeneratedAtUtc.ToString("o", CultureInfo.InvariantCulture), true);
            AppendProperty(builder, "map_name", manifest.MapName, false);
            AppendProperty(builder, "output_directory", manifest.OutputDirectory, false);
            AppendProperty(builder, "config_path", manifest.ConfigPath, false);
            AppendProperty(builder, "cli_path", manifest.CliPath, false);
            AppendProperty(builder, "cli_exit_code", manifest.CliExitCode, false);
            AppendProperty(builder, "cli_output", manifest.CliOutput, false);
            builder.Append(",\"formats\":");
            AppendArray(builder, manifest.Formats);
            builder.Append(",\"warnings\":");
            AppendArray(builder, manifest.Warnings);
            builder.Append(",\"layers\":[");
            for (int i = 0; i < manifest.Layers.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                GeoSkylinesLayer layer = manifest.Layers[i];
                builder.Append("{");
                AppendProperty(builder, "name", layer.Name, true);
                AppendProperty(builder, "source_system", layer.SourceSystem, false);
                builder.Append(",\"experimental\":");
                builder.Append(layer.Experimental ? "true" : "false");
                builder.Append(",\"feature_count\":");
                builder.Append(layer.Features.Count.ToString(CultureInfo.InvariantCulture));
                builder.Append(",\"files\":");
                AppendArray(builder, layer.Files);
                builder.Append(",\"warnings\":");
                AppendArray(builder, layer.Warnings);
                builder.Append("}");
            }
            builder.Append("]}");

            File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
        }

        private static void AppendProperty(StringBuilder builder, string key, string value, bool first)
        {
            if (!first)
            {
                builder.Append(",");
            }

            builder.Append("\"");
            builder.Append(key);
            builder.Append("\":");
            if (value == null)
            {
                builder.Append("null");
            }
            else
            {
                AppendJsonString(builder, value);
            }
        }

        private static void AppendArray(StringBuilder builder, IEnumerable<string> values)
        {
            builder.Append("[");
            bool first = true;
            foreach (string value in values)
            {
                if (!first)
                {
                    builder.Append(",");
                }

                AppendJsonString(builder, value ?? string.Empty);
                first = false;
            }
            builder.Append("]");
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append("\"");
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }
            builder.Append("\"");
        }
    }
}
