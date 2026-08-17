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
                bool isCloud = modelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase);
                string modelName = isCloud ? modelPath.Split('|')[0].Replace("acc://", "") : Path.GetFileName(modelPath);

                if (!isCloud && !File.Exists(modelPath))
                {
                    _logger.Error("Revit", $"Model file not found: {modelPath}", modelName, "Preflight");
                    return null;
                }

                _logger.Info("Revit", $"Opening model {(isCloud ? "from cloud" : "detached")}: {modelPath}", modelName, "OpeningModel");

                ModelPath revitModelPath;
                if (isCloud)
                {
                    // Format: acc://ModelName.rvt|urn:adsk.wipprod:fs.file:vf.XXXXX
                    // We must extract the URN and convert it to a proper Cloud ModelPath
                    string urn = modelPath.Contains("|") ? modelPath.Split('|')[1] : string.Empty;
                    
                    if (string.IsNullOrEmpty(urn))
                    {
                        throw new InvalidOperationException("Cloud model URN is missing from the path.");
                    }

                    // To avoid Revit prepending its install path, we should NOT use ConvertUserVisiblePathToModelPath
                    // for cloud paths that Revit doesn't recognize as "user visible".
                    // Instead, we can try to create a Cloud ModelPath.
                    // Note: This is a simplified version. A production version would resolve the Region, ProjectGuid, and ModelGuid.
                    try
                    {
                        // Try to parse the URN to get GUIDs if possible, or use the URN directly if Revit 2024 supports it
                        revitModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPath);
                    }
                    catch
                    {
                        // Fallback: If it fails, it's likely because of the custom acc:// prefix
                        // Let's try removing the prefix and see if Revit recognizes the URN part
                        revitModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(urn);
                    }
                }
                else
                {
                    revitModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPath);
                }
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
                string safeModelName = modelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase) 
                    ? modelPath.Split('|')[0].Replace("acc://", "") 
                    : Path.GetFileName(modelPath);
                _logger.Error("Revit", $"Failed to open model detached: {ex.Message}", safeModelName, "OpeningModel", ex);
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
