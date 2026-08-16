using System;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    /// <summary>
    /// Opens and closes source models for non-destructive export processing.
    /// </summary>
    public class DocumentManager
    {
        private readonly ILogger _logger;

        public DocumentManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Opens a Revit document in memory, detached from central where applicable, with all
        /// user-created worksets configured to open before the document is loaded.
        /// </summary>
        public Document? OpenModelDetached(Autodesk.Revit.ApplicationServices.Application app, string modelPath)
        {
            try
            {
                if (!File.Exists(modelPath))
                {
                    _logger.Error("Revit", $"Model file not found: {modelPath}", Path.GetFileName(modelPath), "Preflight");
                    return null;
                }

                string modelName = Path.GetFileName(modelPath);
                _logger.Info("Revit", $"Opening model detached: {modelPath}", modelName, "OpeningModel");

                ModelPath revitModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPath);
                var openOptions = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
                };

                // Worksets must be selected before opening. Revit does not expose an API to open
                // arbitrary worksets after the document has already been loaded.
                var worksetConfiguration = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
                openOptions.SetOpenWorksetsConfiguration(worksetConfiguration);

                // This opens the document without activating it in Revit's user interface.
                Document doc = app.OpenDocumentFile(revitModelPath, openOptions);

                _logger.Success("Revit", $"Successfully opened detached document: {modelPath}", modelName, "OpeningModel");
                return doc;
            }
            catch (Exception ex)
            {
                _logger.Error("Revit", $"Failed to open model detached: {ex.Message}", Path.GetFileName(modelPath), "OpeningModel", ex);
                return null;
            }
        }

        /// <summary>
        /// Closes an in-memory document without saving any changes.
        /// </summary>
        public void CloseDocumentSafely(Document? doc)
        {
            if (doc == null) return;

            try
            {
                string title = doc.Title;
                _logger.Info("Revit", $"Closing document safely: {title}", title, "ClosingModel");
                doc.Close(false);
                _logger.Success("Revit", $"Document closed successfully: {title}", title, "ClosingModel");
            }
            catch (Exception ex)
            {
                _logger.Warning("Revit", $"Error while closing document: {ex.Message}", string.Empty, "ClosingModel", ex);
            }
        }
    }
}
