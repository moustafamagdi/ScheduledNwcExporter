using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    /// <summary>
    /// Represents a source model prepared for one export job. A temporary source is deleted during cleanup.
    /// </summary>
    public sealed class PreparedModelSource : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string? _temporaryDirectory;

        internal PreparedModelSource(string sourcePath, string openPath, string? temporaryDirectory, ILogger logger, int disabledRevitLinkCount)
        {
            SourcePath = sourcePath;
            OpenPath = openPath;
            _temporaryDirectory = temporaryDirectory;
            _logger = logger;
            DisabledRevitLinkCount = disabledRevitLinkCount;
        }

        public string SourcePath { get; }
        public string OpenPath { get; }
        public bool IsTemporaryCopy => _temporaryDirectory != null;
        public int DisabledRevitLinkCount { get; }

        public void Dispose()
        {
            if (string.IsNullOrWhiteSpace(_temporaryDirectory)) return;

            try
            {
                if (Directory.Exists(_temporaryDirectory))
                {
                    Directory.Delete(_temporaryDirectory, true);
                    _logger.Debug("PerformanceMode", $"Deleted temporary model directory: {_temporaryDirectory}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("PerformanceMode", $"Could not delete temporary model directory '{_temporaryDirectory}': {ex.Message}", string.Empty, "Cleanup", ex);
            }
        }
    }

    /// <summary>
    /// Creates an isolated local RVT copy whose top-level Revit links are marked unloaded through
    /// TransmissionData. The original RVT is only read and is never modified.
    /// </summary>
    public sealed class TemporaryModelCopyService
    {
        private readonly ILogger _logger;

        public TemporaryModelCopyService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public PreparedModelSource Prepare(string sourceModelPath, bool disableRevitLinks, string modelName)
        {
            if (!disableRevitLinks)
            {
                _logger.Info("PerformanceMode", "Performance Mode is disabled. Opening the original source model.", modelName, "Preflight");
                return new PreparedModelSource(sourceModelPath, sourceModelPath, null, _logger, 0);
            }

            if (!File.Exists(sourceModelPath))
            {
                throw new FileNotFoundException("Source model file was not found.", sourceModelPath);
            }

            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "MoustafaMagdi",
                "ScheduledNwcExporter",
                "jobs",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);

            string temporaryModelPath = Path.Combine(temporaryDirectory, Path.GetFileName(sourceModelPath));
            try
            {
                _logger.Info("PerformanceMode", "Creating an isolated local temporary copy with Revit links disabled.", modelName, "PreparingTemporaryCopy");
                File.Copy(sourceModelPath, temporaryModelPath, false);

                int disabledCount = DisableTopLevelRevitLinks(temporaryModelPath, modelName);
                _logger.Info(
                    "PerformanceMode",
                    $"Temporary copy is ready. Top-level Revit links marked unloaded: {disabledCount}. Original source model remains unchanged.",
                    modelName,
                    "PreparingTemporaryCopy");

                return new PreparedModelSource(sourceModelPath, temporaryModelPath, temporaryDirectory, _logger, disabledCount);
            }
            catch
            {
                TryDeleteTemporaryDirectory(temporaryDirectory, modelName);
                throw;
            }
        }

        private int DisableTopLevelRevitLinks(string temporaryModelPath, string modelName)
        {
            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(temporaryModelPath);
            using (TransmissionData? transmissionData = TransmissionData.ReadTransmissionData(modelPath))
            {
                if (transmissionData == null)
                {
                    _logger.Warning(
                        "PerformanceMode",
                        "The temporary model has no TransmissionData. No Revit links were disabled; the model will open normally.",
                        modelName,
                        "PreparingTemporaryCopy");
                    return 0;
                }

                int disabledCount = 0;
                ICollection<ElementId> referenceIds = transmissionData.GetAllExternalFileReferenceIds();
                foreach (ElementId referenceId in referenceIds)
                {
                    ExternalFileReference externalReference = transmissionData.GetLastSavedReferenceData(referenceId);
                    if (externalReference != null && externalReference.ExternalFileReferenceType == ExternalFileReferenceType.RevitLink)
                    {
                        transmissionData.SetDesiredReferenceData(
                            referenceId,
                            externalReference.GetPath(),
                            externalReference.PathType,
                            false);
                        disabledCount++;
                    }
                }

                // Revit only honors desired external reference states on a transmitted model.
                transmissionData.IsTransmitted = true;
                TransmissionData.WriteTransmissionData(modelPath, transmissionData);
                return disabledCount;
            }
        }

        private void TryDeleteTemporaryDirectory(string temporaryDirectory, string modelName)
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
            catch (Exception cleanupException)
            {
                _logger.Warning("PerformanceMode", $"Temporary directory cleanup failed: {cleanupException.Message}", modelName, "Cleanup", cleanupException);
            }
        }
    }
}
