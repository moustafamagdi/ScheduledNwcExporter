using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit;

namespace ScheduledNwcExporter.Queue
{
    public class BatchResult
    {
        public int TotalModels { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public List<string> FailedModelNames { get; set; } = new List<string>();
    }

    public class JobProcessor
    {
        private readonly Autodesk.Revit.ApplicationServices.Application _app;
        private readonly AppSettings _settings;
        private readonly ILogger _logger;
        private readonly DocumentManager _docManager;
        private readonly WorksetManager _worksetManager;
        private readonly LinkManager _linkManager;
        private readonly NwcExporterService _nwcExporter;

        public event EventHandler<string>? JobStatusUpdated;
        public event EventHandler<int>? OverallProgressUpdated;

        private bool _isCancelled = false;

        public JobProcessor(Autodesk.Revit.ApplicationServices.Application app, AppSettings settings, ILogger logger)
        {
            _app = app;
            _settings = settings;
            _logger = logger;
            _docManager = new DocumentManager(logger);
            _worksetManager = new WorksetManager(logger);
            _linkManager = new LinkManager(logger);
            _nwcExporter = new NwcExporterService(logger);
        }

        public void Cancel()
        {
            _isCancelled = true;
            _logger.Warning("Scheduler", "Cancellation requested by user. Will stop after current safe boundary.", "", "Cancellation");
        }

        public async Task<BatchResult> ProcessQueueAsync(IEnumerable<ModelExportJob> jobs)
        {
            _isCancelled = false;
            var batchResult = new BatchResult();
            var jobList = new List<ModelExportJob>(jobs);
            batchResult.TotalModels = jobList.Count;

            DateTime startTime = DateTime.Now;
            _logger.Info("Scheduler", $"Export session started. Total models in queue: {batchResult.TotalModels}");

            int currentIndex = 0;

            foreach (var job in jobList)
            {
                if (_isCancelled)
                {
                    _logger.Warning("Scheduler", "Export session aborted due to user cancellation.", Path.GetFileName(job.SourceModelPath), "Cancellation");
                    break;
                }

                currentIndex++;
                OverallProgressUpdated?.Invoke(this, (int)((double)currentIndex / batchResult.TotalModels * 100));

                if (!job.IsEnabled)
                {
                    _logger.Info("Scheduler", $"Job is disabled. Skipping: {job.SourceModelPath}", Path.GetFileName(job.SourceModelPath), "Preflight");
                    batchResult.Skipped++;
                    job.LastStatus = "Skipped";
                    continue;
                }

                string modelName = Path.GetFileName(job.SourceModelPath);
                JobStatusUpdated?.Invoke(this, $"Processing: {modelName} ({currentIndex}/{batchResult.TotalModels})");

                bool success = false;
                int attempts = 0;
                int maxAttempts = Math.Max(1, job.RetryCount + 1);
                string lastErrorMsg = string.Empty;

                DateTime jobStart = DateTime.Now;

                while (attempts < maxAttempts && !success && !_isCancelled)
                {
                    attempts++;
                    if (attempts > 1)
                    {
                        _logger.Warning("Job", $"Retrying job (Attempt {attempts} of {maxAttempts})", modelName, "Retrying");
                    }

                    Document? doc = null;
                    try
                    {
                        job.LastStatus = $"Attempt {attempts} - Opening";
                        
                        // 1. Preflight check
                        if (!File.Exists(job.SourceModelPath))
                        {
                            throw new FileNotFoundException($"Source model file not found: {job.SourceModelPath}");
                        }

                        // 2. Open model detached
                        doc = _docManager.OpenModelDetached(_app, job.SourceModelPath);
                        if (doc == null)
                        {
                            throw new InvalidOperationException("Failed to open document detached.");
                        }

                        // 3. Verify worksets
                        job.LastStatus = "Verifying Worksets";
                        _worksetManager.VerifyAndOpenAllWorksets(doc, modelName);

                        // 4. Inspect links
                        job.LastStatus = "Inspecting Links";
                        _linkManager.InspectAndLogRevitLinks(doc, modelName);

                        // 5. Resolve output filename using templates
                        string resolvedOutputName = ResolveFilenameTemplate(job.OutputFileNameTemplate, modelName);

                        // 6. Export to NWC
                        job.LastStatus = "Exporting NWC";
                        bool exportSuccess = _nwcExporter.ExportModelToNwc(doc, job.OutputDirectory, resolvedOutputName, _settings.Export, modelName);

                        if (exportSuccess)
                        {
                            success = true;
                        }
                        else
                        {
                            throw new Exception("NWC exporter returned failure result.");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastErrorMsg = ex.Message;
                        _logger.Error("Job", $"Model export failed for {modelName}: {ex.Message}", modelName, "Exporting", ex);
                        
                        // Permanent failure check: if file not found, do not retry
                        if (ex is FileNotFoundException)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        // 7. Safe cleanup
                        if (doc != null)
                        {
                            job.LastStatus = "Closing Model";
                            _docManager.CloseDocumentSafely(doc);
                        }
                    }
                }

                TimeSpan jobDuration = DateTime.Now - jobStart;
                job.LastDuration = jobDuration.ToString(@"hh\:mm\:ss");
                job.LastRun = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                if (success)
                {
                    batchResult.Successful++;
                    job.LastStatus = "Success";
                    job.LastError = string.Empty;
                    _logger.Success("Job", $"Job completed successfully for {modelName} in {job.LastDuration}", modelName, "Completed");
                }
                else
                {
                    batchResult.Failed++;
                    job.LastStatus = "Failed";
                    job.LastError = lastErrorMsg;
                    batchResult.FailedModelNames.Add(modelName);
                    _logger.Error("Job", $"Job permanently failed for {modelName}. Reason: {lastErrorMsg}", modelName, "Failed");
                }
            }

            batchResult.TotalDuration = DateTime.Now - startTime;
            _logger.Info("Scheduler", $"Export session finished. Successful: {batchResult.Successful}, Failed: {batchResult.Failed}, Skipped: {batchResult.Skipped}, Duration: {batchResult.TotalDuration:hh\\:mm\\:ss}");

            return batchResult;
        }

        private string ResolveFilenameTemplate(string template, string modelFileName)
        {
            string modelNameOnly = Path.GetFileNameWithoutExtension(modelFileName);
            DateTime now = DateTime.Now;

            string resolved = template
                .Replace("{ModelName}", modelNameOnly)
                .Replace("{ModelFileName}", modelFileName)
                .Replace("{Date}", now.ToString("yyyy-MM-dd"))
                .Replace("{Time}", now.ToString("HH-mm-ss"))
                .Replace("{Year}", now.ToString("yyyy"))
                .Replace("{Month}", now.ToString("MM"))
                .Replace("{Day}", now.ToString("dd"))
                .Replace("{Hour}", now.ToString("HH"))
                .Replace("{Minute}", now.ToString("mm"));

            // Sanitize invalid filename chars
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                resolved = resolved.Replace(c, '_');
            }

            if (!resolved.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase))
            {
                resolved += ".nwc";
            }

            return resolved;
        }
    }
}
