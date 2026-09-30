using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using ScheduledNwcExporter.Application;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
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
    /// During programmatic open, narrowly-approved failure resolutions are delegated to the
    /// centralized unattended policy registry.
    /// </summary>
    public class DocumentManager
    {
        private readonly ILogger _logger;

        public DocumentManager(ILogger logger)
        {
            _logger = logger;
        }

        public Document? OpenModelDetached(Autodesk.Revit.ApplicationServices.Application app, string modelPath)
        {
            bool isCloud = modelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase);
            string modelName = isCloud ? modelPath.Split('|')[0].Replace("acc://", "") : Path.GetFileName(modelPath);
            EventHandler<FailuresProcessingEventArgs>? failureHandler = null;

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
                    string[] parts = modelPath.Split('|');
                    if (parts.Length < 4)
                    {
                        throw new InvalidOperationException("This cloud model was added using an older version of the tool. Please REMOVE it from the list and re-add it using the '+ Add Model' > 'Cloud' button to capture the required Revit GUIDs.");
                    }

                    string region = parts[1];
                    string cleanProjectGuid = parts[2].StartsWith("b.") ? parts[2].Substring(2) : parts[2];
                    Guid projectGuid = Guid.Parse(cleanProjectGuid);
                    Guid modelGuid = Guid.Parse(parts[3]);

                    _logger.Info("Revit", $"Resolving cloud path: Region={region}, Project={projectGuid}, Model={modelGuid}", modelName, "OpeningModel");
                    revitModelPath = ModelPathUtils.ConvertCloudGUIDsToCloudPath(region, projectGuid, modelGuid);
                    openOptions.DetachFromCentralOption = DetachFromCentralOption.DoNotDetach;
                }
                else
                {
                    revitModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPath);
                    openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                }

                var worksetConfiguration = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
                openOptions.SetOpenWorksetsConfiguration(worksetConfiguration);

                failureHandler = (sender, args) => HandleOpeningFailures(args, modelName);
                app.FailuresProcessing += failureHandler;

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
            finally
            {
                if (failureHandler != null)
                    app.FailuresProcessing -= failureHandler;
            }
        }

        private void HandleOpeningFailures(FailuresProcessingEventArgs args, string modelName)
        {
            try
            {
                FailuresAccessor accessor = args.GetFailuresAccessor();
                bool resolvedAny = false;

                foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
                {
                    string description = failure.GetDescriptionText() ?? string.Empty;
                    string failureId = UnattendedPolicyRegistry.GetFailureDefinitionIdText(failure);

                    if (UnattendedPolicyRegistry.ShouldDeleteBrokenDimension(failure, out string deletePolicyMatch))
                    {
                        if (!accessor.IsFailureResolutionPermitted(failure, FailureResolutionType.DeleteElements))
                        {
                            _logger.Warning(
                                "Revit",
                                $"Recognized invalid radial dimension but DeleteElements is unavailable. FailureDefinitionId='{failureId}', Match='{deletePolicyMatch}', Description='{description}'.",
                                modelName,
                                "OpeningFailures");
                            continue;
                        }

                        failure.SetCurrentResolutionType(FailureResolutionType.DeleteElements);
                        accessor.ResolveFailure(failure);
                        resolvedAny = true;

                        _logger.Warning(
                            "Revit",
                            $"Automatically applied Delete Dimension(s). FailureDefinitionId='{failureId}', Match='{deletePolicyMatch}', Description='{description}'.",
                            modelName,
                            "OpeningFailures");
                        continue;
                    }

                    if (!UnattendedPolicyRegistry.ShouldDetachBrokenReference(failure, out string policyMatch))
                    {
                        _logger.Debug(
                            "Revit",
                            $"Opening failure left untouched. FailureDefinitionId='{failureId}', Description='{description}'.",
                            modelName,
                            "OpeningFailures");
                        continue;
                    }

                    if (!failure.HasResolutionOfType(FailureResolutionType.DetachElements))
                    {
                        _logger.Warning(
                            "Revit",
                            $"Recognized broken annotation reference but DetachElements is unavailable. FailureDefinitionId='{failureId}', Match='{policyMatch}', Description='{description}'.",
                            modelName,
                            "OpeningFailures");
                        continue;
                    }

                    failure.SetCurrentResolutionType(FailureResolutionType.DetachElements);
                    accessor.ResolveFailure(failure);
                    resolvedAny = true;

                    _logger.Warning(
                        "Revit",
                        $"Automatically applied Remove Reference. FailureDefinitionId='{failureId}', Match='{policyMatch}', Description='{description}'.",
                        modelName,
                        "OpeningFailures");
                }

                if (resolvedAny)
                    args.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
            }
            catch (Exception ex)
            {
                _logger.Warning("Revit", $"Could not automatically process model-opening failures: {ex.Message}", modelName, "OpeningFailures", ex);
            }
        }

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
