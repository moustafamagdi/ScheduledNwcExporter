using System;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    public class NwcExporterService
    {
        private readonly ILogger _logger;

        public NwcExporterService(ILogger logger)
        {
            _logger = logger;
        }

        public bool IsExporterAvailable()
        {
            try
            {
                bool available = OptionalFunctionalityUtils.IsNavisworksExporterAvailable();
                _logger.Info("Exporter", $"Navisworks Exporter availability check: {(available ? "Available" : "Not Available")}");
                return available;
            }
            catch (Exception ex)
            {
                _logger.Warning("Exporter", $"Error checking Navisworks Exporter availability: {ex.Message}", "", "", ex);
                return false;
            }
        }

        public bool ExportModelToNwc(Document doc, string outputDirectory, string outputFileName, ExportSettings settings, string modelName)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                string fullOutputPath = Path.Combine(outputDirectory, outputFileName);

                // Handle overwrite policy
                if (File.Exists(fullOutputPath))
                {
                    if (settings.OverwritePolicy == "Skip")
                    {
                        _logger.Info("Export", $"Output file already exists and overwrite policy is 'Skip'. Skipping export for {modelName}.", modelName, "Exporting");
                        return true;
                    }
                    else if (settings.OverwritePolicy == "TimestampedCopy")
                    {
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string ext = Path.GetExtension(outputFileName);
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(outputFileName);
                        outputFileName = $"{nameWithoutExt}_{timestamp}{ext}";
                        fullOutputPath = Path.Combine(outputDirectory, outputFileName);
                        _logger.Info("Export", $"Output file exists. Timestamped copy policy applied: {outputFileName}", modelName, "Exporting");
                    }
                    else
                    {
                        // Overwrite
                        _logger.Info("Export", $"Overwriting existing output file: {fullOutputPath}", modelName, "Exporting");
                    }
                }

                _logger.Info("Export", $"Configuring Navisworks export options (ExportLinks = {settings.ExportLinks}, Scope = {settings.ExportScope}, Coordinates = {settings.Coordinates})", modelName, "Exporting");

                NavisworksExportOptions exportOptions = new NavisworksExportOptions
                {
                    ExportLinks = settings.ExportLinks,
                    ExportElementIds = settings.ExportElementIds,
                    ExportRoomGeometry = settings.ExportRoomGeometry,
                    ExportScope = NavisworksExportScope.Model
                };

                // Set coordinates
                if (settings.Coordinates == "Shared")
                {
                    exportOptions.Coordinates = NavisworksCoordinates.Shared;
                }
                else
                {
                    exportOptions.Coordinates = NavisworksCoordinates.Internal;
                }

                string exportFolder = outputDirectory;
                string exportName = Path.GetFileNameWithoutExtension(outputFileName);

                _logger.Info("Export", $"Starting NWC export to folder '{exportFolder}' with filename '{exportName}.nwc'", modelName, "Exporting");

                bool success = doc.Export(exportFolder, exportName, exportOptions);

                if (success && File.Exists(fullOutputPath))
                {
                    FileInfo fi = new FileInfo(fullOutputPath);
                    _logger.Success("Export", $"NWC file successfully created. Size: {fi.Length / (1024 * 1024)} MB, Path: {fullOutputPath}", modelName, "VerifyingOutput");
                    return true;
                }
                else
                {
                    _logger.Error("Export", $"NWC export returned failure or output file not found at {fullOutputPath}", modelName, "VerifyingOutput");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Export", $"Exception during NWC export: {ex.Message}", modelName, "Exporting", ex);
                return false;
            }
        }
    }
}
