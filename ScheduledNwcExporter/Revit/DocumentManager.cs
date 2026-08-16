using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    public class DocumentManager
    {
        private readonly ILogger _logger;

        public DocumentManager(ILogger logger)
        {
            _logger = logger;
        }

        public Document? OpenModelDetached(Autodesk.Revit.ApplicationServices.Application app, string modelPath)
        {
            try
            {
                if (!File.Exists(modelPath))
                {
                    _logger.Error("Revit", $"Model file not found: {modelPath}", Path.GetFileName(modelPath), "Preflight");
                    return null;
                }

                _logger.Info("Revit", $"Opening model detached: {modelPath}", Path.GetFileName(modelPath), "OpeningModel");

                ModelPath modelUIMPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPath);
                
                OpenOptions openOptions = new OpenOptions();
                
                // Configure Detach from Central
                WorksharingOpenOptions wsOptions = openOptions.GetWorksharingOpenOptions();
                wsOptions.OpenAllWorksets = true;
                wsOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                openOptions.SetWorksharingOpenOptions(wsOptions);

                // Open without making it active in the UI (background/in-memory)
                Document doc = app.OpenDocumentFile(modelUIMPath, openOptions);
                
                _logger.Success("Revit", $"Successfully opened detached document: {modelPath}", Path.GetFileName(modelPath), "OpeningModel");
                return doc;
            }
            catch (Exception ex)
            {
                _logger.Error("Revit", $"Failed to open model detached: {ex.Message}", Path.GetFileName(modelPath), "OpeningModel", ex);
                return null;
            }
        }

        public void CloseDocumentSafely(Document? doc)
        {
            if (doc == null) return;
            try
            {
                string title = doc.Title;
                _logger.Info("Revit", $"Closing document safely: {title}", title, "ClosingModel");
                // Close without saving changes (since it's a detached read-only export session)
                doc.Close(false);
                _logger.Success("Revit", $"Document closed successfully: {title}", title, "ClosingModel");
            }
            catch (Exception ex)
            {
                _logger.Warning("Revit", $"Error while closing document: {ex.Message}", "", "ClosingModel", ex);
            }
        }
    }
}
