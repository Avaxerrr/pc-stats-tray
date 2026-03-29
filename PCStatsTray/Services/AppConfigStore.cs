using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PCStatsTray
{
    internal static class AppConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hwmon_config.json");

        public static OverlayConfig LoadOverlayConfig(string path)
        {
            try
            {
                var config = LoadFullConfig(path);
                if (config?.Overlay != null)
                {
                    config.Overlay.NormalizeMetrics();
                    return config.Overlay;
                }
            }
            catch
            {
            }

            var overlay = new OverlayConfig();
            overlay.NormalizeMetrics();
            return overlay;
        }

        public static void SaveOverlayConfig(string path, OverlayConfig overlay)
        {
            try
            {
                overlay.NormalizeMetrics();
                var config = LoadFullConfig(path) ?? new StoredAppConfig();
                config.Overlay = overlay;
                SaveFullConfig(path, config);
            }
            catch
            {
            }
        }

        public static HashSet<string> LoadHiddenSensors(string path)
        {
            try
            {
                var config = LoadFullConfig(path);
                return config?.HiddenSensors != null
                    ? new HashSet<string>(config.HiddenSensors)
                    : new HashSet<string>();
            }
            catch
            {
                return new HashSet<string>();
            }
        }

        public static void SaveHiddenSensors(string path, IEnumerable<string> hiddenSensors)
        {
            try
            {
                var config = LoadFullConfig(path) ?? new StoredAppConfig();
                config.HiddenSensors = hiddenSensors.Distinct(StringComparer.Ordinal).ToList();
                SaveFullConfig(path, config);
            }
            catch
            {
            }
        }

        private static StoredAppConfig? LoadFullConfig(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StoredAppConfig>(json);
        }

        private static void SaveFullConfig(string path, StoredAppConfig config)
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(path, json);
        }
    }
}
