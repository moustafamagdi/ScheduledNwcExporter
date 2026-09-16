using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using ScheduledNwcExporter.Configuration;

namespace ScheduledNwcExporter.Reliability
{
    public enum FreshnessState
    {
        Current,
        NeedsExport
    }

    public sealed class FreshnessEvaluation
    {
        public FreshnessState State { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool NeedsExport => State == FreshnessState.NeedsExport;
    }

    public sealed class ExportStateRecord
    {
        public string JobId { get; set; } = string.Empty;
        public DateTime ExportedAtUtc { get; set; }
        public DateTime? SourceModifiedUtc { get; set; }
        public string CloudVersionId { get; set; } = string.Empty;
        public string ExportFingerprint { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Persists verified export snapshots separately from the editable job configuration.
    /// Only an NWC that was actually written is recorded here; skipped-existing runs never
    /// advance freshness state.
    /// </summary>
    public static class ExportStateStore
    {
        private static readonly object Sync = new object();
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoustafaMagdi",
            "ScheduledNwcExporter");
        private static readonly string FilePath = Path.Combine(DirectoryPath, "export-state.json");

        public static ExportStateRecord? Get(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return null;
            lock (Sync)
            {
                Dictionary<string, ExportStateRecord> records = LoadUnsafe();
                return records.TryGetValue(jobId, out ExportStateRecord? record) ? record : null;
            }
        }

        public static void RecordSuccessfulExport(ModelExportJob job, AppSettings appSettings, string outputPath)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (appSettings == null) throw new ArgumentNullException(nameof(appSettings));
            if (string.IsNullOrWhiteSpace(job.Id)) return;

            DateTime? sourceModifiedUtc = ResolveCurrentSourceModifiedUtc(job);
            ExportSettings effectiveSettings = job.CustomExportSettings ?? appSettings.Export;

            var record = new ExportStateRecord
            {
                JobId = job.Id,
                ExportedAtUtc = DateTime.UtcNow,
                SourceModifiedUtc = sourceModifiedUtc,
                CloudVersionId = job.CloudVersionId ?? string.Empty,
                ExportFingerprint = ExportFingerprintService.Compute(effectiveSettings),
                OutputPath = outputPath ?? string.Empty
            };

            lock (Sync)
            {
                Dictionary<string, ExportStateRecord> records = LoadUnsafe();
                records[job.Id] = record;
                SaveUnsafe(records);
            }
        }

        private static Dictionary<string, ExportStateRecord> LoadUnsafe()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new Dictionary<string, ExportStateRecord>(StringComparer.OrdinalIgnoreCase);

                string json = File.ReadAllText(FilePath);
                var records = JsonConvert.DeserializeObject<List<ExportStateRecord>>(json) ?? new List<ExportStateRecord>();
                return records
                    .Where(record => record != null && !string.IsNullOrWhiteSpace(record.JobId))
                    .GroupBy(record => record.JobId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.OrderByDescending(record => record.ExportedAtUtc).First(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // A damaged state file must never make a model look current. Returning an empty
                // store makes every job conservative (Needs Export) until a verified export occurs.
                return new Dictionary<string, ExportStateRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveUnsafe(Dictionary<string, ExportStateRecord> records)
        {
            Directory.CreateDirectory(DirectoryPath);
            string json = JsonConvert.SerializeObject(records.Values.OrderBy(record => record.JobId).ToList(), Formatting.Indented);
            string tempPath = FilePath + ".tmp";
            string backupPath = FilePath + ".bak";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(FilePath))
            {
                try
                {
                    File.Replace(tempPath, FilePath, backupPath, true);
                }
                catch
                {
                    File.Copy(tempPath, FilePath, true);
                    File.Delete(tempPath);
                }
            }
            else
            {
                File.Move(tempPath, FilePath);
            }
        }

        internal static DateTime? ResolveCurrentSourceModifiedUtc(ModelExportJob job)
        {
            // Local files are cheap to inspect and may change while the manager window is closed,
            // so always prefer the live filesystem timestamp over cached UI metadata.
            if (!job.IsCloud && !string.IsNullOrWhiteSpace(job.SourceModelPath) && File.Exists(job.SourceModelPath))
                return File.GetLastWriteTimeUtc(job.SourceModelPath);

            if (job.LastSourceModifiedUtc.HasValue)
                return NormalizeUtc(job.LastSourceModifiedUtc.Value);

            return null;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }

    public static class FreshnessEvaluator
    {
        public static FreshnessEvaluation Evaluate(ModelExportJob job, AppSettings appSettings)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (appSettings == null) throw new ArgumentNullException(nameof(appSettings));

            ExportStateRecord? record = ExportStateStore.Get(job.Id);
            if (record == null)
                return NeedsExport("No verified export snapshot exists for this job.");

            if (string.IsNullOrWhiteSpace(record.OutputPath) || !File.Exists(record.OutputPath))
                return NeedsExport("The last verified NWC output no longer exists.");

            try
            {
                if (new FileInfo(record.OutputPath).Length <= 0)
                    return NeedsExport("The last verified NWC output is empty.");
            }
            catch
            {
                return NeedsExport("The last verified NWC output cannot be verified.");
            }

            ExportSettings effectiveSettings = job.CustomExportSettings ?? appSettings.Export;
            string currentFingerprint = ExportFingerprintService.Compute(effectiveSettings);
            if (!string.Equals(currentFingerprint, record.ExportFingerprint, StringComparison.Ordinal))
                return NeedsExport("The export settings or export-scope policy changed since the last verified NWC.");

            if (job.IsCloud && !string.IsNullOrWhiteSpace(job.CloudVersionId))
            {
                if (string.IsNullOrWhiteSpace(record.CloudVersionId) ||
                    !string.Equals(job.CloudVersionId, record.CloudVersionId, StringComparison.Ordinal))
                {
                    return NeedsExport("The ACC model tip version changed since the last verified NWC.");
                }

                return Current("ACC tip version, export settings, and output file match the last verified export.");
            }

            DateTime? sourceModifiedUtc = ExportStateStore.ResolveCurrentSourceModifiedUtc(job);
            if (!sourceModifiedUtc.HasValue || !record.SourceModifiedUtc.HasValue)
                return NeedsExport("The source modification state cannot be verified safely.");

            if (sourceModifiedUtc.Value > record.SourceModifiedUtc.Value.AddSeconds(1))
                return NeedsExport("The source model was modified after the last verified NWC.");

            return Current("Source version, export settings, and output file match the last verified export.");
        }

        private static FreshnessEvaluation NeedsExport(string reason)
        {
            return new FreshnessEvaluation { State = FreshnessState.NeedsExport, Reason = reason };
        }

        private static FreshnessEvaluation Current(string reason)
        {
            return new FreshnessEvaluation { State = FreshnessState.Current, Reason = reason };
        }
    }

    public static class ExportFingerprintService
    {
        // Bump this whenever the generated export-view policy changes in a way that can affect NWC content.
        private const string ExportScopeRevision = "NWC_SCOPE_2026_09_R1";

        public static string Compute(ExportSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            string canonical = string.Join("|", new[]
            {
                "scope=" + ExportScopeRevision,
                "links=false",
                "projectCad=false",
                "familyCad=true",
                "levels=false",
                "grids=false",
                "worksets=visible",
                "detail=fine",
                "ConvertElementProperties=" + settings.ConvertElementProperties,
                "DivideFileIntoLevels=" + settings.DivideFileIntoLevels,
                "ExportElementIds=" + settings.ExportElementIds,
                "ExportParts=" + settings.ExportParts,
                "ExportInternalCoordinates=" + settings.ExportInternalCoordinates,
                "ConvertLights=" + settings.ConvertLights,
                "ExportRoomAsAttribute=" + settings.ExportRoomAsAttribute,
                "ExportRoomGeometry=" + settings.ExportRoomGeometry,
                "ExportUrls=" + settings.ExportUrls,
                "FindMissingMaterials=" + settings.FindMissingMaterials,
                "ExportAllParameters=" + settings.ExportAllParameters,
                "ExportElementParameters=" + settings.ExportElementParameters,
                "FacetingFactor=" + settings.FacetingFactor.ToString("R", CultureInfo.InvariantCulture),
                "UseTemporaryCopyWithoutRevitLinks=" + settings.UseTemporaryCopyWithoutRevitLinks
            });

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }
}
