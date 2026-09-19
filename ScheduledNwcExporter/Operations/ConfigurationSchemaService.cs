using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Operations
{
    /// <summary>
    /// Lightweight schema migration layer that runs before ConfigurationManager deserializes settings.
    /// A sidecar version marker remains authoritative because older ConfigurationManager builds may
    /// rewrite config.json without preserving unknown JSON properties.
    /// </summary>
    public static class ConfigurationSchemaService
    {
        public const int CurrentVersion = 2;

        private static readonly string RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoustafaMagdi",
            "ScheduledNwcExporter");

        public static string ConfigPath { get; } = Path.Combine(RootDirectory, "config.json");
        public static string VersionFilePath { get; } = Path.Combine(RootDirectory, "config.version");

        public static int ReadVersion()
        {
            try
            {
                if (File.Exists(VersionFilePath) && int.TryParse(File.ReadAllText(VersionFilePath).Trim(), out int sidecarVersion))
                    return sidecarVersion;

                if (!File.Exists(ConfigPath)) return 0;
                JObject root = JObject.Parse(File.ReadAllText(ConfigPath));
                return root.Value<int?>("ConfigVersion") ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        public static void MigrateBeforeLoad(ILogger logger)
        {
            try
            {
                Directory.CreateDirectory(RootDirectory);
                int version = ReadVersion();

                if (File.Exists(ConfigPath) && version < CurrentVersion)
                {
                    JObject root = JObject.Parse(File.ReadAllText(ConfigPath));

                    if (version < 1)
                    {
                        if (root["Jobs"] == null || root["Jobs"]!.Type == JTokenType.Null)
                            root["Jobs"] = new JArray();
                        if (root["Scheduler"] is JObject scheduler && (scheduler["Slots"] == null || scheduler["Slots"]!.Type == JTokenType.Null))
                            scheduler["Slots"] = new JArray();
                    }

                    if (version < 2)
                    {
                        // Phase 2 history, reports and log correlation live in sidecar operational
                        // stores, so no destructive transformation of user jobs/settings is required.
                    }

                    root["ConfigVersion"] = CurrentVersion;
                    AtomicWriteConfig(root.ToString(Formatting.Indented));
                    logger.Info("Config", $"Configuration schema migrated from v{version} to v{CurrentVersion}.");
                }

                WriteVersionMarker(CurrentVersion);
            }
            catch (Exception ex)
            {
                logger.Warning("Config", $"Configuration schema migration could not be completed: {ex.Message}", string.Empty, "Migration", ex);
            }
        }

        public static void StampCurrentVersion(ILogger logger)
        {
            try
            {
                Directory.CreateDirectory(RootDirectory);
                WriteVersionMarker(CurrentVersion);

                if (!File.Exists(ConfigPath)) return;
                JObject root = JObject.Parse(File.ReadAllText(ConfigPath));
                if ((root.Value<int?>("ConfigVersion") ?? 0) != CurrentVersion)
                {
                    root["ConfigVersion"] = CurrentVersion;
                    AtomicWriteConfig(root.ToString(Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                logger.Warning("Config", $"Could not stamp configuration schema version: {ex.Message}", string.Empty, "Migration", ex);
            }
        }

        private static void WriteVersionMarker(int version)
        {
            string tempPath = VersionFilePath + ".tmp";
            File.WriteAllText(tempPath, version.ToString(), new UTF8Encoding(false));
            if (File.Exists(VersionFilePath)) File.Delete(VersionFilePath);
            File.Move(tempPath, VersionFilePath);
        }

        private static void AtomicWriteConfig(string json)
        {
            Directory.CreateDirectory(RootDirectory);
            string tempPath = ConfigPath + ".schema.tmp";
            string backupPath = ConfigPath + ".schema.bak";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(ConfigPath))
            {
                try
                {
                    File.Replace(tempPath, ConfigPath, backupPath, true);
                }
                catch
                {
                    File.Copy(tempPath, ConfigPath, true);
                    File.Delete(tempPath);
                }
            }
            else
            {
                File.Move(tempPath, ConfigPath);
            }
        }
    }
}
