using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Operations
{
    /// <summary>
    /// Lightweight schema marker/migration layer that runs before ConfigurationManager deserializes
    /// the user's settings. Unknown JSON properties are intentionally tolerated by the existing
    /// configuration model, so the schema version can be introduced without breaking older builds.
    /// </summary>
    public static class ConfigurationSchemaService
    {
        public const int CurrentVersion = 2;

        public static string ConfigPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoustafaMagdi",
            "ScheduledNwcExporter",
            "config.json");

        public static int ReadVersion()
        {
            try
            {
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
                if (!File.Exists(ConfigPath)) return;

                JObject root = JObject.Parse(File.ReadAllText(ConfigPath));
                int version = root.Value<int?>("ConfigVersion") ?? 0;
                if (version >= CurrentVersion) return;

                if (version < 1)
                {
                    // Normalize missing collections so older/hand-edited files deserialize safely.
                    if (root["Jobs"] == null || root["Jobs"]!.Type == JTokenType.Null)
                        root["Jobs"] = new JArray();
                    if (root["Scheduler"] is JObject scheduler && (scheduler["Slots"] == null || scheduler["Slots"]!.Type == JTokenType.Null))
                        scheduler["Slots"] = new JArray();
                }

                if (version < 2)
                {
                    // Phase 2 introduces operational/session sidecars rather than embedding transient
                    // history into config.json. No destructive data transformation is required.
                }

                root["ConfigVersion"] = CurrentVersion;
                AtomicWrite(root.ToString(Formatting.Indented));
                logger.Info("Config", $"Configuration schema migrated from v{version} to v{CurrentVersion}.");
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
                if (!File.Exists(ConfigPath)) return;
                JObject root = JObject.Parse(File.ReadAllText(ConfigPath));
                if ((root.Value<int?>("ConfigVersion") ?? 0) == CurrentVersion) return;
                root["ConfigVersion"] = CurrentVersion;
                AtomicWrite(root.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                logger.Warning("Config", $"Could not stamp configuration schema version: {ex.Message}", string.Empty, "Migration", ex);
            }
        }

        private static void AtomicWrite(string json)
        {
            string directory = Path.GetDirectoryName(ConfigPath) ?? string.Empty;
            Directory.CreateDirectory(directory);
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
