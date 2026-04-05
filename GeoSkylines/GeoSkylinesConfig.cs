using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GeoSkylines
{
    public sealed class GeoSkylinesConfig
    {
        public const string ExportOutputDirectoryKey = "ExportOutputDirectory";
        public const string ExportFormatsKey = "ExportFormats";
        public const string ExportLayersKey = "ExportLayers";
        public const string ExportCliPathKey = "ExportCliPath";
        public const string ExportRunCliKey = "ExportRunCli";
        public const string ExportIncludeExtendedAttributesKey = "ExportIncludeExtendedAttributes";

        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string ConfigPath { get; private set; }

        public static readonly string[] DefaultFormats = new string[] { "csv", "geojson" };
        public static readonly string[] DefaultLayers = GeoSkylinesLayerCatalog.AllLayers;

        private GeoSkylinesConfig()
        {
        }

        public static GeoSkylinesConfig Load()
        {
            GeoSkylinesConfig config = new GeoSkylinesConfig();
            config.ConfigPath = ResolveConfigPath();

            if (File.Exists(config.ConfigPath))
            {
                using (StreamReader confSr = File.OpenText(config.ConfigPath))
                {
                    while (!confSr.EndOfStream)
                    {
                        string line = confSr.ReadLine();
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        int separator = line.IndexOf(':');
                        if (separator <= 0)
                        {
                            continue;
                        }

                        string key = line.Substring(0, separator).Trim();
                        string value = line.Substring(separator + 1).Trim();
                        if (!config.values.ContainsKey(key))
                        {
                            config.values.Add(key, value);
                        }
                        else
                        {
                            config.values[key] = value;
                        }
                    }
                }
            }

            config.ApplyDefaults();
            config.ApplySaveOverrides();
            config.ApplyCurrentCenterState();
            return config;
        }

        public static string ResolveConfigPath()
        {
            string filesDirectory = GetFilesDirectory();
            string confPath = Path.Combine(filesDirectory, "import_export.conf");
            if (File.Exists(confPath))
            {
                return confPath;
            }

            string txtPath = Path.Combine(filesDirectory, "import_export.txt");
            if (File.Exists(txtPath))
            {
                return txtPath;
            }

            return confPath;
        }

        public static string GetFilesDirectory()
        {
            List<string> candidates = GetFilesDirectoryCandidates();
            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            if (candidates.Count > 0)
            {
                return candidates[0];
            }

            return Path.GetFullPath("Files");
        }

        private static List<string> GetFilesDirectoryCandidates()
        {
            List<string> candidates = new List<string>();

            AddCandidate(candidates, Environment.GetEnvironmentVariable("GEOSKYLINES_FILES_DIR"));
            AddCandidate(candidates, Path.Combine(Directory.GetCurrentDirectory(), "Files"));
            AddCandidate(candidates, Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? string.Empty, "Files"));

            string dataPath = string.Empty;
            try
            {
                dataPath = Application.dataPath;
            }
            catch
            {
                dataPath = string.Empty;
            }

            if (!string.IsNullOrEmpty(dataPath))
            {
                AddCandidate(candidates, Path.Combine(Path.Combine(dataPath, "Resources"), "Files"));
                AddCandidate(candidates, Path.Combine(dataPath, "Files"));

                string dataParent = Path.GetDirectoryName(dataPath);
                if (!string.IsNullOrEmpty(dataParent))
                {
                    AddCandidate(candidates, Path.Combine(dataParent, "Files"));
                    AddCandidate(candidates, Path.Combine(Path.Combine(dataParent, "Resources"), "Files"));

                    string appParent = Path.GetDirectoryName(dataParent);
                    if (!string.IsNullOrEmpty(appParent))
                    {
                        AddCandidate(candidates, Path.Combine(Path.Combine(appParent, "Resources"), "Files"));
                    }
                }
            }

            string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            if (!string.IsNullOrEmpty(homeDirectory))
            {
                AddCandidate(candidates, Path.Combine(homeDirectory, "Library/Application Support/Steam/steamapps/common/Cities_Skylines/Cities.app/Contents/Resources/Files"));
                AddCandidate(candidates, Path.Combine(homeDirectory, "Library/Application Support/Steam/steamapps/common/Cities_Skylines/Files"));
            }

            AddCandidate(candidates, @"C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Files");

            return candidates;
        }

        private static void AddCandidate(List<string> candidates, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            if (!candidates.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalizedPath);
            }
        }

        public string GetValue(string key, string defaultValue)
        {
            string value;
            if (values.TryGetValue(key, out value))
            {
                return value;
            }

            return defaultValue;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            string rawValue = GetValue(key, null);
            if (string.IsNullOrEmpty(rawValue))
            {
                return defaultValue;
            }

            bool parsedBool;
            if (bool.TryParse(rawValue, out parsedBool))
            {
                return parsedBool;
            }

            if (rawValue == "1")
            {
                return true;
            }

            if (rawValue == "0")
            {
                return false;
            }

            return defaultValue;
        }

        public double GetDouble(string key, double defaultValue)
        {
            string rawValue = GetValue(key, null);
            if (string.IsNullOrEmpty(rawValue))
            {
                return defaultValue;
            }

            double parsed;
            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            if (double.TryParse(rawValue, out parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        public string[] GetList(string key, IEnumerable<string> defaultValues)
        {
            string rawValue = GetValue(key, null);
            if (string.IsNullOrEmpty(rawValue))
            {
                return defaultValues.ToArray();
            }

            return rawValue.Split(',')
                .Select(delegate(string part) { return part.Trim(); })
                .Where(delegate(string part) { return !string.IsNullOrEmpty(part); })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void SetValue(string key, string value)
        {
            if (values.ContainsKey(key))
            {
                values[key] = value;
            }
            else
            {
                values.Add(key, value);
            }
        }

        public void Save()
        {
            string directory = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string[] orderedKeys = values.Keys.OrderBy(delegate(string key) { return key; }, StringComparer.OrdinalIgnoreCase).ToArray();
            using (StreamWriter writer = new StreamWriter(ConfigPath, false))
            {
                foreach (string key in orderedKeys)
                {
                    writer.WriteLine("{0}: {1}", key, values[key]);
                }
            }

            if (ConfigPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                string confPath = Path.Combine(GetFilesDirectory(), "import_export.conf");
                ConfigPath = confPath;
                Save();
            }
        }

        public string GetOutputDirectory()
        {
            string configured = GetValue(ExportOutputDirectoryKey, null);
            if (string.IsNullOrEmpty(configured))
            {
                configured = Path.Combine(GetFilesDirectory(), "Exports");
            }

            if (!Path.IsPathRooted(configured))
            {
                configured = Path.GetFullPath(configured);
            }

            return configured;
        }

        public string[] GetExportFormats()
        {
            return GetList(ExportFormatsKey, DefaultFormats)
                .Select(delegate(string format) { return format.ToLowerInvariant(); })
                .ToArray();
        }

        public string[] GetExportLayers()
        {
            return GetList(ExportLayersKey, DefaultLayers)
                .Select(delegate(string layer) { return layer.ToLowerInvariant(); })
                .SelectMany(ExpandLegacyLayerNames)
                .Where(delegate(string layer) { return GeoSkylinesLayerCatalog.KnownLayers.Contains(layer); })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IEnumerable<string> ExpandLegacyLayerNames(string layer)
        {
            if (layer == "outside_connections")
            {
                return new string[] { "outside_connection_nodes" };
            }

            return new string[] { layer };
        }

        private void ApplyDefaults()
        {
            if (!values.ContainsKey(ExportOutputDirectoryKey))
            {
                values.Add(ExportOutputDirectoryKey, Path.Combine(GetFilesDirectory(), "Exports"));
            }

            if (!values.ContainsKey(ExportFormatsKey))
            {
                values.Add(ExportFormatsKey, string.Join(",", DefaultFormats));
            }

            if (!values.ContainsKey(ExportLayersKey))
            {
                values.Add(ExportLayersKey, string.Join(",", DefaultLayers));
            }

            if (!values.ContainsKey(ExportCliPathKey))
            {
                values.Add(ExportCliPathKey, string.Empty);
            }

            if (!values.ContainsKey(ExportRunCliKey))
            {
                values.Add(ExportRunCliKey, "false");
            }

            if (!values.ContainsKey(ExportIncludeExtendedAttributesKey))
            {
                values.Add(ExportIncludeExtendedAttributesKey, "false");
            }
        }

        private void ApplySaveOverrides()
        {
            GeoSkylinesCenterState centerState;
            if (!GeoSkylinesSaveState.TryGetCenterState(out centerState))
            {
                return;
            }

            if (!string.IsNullOrEmpty(centerState.MapName))
            {
                SetValue("MapName", centerState.MapName);
            }

            SetValue("CenterLatitude", centerState.CenterLatitude.ToString("R", CultureInfo.InvariantCulture));
            SetValue("CenterLongitude", centerState.CenterLongitude.ToString("R", CultureInfo.InvariantCulture));
        }

        private void ApplyCurrentCenterState()
        {
            double centerLatitude;
            double centerLongitude;
            if (!double.TryParse(GetValue("CenterLatitude", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out centerLatitude))
            {
                return;
            }

            if (!double.TryParse(GetValue("CenterLongitude", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out centerLongitude))
            {
                return;
            }

            GeoSkylinesSaveState.SetCenterState(GetValue("MapName", "GeoSkylines"), centerLatitude, centerLongitude);
        }
    }
}
