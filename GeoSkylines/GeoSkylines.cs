using ColossalFramework.UI;
using ICities;

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
            UIHelperBase exportGroup = helper.AddGroup("Export Settings");

            string outputDirectory = config.GetValue(GeoSkylinesConfig.ExportOutputDirectoryKey, config.GetOutputDirectory());
            string formats = string.Join(",", config.GetExportFormats());
            string layers = string.Join(",", config.GetExportLayers());
            string cliPath = config.GetValue(GeoSkylinesConfig.ExportCliPathKey, string.Empty);
            bool runCli = config.GetBool(GeoSkylinesConfig.ExportRunCliKey, false);
            bool extendedAttributes = config.GetBool(GeoSkylinesConfig.ExportIncludeExtendedAttributesKey, false);

            exportGroup.AddTextfield("Output directory", outputDirectory, delegate(string value) { outputDirectory = value; }, delegate(string value) { outputDirectory = value; });
            exportGroup.AddTextfield("Formats", formats, delegate(string value) { formats = value; }, delegate(string value) { formats = value; });
            exportGroup.AddTextfield("Layers", layers, delegate(string value) { layers = value; }, delegate(string value) { layers = value; });
            exportGroup.AddTextfield("CLI path", cliPath, delegate(string value) { cliPath = value; }, delegate(string value) { cliPath = value; });
            exportGroup.AddCheckbox("Run CLI after export", runCli, delegate(bool value) { runCli = value; });
            exportGroup.AddCheckbox("Include extended attributes", extendedAttributes, delegate(bool value) { extendedAttributes = value; });
            exportGroup.AddButton("Save export settings", delegate()
            {
                config.SetValue(GeoSkylinesConfig.ExportOutputDirectoryKey, outputDirectory);
                config.SetValue(GeoSkylinesConfig.ExportFormatsKey, formats);
                config.SetValue(GeoSkylinesConfig.ExportLayersKey, layers);
                config.SetValue(GeoSkylinesConfig.ExportCliPathKey, cliPath);
                config.SetValue(GeoSkylinesConfig.ExportRunCliKey, runCli.ToString().ToLowerInvariant());
                config.SetValue(GeoSkylinesConfig.ExportIncludeExtendedAttributesKey, extendedAttributes.ToString().ToLowerInvariant());
                config.Save();

                ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                panel.SetMessage("GeoSkylines", "Export settings saved to " + config.ConfigPath, false);
            });
            exportGroup.AddButton("Run batch export", delegate()
            {
                GeoSkylinesExport export = new GeoSkylinesExport();
                export.BatchExportConfiguredLayers();
            });
        }
    }
}
