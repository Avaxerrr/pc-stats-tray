using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
{
    internal sealed class DashboardMetricValue
    {
        public string Key { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public string SourceName { get; init; } = string.Empty;
        public string? Value { get; init; }
        public bool DefaultVisible { get; init; }
    }

    internal static class DashboardSnapshotBuilder
    {
        private static readonly string[] StorageMetricKeys =
        {
            "StorageTemp",
            "StorageLoad",
            "StorageRead",
            "StorageWrite"
        };

        public static DashboardSnapshot Build(Computer computer, OverlayConfig config, int refreshIntervalMs)
        {
            var currentValues = OverlayMetricCollector.Collect(computer, config, useOverlayDisplayModes: false);
            var metrics = BuildStaticMetricValues(currentValues, includeStorageMetrics: false);
            metrics.AddRange(BuildStorageMetricValues(computer));
            return Build(metrics, refreshIntervalMs);
        }

        internal static DashboardSnapshot Build(OverlayConfig config, IReadOnlyDictionary<string, string> currentValues, int refreshIntervalMs)
        {
            return Build(BuildStaticMetricValues(currentValues, includeStorageMetrics: true), refreshIntervalMs);
        }

        internal static DashboardSnapshot Build(IEnumerable<DashboardMetricValue> metricValues, int refreshIntervalMs)
        {
            return new DashboardSnapshot
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                MachineName = Environment.MachineName,
                RefreshIntervalMs = refreshIntervalMs,
                Metrics = metricValues
                    .Select(metric => new DashboardMetricSnapshot
                    {
                        Key = metric.Key,
                        Label = metric.Label,
                        Group = metric.Group,
                        SourceName = metric.SourceName,
                        Value = metric.Value,
                        Available = true,
                        DefaultVisible = metric.DefaultVisible
                    })
                    .ToList()
            };
        }

        private static List<DashboardMetricValue> BuildStaticMetricValues(
            IReadOnlyDictionary<string, string> currentValues,
            bool includeStorageMetrics)
        {
            return DashboardMetricCatalog.GetDefinitions()
                .Where(metric => currentValues.ContainsKey(metric.Key))
                .Where(metric => includeStorageMetrics || !IsStorageMetric(metric.Key))
                .Select(metric => new DashboardMetricValue
                {
                    Key = metric.Key,
                    Label = metric.Label,
                    Group = metric.Group,
                    SourceName = string.Empty,
                    Value = currentValues.TryGetValue(metric.Key, out string? value) ? value : null,
                    DefaultVisible = metric.DefaultVisible
                })
                .ToList();
        }

        private static List<DashboardMetricValue> BuildStorageMetricValues(Computer computer)
        {
            var metrics = new List<DashboardMetricValue>();
            var storageHardware = computer.Hardware
                .Where(hardware => hardware.HardwareType == HardwareType.Storage)
                .ToList();

            var duplicateCounts = storageHardware
                .GroupBy(hardware => NormalizeSourceName(hardware.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var duplicateIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var hardware in storageHardware)
            {
                string normalizedSourceName = NormalizeSourceName(hardware.Name);
                string sourceName = BuildUniqueSourceName(normalizedSourceName, duplicateCounts, duplicateIndexes);
                string sourceKey = SanitizeMetricKeyFragment(hardware.Identifier.ToString());

                var preferredTemperature = SelectStorageTemperatureSensor(hardware.Sensors);
                if (preferredTemperature?.Value.HasValue == true)
                {
                    metrics.Add(CreateStorageMetricValue(
                        "StorageTemp",
                        sourceKey,
                        sourceName,
                        $"{preferredTemperature.Value.Value:0}\u00B0C"));
                }

                var activity = FindPreferredSensor(
                    hardware.Sensors,
                    SensorType.Load,
                    "Total Activity",
                    "Activity");
                if (activity?.Value.HasValue == true)
                {
                    metrics.Add(CreateStorageMetricValue(
                        "StorageLoad",
                        sourceKey,
                        sourceName,
                        $"{activity.Value.Value:0}%"));
                }

                var read = FindPreferredSensor(
                    hardware.Sensors,
                    SensorType.Throughput,
                    "Read");
                if (read?.Value.HasValue == true)
                {
                    metrics.Add(CreateStorageMetricValue(
                        "StorageRead",
                        sourceKey,
                        sourceName,
                        FormatThroughput(read.Value.Value)));
                }

                var write = FindPreferredSensor(
                    hardware.Sensors,
                    SensorType.Throughput,
                    "Write");
                if (write?.Value.HasValue == true)
                {
                    metrics.Add(CreateStorageMetricValue(
                        "StorageWrite",
                        sourceKey,
                        sourceName,
                        FormatThroughput(write.Value.Value)));
                }
            }

            return metrics;
        }

        private static DashboardMetricValue CreateStorageMetricValue(string baseKey, string sourceKey, string sourceName, string value)
        {
            DashboardMetricDefinition definition = DashboardMetricCatalog.GetDefinitions()
                .First(metric => string.Equals(metric.Key, baseKey, StringComparison.OrdinalIgnoreCase));

            return new DashboardMetricValue
            {
                Key = $"{baseKey}::{sourceKey}",
                Label = definition.Label,
                Group = definition.Group,
                SourceName = sourceName,
                Value = value,
                DefaultVisible = definition.DefaultVisible
            };
        }

        private static bool IsStorageMetric(string key)
        {
            return StorageMetricKeys.Any(metricKey => string.Equals(metricKey, key, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeSourceName(string? sourceName)
        {
            return string.IsNullOrWhiteSpace(sourceName) ? "Storage Device" : sourceName.Trim();
        }

        private static string BuildUniqueSourceName(
            string sourceName,
            IReadOnlyDictionary<string, int> duplicateCounts,
            IDictionary<string, int> duplicateIndexes)
        {
            if (!duplicateCounts.TryGetValue(sourceName, out int count) || count <= 1)
            {
                return sourceName;
            }

            duplicateIndexes[sourceName] = duplicateIndexes.TryGetValue(sourceName, out int currentIndex)
                ? currentIndex + 1
                : 1;

            return $"{sourceName} #{duplicateIndexes[sourceName]}";
        }

        private static string SanitizeMetricKeyFragment(string value)
        {
            var chars = value
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray();

            return new string(chars);
        }

        private static ISensor? FindPreferredSensor(IEnumerable<ISensor> sensors, SensorType sensorType, params string[] preferredTerms)
        {
            var candidates = sensors
                .Where(sensor => sensor.SensorType == sensorType && sensor.Value.HasValue)
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            foreach (string term in preferredTerms)
            {
                var match = candidates.FirstOrDefault(sensor => ContainsIgnoreCase(sensor.Name, term));
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static ISensor? SelectStorageTemperatureSensor(IEnumerable<ISensor> sensors)
        {
            return sensors
                .Where(sensor => sensor.SensorType == SensorType.Temperature &&
                                 sensor.Value.HasValue &&
                                 !IsStorageTemperatureThresholdSensor(sensor.Name))
                .OrderBy(sensor => GetStorageTemperaturePriority(sensor.Name))
                .ThenBy(sensor => sensor.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStorageTemperatureThresholdSensor(string? sensorName)
        {
            if (string.IsNullOrWhiteSpace(sensorName))
            {
                return false;
            }

            return ContainsIgnoreCase(sensorName, "Critical") ||
                   ContainsIgnoreCase(sensorName, "Warning") ||
                   ContainsIgnoreCase(sensorName, "Limit") ||
                   ContainsIgnoreCase(sensorName, "Maximum") ||
                   ContainsIgnoreCase(sensorName, "Max");
        }

        private static int GetStorageTemperaturePriority(string? sensorName)
        {
            if (string.IsNullOrWhiteSpace(sensorName))
            {
                return 100;
            }

            if (ContainsIgnoreCase(sensorName, "Composite"))
            {
                return 0;
            }

            if (ContainsIgnoreCase(sensorName, "Temperature #1"))
            {
                return 1;
            }

            if (ContainsIgnoreCase(sensorName, "Temperature #2"))
            {
                return 2;
            }

            if (ContainsIgnoreCase(sensorName, "Temperature #"))
            {
                return 10;
            }

            if (ContainsIgnoreCase(sensorName, "Temperature"))
            {
                return 20;
            }

            return 50;
        }

        private static string FormatThroughput(double bytesPerSecond)
        {
            const double kilobyte = 1024d;
            const double megabyte = kilobyte * 1024d;
            const double gigabyte = megabyte * 1024d;

            if (bytesPerSecond >= gigabyte)
            {
                return $"{bytesPerSecond / gigabyte:0.##} GB/s";
            }

            if (bytesPerSecond >= megabyte)
            {
                return $"{bytesPerSecond / megabyte:0.##} MB/s";
            }

            if (bytesPerSecond >= kilobyte)
            {
                return $"{bytesPerSecond / kilobyte:0.##} KB/s";
            }

            return $"{bytesPerSecond:0} B/s";
        }
    }
}
