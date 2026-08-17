using System;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    /// <summary>
    /// Signals that Revit, the authority for cloud-model entitlement, denied the signed-in user.
    /// The queue must not retry this condition because retrying re-enters Revit's native cloud-open UI.
    /// </summary>
    public sealed class CloudModelAccessDeniedException : InvalidOperationException
    {
        public bool IsPermanentAccessDenial { get; }

        public CloudModelAccessDeniedException(string message, Exception innerException, bool isPermanentAccessDenial = true)
            : base(message, innerException)
        {
            IsPermanentAccessDenial = isPermanentAccessDenial;
        }
    }

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
            bool isCloud = modelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase);
            string modelName = isCloud ? modelPath.Split('|')[0].Replace("acc://", "") : Path.GetFileName(modelPath);

            try
            {

                if (!isCloud && !File.Exists(modelPath))
                {
                    _logger.Error("Revit", $"Model file not found: {modelPath}", modelName, "Preflight");
                    return null;
                }

                _logger.Info("Revit", $"Opening model {(isCloud ? "from cloud" : "detached")}: {modelPath}", modelName, "OpeningModel");

                ModelPath revitModelPath;
                var openOptions = new OpenOptions();

                if (isCloud)
                {
                    // Format: acc://ModelName.rvt|Region|ProjectGUID|ModelGUID
                    string[] parts = modelPath.Split('|');
                    if (parts.Length < 4)
                    {
                        throw new InvalidOperationException("This cloud model was added using an older version of the tool. Please REMOVE it from the list and re-add it using the '+ Add Model' > 'Cloud' button to capture the required Revit GUIDs.");
                    }

                    string region = parts[1];
                    // Strip "b." prefix if present as per expert advice
                    string cleanProjectGuid = parts[2].StartsWith("b.") ? parts[2].Substring(2) : parts[2];
                    
                    Guid projectGuid = Guid.Parse(cleanProjectGuid);
                    Guid modelGuid = Guid.Parse(parts[3]);

                    _logger.Info("Revit", $"Resolving cloud path: Region={region}, Project={projectGuid}, Model={modelGuid}", modelName, "OpeningModel");
                    
                    // The official way to create a Cloud ModelPath in Revit API
                    revitModelPath = ModelPathUtils.ConvertCloudGUIDsToCloudPath(region, projectGuid, modelGuid);
                    
                    // EXPERT FIX: Open central directly as ReadOnly to avoid "Detached" permission issues in cloud.
                    openOptions.DetachFromCentralOption = DetachFromCentralOption.DoNotDetach;
                }
                else
                {
                    revitModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPath);
                    openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                }

                // EXPERT FIX for Revit 2024: Use WorksetConfiguration to open all worksets
                var worksetConfiguration = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
                openOptions.SetOpenWorksetsConfiguration(worksetConfiguration);

                // This opens the document without activating it in Revit's user interface.
                Document doc = app.OpenDocumentFile(revitModelPath, openOptions);

                _logger.Success("Revit", $"Successfully opened {(isCloud ? "cloud" : "detached")} document: {modelName}", modelName, "OpeningModel");
                return doc;
            }
            catch (Exception ex)
            {
                bool accessDenied = isCloud &&
                    (ex.GetType().Name.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ex.Message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ex.Message.IndexOf("not authorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ex.Message.IndexOf("entitlement", StringComparison.OrdinalIgnoreCase) >= 0);

                _logger.Error("Revit", $"Failed to open model: {ex.Message}", modelName, "OpeningModel", ex);

                if (accessDenied)
                {
                    throw new CloudModelAccessDeniedException(
                        "Revit denied cloud-model access. Verify that the signed-in Autodesk user has a valid Revit Cloud Worksharing entitlement and View + Download + Upload + Edit permissions on the model folder.",
                        ex);
                }

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
