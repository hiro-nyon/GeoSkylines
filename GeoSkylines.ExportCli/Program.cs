using System.Text.Json;
using GeoParquet;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.Esri;
using ParquetSharp;

namespace GeoSkylines.ExportCli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            CliOptions options = CliOptions.Parse(args);
            if (!Directory.Exists(options.InputDirectory))
            {
                Console.Error.WriteLine($"Input directory does not exist: {options.InputDirectory}");
                return 2;
            }

            string[] geoJsonFiles = Directory.GetFiles(options.InputDirectory, "*.geojson", SearchOption.TopDirectoryOnly);
            if (geoJsonFiles.Length == 0)
            {
                Console.WriteLine("No GeoJSON files found.");
                return 0;
            }

            foreach (string geoJsonFile in geoJsonFiles)
            {
                FeatureCollection collection = ReadFeatures(geoJsonFile);
                if (options.Formats.Contains("shp", StringComparer.OrdinalIgnoreCase))
                {
                    WriteShapefile(geoJsonFile, collection);
                }

                if (options.Formats.Contains("parquet", StringComparer.OrdinalIgnoreCase))
                {
                    WriteGeoParquet(geoJsonFile, collection);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static FeatureCollection ReadFeatures(string geoJsonFile)
    {
        string json = File.ReadAllText(geoJsonFile);
        GeoJsonReader reader = new GeoJsonReader();
        FeatureCollection collection = reader.Read<FeatureCollection>(json);
        return collection ?? new FeatureCollection();
    }

    private static void WriteShapefile(string geoJsonFile, FeatureCollection collection)
    {
        if (collection.Count == 0)
        {
            return;
        }

        string shpPath = Path.ChangeExtension(geoJsonFile, ".shp");
        Dictionary<string, string> fieldMap = BuildFieldMap(collection);
        var features = new List<Feature>(collection.Count);
        foreach (IFeature feature in collection)
        {
            AttributesTable attributes = new AttributesTable();
            foreach (string originalName in fieldMap.Keys)
            {
                object value = feature.Attributes.Exists(originalName) ? feature.Attributes[originalName] : null;
                attributes.Add(fieldMap[originalName], value);
            }

            features.Add(new Feature(feature.Geometry, attributes));
        }

        Shapefile.WriteAllFeatures(features, shpPath);
        File.WriteAllText(Path.ChangeExtension(geoJsonFile, ".fieldmap.json"), JsonSerializer.Serialize(fieldMap, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void WriteGeoParquet(string geoJsonFile, FeatureCollection collection)
    {
        if (collection.Count == 0)
        {
            return;
        }

        string parquetPath = Path.ChangeExtension(geoJsonFile, ".parquet");
        string[] propertyNames = collection
            .SelectMany(feature => feature.Attributes.GetNames())
            .Where(name => !string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var columns = new Column[propertyNames.Length + 2];
        columns[0] = new Column<string>("id");
        columns[1] = new Column<byte[]>("geometry");
        for (int i = 0; i < propertyNames.Length; i++)
        {
            columns[i + 2] = new Column<string>(propertyNames[i]);
        }

        Envelope envelope = new Envelope();
        HashSet<string> geometryTypes = new(StringComparer.OrdinalIgnoreCase);
        WKBWriter wkbWriter = new();

        string[] ids = new string[collection.Count];
        byte[][] geometries = new byte[collection.Count][];
        Dictionary<string, string[]> propertyValues = propertyNames.ToDictionary(name => name, _ => new string[collection.Count], StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < collection.Count; i++)
        {
            IFeature feature = collection[i];
            ids[i] = feature.Attributes.Exists("id") ? feature.Attributes["id"]?.ToString() ?? i.ToString() : i.ToString();
            geometries[i] = wkbWriter.Write(feature.Geometry);
            envelope.ExpandToInclude(feature.Geometry.EnvelopeInternal);
            geometryTypes.Add(feature.Geometry.GeometryType);
            foreach (string propertyName in propertyNames)
            {
                propertyValues[propertyName][i] = feature.Attributes.Exists(propertyName)
                    ? feature.Attributes[propertyName]?.ToString()
                    : null;
            }
        }

        GeoColumn geoColumn = new GeoColumn();
        geoColumn.Encoding = "WKB";
        geoColumn.Bbox = new[] { envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY };
        foreach (string geometryType in geometryTypes)
        {
            geoColumn.Geometry_types.Add(geometryType);
        }

        using ParquetFileWriter writer = new(parquetPath, columns, keyValueMetadata: GeoMetadata.GetGeoMetadata(geoColumn));
        using RowGroupWriter rowGroup = writer.AppendRowGroup();
        rowGroup.NextColumn().LogicalWriter<string>().WriteBatch(ids);
        rowGroup.NextColumn().LogicalWriter<byte[]>().WriteBatch(geometries);
        foreach (string propertyName in propertyNames)
        {
            rowGroup.NextColumn().LogicalWriter<string>().WriteBatch(propertyValues[propertyName]);
        }

        writer.Close();
    }

    private static Dictionary<string, string> BuildFieldMap(FeatureCollection collection)
    {
        Dictionary<string, string> fieldMap = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedShortNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string originalName in collection.SelectMany(feature => feature.Attributes.GetNames()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string sanitized = new string(originalName.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "field";
            }

            if (char.IsDigit(sanitized[0]))
            {
                sanitized = "f" + sanitized;
            }

            sanitized = sanitized[..Math.Min(10, sanitized.Length)];
            string candidate = sanitized;
            int suffix = 1;
            while (!usedShortNames.Add(candidate))
            {
                string suffixText = suffix.ToString();
                int baseLength = Math.Max(1, 10 - suffixText.Length);
                candidate = sanitized[..Math.Min(baseLength, sanitized.Length)] + suffixText;
                suffix += 1;
            }

            fieldMap[originalName] = candidate;
        }

        return fieldMap;
    }

    private sealed class CliOptions
    {
        public string InputDirectory { get; private set; } = Directory.GetCurrentDirectory();
        public string[] Formats { get; private set; } = ["shp", "parquet"];

        public static CliOptions Parse(string[] args)
        {
            CliOptions options = new();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--input-dir":
                        if (i + 1 < args.Length)
                        {
                            options.InputDirectory = Path.GetFullPath(args[++i]);
                        }
                        break;
                    case "--formats":
                        if (i + 1 < args.Length)
                        {
                            options.Formats = args[++i]
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        }
                        break;
                }
            }

            return options;
        }
    }
}
