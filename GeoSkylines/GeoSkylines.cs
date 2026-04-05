using ColossalFramework.UI;
using ICities;
using System.Globalization;
using System.Linq;

namespace GeoSkylines
{
    public class GeoSkylines : IUserMod
    {
        public string Name
        {
            get { return "GeoSkylines"; }
        }

        public string Description
        {
            get { return "Import/export geodata for Cities: Skylines with CSV and GeoJSON output plus optional CLI conversion."; }
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            GeoSkylinesConfig config = GeoSkylinesConfig.Load();
            UIHelperBase geoGroup = helper.AddGroup("Geo Reference");
            UIHelperBase exportGroup = helper.AddGroup("Export Settings");

            string mapName = config.GetValue("MapName", "GeoSkylines");
            string centerLatitude = config.GetValue("CenterLatitude", "0");
            string centerLongitude = config.GetValue("CenterLongitude", "0");
            string outputDirectory = config.GetValue(GeoSkylinesConfig.ExportOutputDirectoryKey, config.GetOutputDirectory());
            string[] selectedFormats = config.GetExportFormats();
            string[] selectedLayers = config.GetExportLayers();
            string cliPath = config.GetValue(GeoSkylinesConfig.ExportCliPathKey, string.Empty);
            bool runCli = config.GetBool(GeoSkylinesConfig.ExportRunCliKey, false);
            bool extendedAttributes = config.GetBool(GeoSkylinesConfig.ExportIncludeExtendedAttributesKey, false);

            geoGroup.AddTextfield("Map name", mapName, delegate(string value) { mapName = value; }, delegate(string value) { mapName = value; });
            geoGroup.AddTextfield("Center latitude", centerLatitude, delegate(string value) { centerLatitude = value; }, delegate(string value) { centerLatitude = value; });
            geoGroup.AddTextfield("Center longitude", centerLongitude, delegate(string value) { centerLongitude = value; }, delegate(string value) { centerLongitude = value; });
            exportGroup.AddTextfield("Output directory", outputDirectory, delegate(string value) { outputDirectory = value; }, delegate(string value) { outputDirectory = value; });
            UIHelperBase formatsGroup = exportGroup.AddGroup("Formats");
            bool[] formatSelections = GeoSkylinesLayerCatalog.AvailableFormats
                .Select(delegate(string format) { return selectedFormats.Contains(format, System.StringComparer.OrdinalIgnoreCase); })
                .ToArray();
            for (int i = 0; i < GeoSkylinesLayerCatalog.AvailableFormats.Length; i++)
            {
                int formatIndex = i;
                formatsGroup.AddCheckbox(
                    GeoSkylinesLayerCatalog.AvailableFormats[formatIndex],
                    formatSelections[formatIndex],
                    delegate(bool value) { formatSelections[formatIndex] = value; });
            }
            bool[] layerSelections = GeoSkylinesLayerCatalog.AllLayers
                .Select(delegate(string layer) { return selectedLayers.Contains(layer, System.StringComparer.OrdinalIgnoreCase); })
                .ToArray();
            foreach (GeoSkylinesLayerGroup layerGroup in GeoSkylinesLayerCatalog.LayerGroups)
            {
                UIHelperBase layersGroup = exportGroup.AddGroup(layerGroup.Name);
                for (int i = 0; i < layerGroup.Layers.Length; i++)
                {
                    string layerName = layerGroup.Layers[i];
                    int checkboxIndex = System.Array.IndexOf(GeoSkylinesLayerCatalog.AllLayers, layerName);
                    if (checkboxIndex < 0)
                    {
                        continue;
                    }

                    layersGroup.AddCheckbox(layerName, layerSelections[checkboxIndex], delegate(bool value) { layerSelections[checkboxIndex] = value; });
                }
            }
            exportGroup.AddTextfield("CLI path", cliPath, delegate(string value) { cliPath = value; }, delegate(string value) { cliPath = value; });
            exportGroup.AddCheckbox("Run CLI after export", runCli, delegate(bool value) { runCli = value; });
            exportGroup.AddCheckbox("Include extended attributes", extendedAttributes, delegate(bool value) { extendedAttributes = value; });
            exportGroup.AddButton("Save export settings", delegate()
            {
                string[] formats = GeoSkylinesLayerCatalog.AvailableFormats
                    .Where(delegate(string format, int index) { return formatSelections[index]; })
                    .ToArray();
                if (formats.Length == 0)
                {
                    formats = GeoSkylinesConfig.DefaultFormats;
                }

                string layers = string.Join(",", GeoSkylinesLayerCatalog.AllLayers.Where(delegate(string layer, int index) { return layerSelections[index]; }).ToArray());
                config.SetValue(GeoSkylinesConfig.ExportOutputDirectoryKey, outputDirectory);
                config.SetValue(GeoSkylinesConfig.ExportFormatsKey, string.Join(",", formats));
                config.SetValue(GeoSkylinesConfig.ExportLayersKey, layers);
                config.SetValue(GeoSkylinesConfig.ExportCliPathKey, cliPath);
                config.SetValue(GeoSkylinesConfig.ExportRunCliKey, runCli.ToString().ToLowerInvariant());
                config.SetValue(GeoSkylinesConfig.ExportIncludeExtendedAttributesKey, extendedAttributes.ToString().ToLowerInvariant());
                config.SetValue("MapName", mapName);
                config.SetValue("CenterLatitude", centerLatitude);
                config.SetValue("CenterLongitude", centerLongitude);
                config.Save();

                double parsedLatitude;
                double parsedLongitude;
                if (double.TryParse(centerLatitude, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedLatitude)
                    && double.TryParse(centerLongitude, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedLongitude))
                {
                    GeoSkylinesSaveState.SetCenterState(mapName, parsedLatitude, parsedLongitude);
                }

                ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                panel.SetMessage("GeoSkylines", "Settings saved to " + config.ConfigPath + "\nCenter will also be stored in the current save.", false);
            });
            exportGroup.AddButton("Run batch export", delegate()
            {
                GeoSkylinesExport export = new GeoSkylinesExport();
                export.BatchExportConfiguredLayers();
            });
        }
    }
}
