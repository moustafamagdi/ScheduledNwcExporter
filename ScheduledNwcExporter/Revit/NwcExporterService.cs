using System;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    public enum NwcExportOutcome
    {
        Exported,
        SkippedExisting,
        Failed
    }

    public sealed class NwcExportResult
    {
        public NwcExportOutcome Outcome { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public bool Succeeded => Outcome != NwcExportOutcome.Failed;
        public bool WroteOutput => Outcome == NwcExportOutcome.Exported;
    }

    /// <summary>
    /// Performs NWC exports through Revit's optional Navisworks exporter.
    /// </summary>
    public class NwcExporterService
    {
        private readonly ILogger _logger;

        public NwcExporterService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        public NwcExportResult ExportModelToNwc(Document doc, string outputDirectory, string outputFileName, ExportSettings settings, ElementId? exportViewId, string modelName)
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
                        _logger.Info("Export", "Output file already exists and overwrite policy is Skip. No new NWC was written and freshness will not advance.", modelName, "Exporting");
                        return new NwcExportResult
                        {
                            Outcome = NwcExportOutcome.SkippedExisting,
                            OutputPath = fullOutputPath
                        };
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
                    ConvertElementProperties = settings.ConvertElementProperties,
                    ExportLinks = false,
                    ExportElementIds = settings.ExportElementIds,
                    ExportRoomGeometry = settings.ExportRoomGeometry,
                    DivideFileIntoLevels = settings.DivideFileIntoLevels,
                    ExportParts = settings.ExportParts,
                    FacetingFactor = settings.FacetingFactor,
                    ExportUrls = settings.ExportUrls,
                    ExportRoomAsAttribute = settings.ExportRoomAsAttribute,
                    ConvertLights = settings.ConvertLights,
                    FindMissingMaterials = settings.FindMissingMaterials,
                    Coordinates = settings.ExportInternalCoordinates ? NavisworksCoordinates.Internal : NavisworksCoordinates.Shared
                };

                if (settings.ExportAllParameters)
                {
                    exportOptions.Parameters = NavisworksParameters.All;
                }
                else if (settings.ExportElementParameters)
                {
                    exportOptions.Parameters = NavisworksParameters.Elements;
                }
                else
                {
                    exportOptions.Parameters = NavisworksParameters.None;
                }

                if (exportViewId != null && exportViewId != ElementId.InvalidElementId)
                {
                    exportOptions.ExportScope = NavisworksExportScope.View;
                    exportOptions.ViewId = exportViewId;
                    _logger.Info("Export", $"Export scope configured to View (View ID: {exportViewId.IntegerValue}). Levels and Grids hidden; all worksets visible.", modelName, "Exporting");
                }
                else
                {
                    exportOptions.ExportScope = NavisworksExportScope.Model;
                    _logger.Info("Export", "Export scope configured to Model (fallback).", modelName, "Exporting");
                }

                string exportName = Path.GetFileNameWithoutExtension(outputFileName);
                _logger.Info("Export", $"Starting NWC export to '{fullOutputPath}' (ExportLinks = {exportOptions.ExportLinks}).", modelName, "Exporting");

                doc.Export(outputDirectory, exportName, exportOptions);

                if (!File.Exists(fullOutputPath))
                {
                    _logger.Error("Export", $"Export completed without creating the expected output file: {fullOutputPath}", modelName, "VerifyingOutput");
                    return new NwcExportResult { Outcome = NwcExportOutcome.Failed, OutputPath = fullOutputPath };
                }

                var outputInfo = new FileInfo(fullOutputPath);
                if (outputInfo.Length <= 0)
                {
                    _logger.Error("Export", $"Export created an empty NWC output file: {fullOutputPath}", modelName, "VerifyingOutput");
                    return new NwcExportResult { Outcome = NwcExportOutcome.Failed, OutputPath = fullOutputPath };
                }

                _logger.Success("Export", $"NWC file created successfully. Size: {outputInfo.Length / (1024d * 1024d):F2} MB; path: {fullOutputPath}", modelName, "VerifyingOutput");
                return new NwcExportResult { Outcome = NwcExportOutcome.Exported, OutputPath = fullOutputPath };
            }
            catch (Exception ex)
            {
                _logger.Error("Export", $"Exception during NWC export: {ex.Message}", modelName, "Exporting", ex);
                return new NwcExportResult { Outcome = NwcExportOutcome.Failed };
            }
        }
    }
}
