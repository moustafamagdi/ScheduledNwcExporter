using System;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    /// <summary>
    /// Performs NWC exports through Revit's optional Navisworks exporter.
    /// </summary>
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
                _logger.Warning("Exporter", $"Error checking Navisworks Exporter availability: {ex.Message}", string.Empty, string.Empty, ex);
                return false;
            }
        }

        /// <summary>
        /// Exports the supplied document to NWC and verifies that a non-empty output file was produced.
        /// In Revit 2024, Document.Export with NavisworksExportOptions returns void rather than a status value.
        /// </summary>
        public bool ExportModelToNwc(Document doc, string outputDirectory, string outputFileName, ExportSettings settings, string modelName)
        {
            try
            {
                if (doc == null) throw new ArgumentNullException(nameof(doc));
                if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
                if (string.IsNullOrWhiteSpace(outputFileName)) throw new ArgumentException("An output file name is required.", nameof(outputFileName));

                Directory.CreateDirectory(outputDirectory);
                string fullOutputPath = Path.Combine(outputDirectory, outputFileName);

                if (File.Exists(fullOutputPath))
                {
                    if (string.Equals(settings.OverwritePolicy, "Skip", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Info("Export", "Output file already exists and overwrite policy is Skip. Export will not run.", modelName, "Exporting");
                        return true;
                    }

                    if (string.Equals(settings.OverwritePolicy, "TimestampedCopy", StringComparison.OrdinalIgnoreCase))
                    {
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string extension = Path.GetExtension(outputFileName);
                        string filenameWithoutExtension = Path.GetFileNameWithoutExtension(outputFileName);
                        outputFileName = $"{filenameWithoutExtension}_{timestamp}{extension}";
                        fullOutputPath = Path.Combine(outputDirectory, outputFileName);
                        _logger.Info("Export", $"Output file exists. Timestamped-copy policy selected: {outputFileName}", modelName, "Exporting");
                    }
                    else
                    {
                        _logger.Info("Export", $"Existing output will be overwritten: {fullOutputPath}", modelName, "Exporting");
                    }
                }

                var exportOptions = new NavisworksExportOptions
                {
                    ExportLinks = settings.ExportLinks,
                    ExportElementIds = settings.ExportElementIds,
                    ExportRoomGeometry = settings.ExportRoomGeometry,
                    ExportScope = NavisworksExportScope.Model,
                    Coordinates = string.Equals(settings.Coordinates, "Shared", StringComparison.OrdinalIgnoreCase)
                        ? NavisworksCoordinates.Shared
                        : NavisworksCoordinates.Internal
                };

                string exportName = Path.GetFileNameWithoutExtension(outputFileName);
                _logger.Info("Export", $"Starting NWC export to '{fullOutputPath}' (ExportLinks = {exportOptions.ExportLinks}).", modelName, "Exporting");

                // Revit 2024 defines this Document.Export overload with a void return type. An API
                // exception or missing/empty result is therefore treated as an export failure.
                doc.Export(outputDirectory, exportName, exportOptions);

                if (!File.Exists(fullOutputPath))
                {
                    _logger.Error("Export", $"Export completed without creating the expected output file: {fullOutputPath}", modelName, "VerifyingOutput");
                    return false;
                }

                var outputInfo = new FileInfo(fullOutputPath);
                if (outputInfo.Length <= 0)
                {
                    _logger.Error("Export", $"Export created an empty NWC output file: {fullOutputPath}", modelName, "VerifyingOutput");
                    return false;
                }

                _logger.Success("Export", $"NWC file created successfully. Size: {outputInfo.Length / (1024d * 1024d):F2} MB; path: {fullOutputPath}", modelName, "VerifyingOutput");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Export", $"Exception during NWC export: {ex.Message}", modelName, "Exporting", ex);
                return false;
            }
        }
    }
}
