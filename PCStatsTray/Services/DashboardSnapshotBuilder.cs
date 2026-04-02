using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
{
    internal sealed class PhysicalMemoryModuleInfo
    {
        public string PartNumber { get; init; } = string.Empty;
        public string Manufacturer { get; init; } = string.Empty;
        public ulong CapacityBytes { get; init; }
        public uint? ConfiguredClockSpeedMHz { get; init; }
    }

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
        private const string DefaultRamSourceName = "System Memory";
        private static readonly object RamSourceCacheLock = new();
        private static readonly TimeSpan RamSourceCacheDuration = TimeSpan.FromMinutes(15);
        private static string? s_cachedRamSourceName;
        private static DateTimeOffset s_cachedRamSourceNameExpiresUtc = DateTimeOffset.MinValue;

        private static readonly string[] DeviceSpecificMetricKeys =
        {
            "StorageTemp",
            "StorageLoad",
            "StorageRead",
            "StorageWrite",
            "NetworkDownload",
            "NetworkUpload"
        };

        public static DashboardSnapshot Build(Computer computer, OverlayConfig config, int refreshIntervalMs)
        {
            var currentValues = OverlayMetricCollector.Collect(computer, config, useOverlayDisplayModes: false);
            var runtimeSourceNames = BuildRuntimeSourceNames(computer);
            var metrics = BuildStaticMetricValues(currentValues, includeDeviceSpecificMetrics: false, runtimeSourceNames);
            metrics.AddRange(BuildStorageMetricValues(computer));
            metrics.AddRange(BuildNetworkMetricValues(computer));
            return Build(metrics, refreshIntervalMs);
        }

        internal static DashboardSnapshot Build(OverlayConfig config, IReadOnlyDictionary<string, string> currentValues, int refreshIntervalMs)
        {
            return Build(BuildStaticMetricValues(currentValues, includeDeviceSpecificMetrics: true), refreshIntervalMs);
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
            bool includeDeviceSpecificMetrics,
            IReadOnlyDictionary<string, string>? sourceNameByKey = null)
        {
            return DashboardMetricCatalog.GetDefinitions()
                .Where(metric => currentValues.ContainsKey(metric.Key))
                .Where(metric => includeDeviceSpecificMetrics || !IsDeviceSpecificMetric(metric.Key))
                .Select(metric => new DashboardMetricValue
                {
                    Key = metric.Key,
                    Label = metric.Label,
                    Group = metric.Group,
                    SourceName = sourceNameByKey != null && sourceNameByKey.TryGetValue(metric.Key, out string? sourceName)
                        ? sourceName
                        : string.Empty,
                    Value = currentValues.TryGetValue(metric.Key, out string? value) ? value : null,
                    DefaultVisible = metric.DefaultVisible
                })
                .ToList();
        }

        private static IReadOnlyDictionary<string, string> BuildRuntimeSourceNames(Computer computer)
        {
            var sourceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IHardware? ramHardware = null;

            foreach (var hardware in computer.Hardware)
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        AssignCpuMetricSources(sourceNames, hardware);
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        AssignGpuMetricSources(sourceNames, hardware);
                        break;

                    case HardwareType.Battery:
                        AssignBatteryMetricSources(sourceNames, hardware);
                        break;

                    case HardwareType.Memory:
                        ramHardware ??= hardware;
                        break;
                }
            }

            if (ramHardware != null)
            {
                AssignRamMetricSources(sourceNames, ramHardware);
            }

            return sourceNames;
        }

        private static List<DashboardMetricValue> BuildStorageMetricValues(Computer computer)
        {
            var metrics = new List<DashboardMetricValue>();
            var storageHardware = computer.Hardware
                .Where(hardware => hardware.HardwareType == HardwareType.Storage)
                .ToList();

            var duplicateCounts = storageHardware
                .GroupBy(hardware => NormalizeSourceName(hardware.Name, "Storage Device"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var duplicateIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var hardware in storageHardware)
            {
                string normalizedSourceName = NormalizeSourceName(hardware.Name, "Storage Device");
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

        private static List<DashboardMetricValue> BuildNetworkMetricValues(Computer computer)
        {
            var metrics = new List<DashboardMetricValue>();
            var networkHardware = computer.Hardware
                .Where(hardware => hardware.HardwareType == HardwareType.Network)
                .ToList();

            var duplicateCounts = networkHardware
                .GroupBy(hardware => NormalizeSourceName(hardware.Name, "Network Adapter"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var duplicateIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var hardware in networkHardware)
            {
                string normalizedSourceName = NormalizeSourceName(hardware.Name, "Network Adapter");
                string sourceName = BuildUniqueSourceName(normalizedSourceName, duplicateCounts, duplicateIndexes);
                string sourceKey = SanitizeMetricKeyFragment(hardware.Identifier.ToString());

                var download = SelectNetworkSensor(
                    hardware.Sensors,
                    "Download",
                    "Receive",
                    "Received");
                if (download?.Value.HasValue == true)
                {
                    metrics.Add(CreateDeviceMetricValue(
                        "NetworkDownload",
                        sourceKey,
                        sourceName,
                        FormatThroughput(download.Value.Value),
                        IsPreferredNetworkSource(sourceName)));
                }

                var upload = SelectNetworkSensor(
                    hardware.Sensors,
                    "Upload",
                    "Transmit",
                    "Sent");
                if (upload?.Value.HasValue == true)
                {
                    metrics.Add(CreateDeviceMetricValue(
                        "NetworkUpload",
                        sourceKey,
                        sourceName,
                        FormatThroughput(upload.Value.Value),
                        IsPreferredNetworkSource(sourceName)));
                }
            }

            return metrics;
        }

        private static DashboardMetricValue CreateStorageMetricValue(string baseKey, string sourceKey, string sourceName, string value)
        {
            return CreateDeviceMetricValue(baseKey, sourceKey, sourceName, value);
        }

        private static void AssignCpuMetricSources(IDictionary<string, string> sourceNames, IHardware hardware)
        {
            string sourceName = NormalizeSourceName(hardware.Name, "CPU");

            if (SelectCpuTemperatureSensor(hardware.Sensors)?.Value.HasValue == true)
            {
                sourceNames["CpuTemp"] = sourceName;
            }

            if (FindPreferredSensor(hardware.Sensors, SensorType.Load, "Total")?.Value.HasValue == true)
            {
                sourceNames["CpuLoad"] = sourceName;
            }

            if (hardware.Sensors.Any(sensor => sensor.SensorType == SensorType.Clock &&
                                               sensor.Value.HasValue &&
                                               IsPeakCpuClockSensor(sensor.Name)))
            {
                sourceNames["CpuClock"] = sourceName;
            }

            if (FindPreferredSensor(hardware.Sensors, SensorType.Clock, "Cores (Average)")?.Value.HasValue == true)
            {
                sourceNames["CpuClockAvg"] = sourceName;
            }

            if (FindPreferredSensor(hardware.Sensors, SensorType.Clock, "Cores (Average Effective)")?.Value.HasValue == true)
            {
                sourceNames["CpuClockEffectiveAvg"] = sourceName;
            }

            if (FindPreferredSensor(hardware.Sensors, SensorType.Power, "Package")?.Value.HasValue == true)
            {
                sourceNames["CpuPower"] = sourceName;
            }
        }

        private static void AssignGpuMetricSources(IDictionary<string, string> sourceNames, IHardware hardware)
        {
            string sourceName = NormalizeSourceName(hardware.Name, "GPU");
            var allSensors = hardware.Sensors
                .Concat(hardware.SubHardware.SelectMany(subHardware => subHardware.Sensors))
                .ToList();

            if (FindPreferredSensor(allSensors, SensorType.Temperature, "Core")?.Value.HasValue == true)
            {
                sourceNames["GpuTemp"] = sourceName;
            }

            if (FindPreferredSensor(allSensors, SensorType.Temperature, "Hot Spot", "Hotspot", "Junction")?.Value.HasValue == true)
            {
                sourceNames["GpuHotspotTemp"] = sourceName;
            }

            if (FindPreferredSensor(allSensors, SensorType.Temperature, "Memory")?.Value.HasValue == true)
            {
                sourceNames["GpuMemoryTemp"] = sourceName;
            }

            if (FindPreferredSensor(allSensors, SensorType.Load, "Core")?.Value.HasValue == true)
            {
                sourceNames["GpuLoad"] = sourceName;
            }

            if (FindPreferredSensor(allSensors, SensorType.Clock, "Core")?.Value.HasValue == true)
            {
                sourceNames["GpuClock"] = sourceName;
            }

            if (FindPreferredSensor(allSensors, SensorType.Clock, "Memory")?.Value.HasValue == true)
            {
                sourceNames["GpuMemoryClock"] = sourceName;
            }

            if (FindPreferredSensor(allSensors, SensorType.SmallData, "Memory Used", "GPU Memory Used")?.Value.HasValue == true)
            {
                sourceNames["GpuVram"] = sourceName;
            }

            if (SelectGpuPowerSensor(allSensors)?.Value.HasValue == true)
            {
                sourceNames["GpuPower"] = sourceName;
            }
        }

        private static void AssignBatteryMetricSources(IDictionary<string, string> sourceNames, IHardware hardware)
        {
            string sourceName = NormalizeSourceName(hardware.Name, "Battery");

            var level = FindPreferredSensor(hardware.Sensors, SensorType.Level, "Charge", "Level")
                ?? FindPreferredSensor(hardware.Sensors, SensorType.Load, "Charge", "Level");
            if (level?.Value.HasValue == true)
            {
                sourceNames["BatteryLevel"] = sourceName;
            }

            var power = FindPreferredSensor(hardware.Sensors, SensorType.Power, "Charge", "Discharge", "Rate")
                ?? hardware.Sensors.FirstOrDefault(sensor => sensor.SensorType == SensorType.Power && sensor.Value.HasValue);
            if (power?.Value.HasValue == true)
            {
                sourceNames["BatteryPower"] = sourceName;
            }
        }

        private static void AssignRamMetricSources(IDictionary<string, string> sourceNames, IHardware hardware)
        {
            string sourceName = ResolveRamSourceName(hardware);

            var used = FindPreferredSensor(hardware.Sensors, SensorType.Data, "Used");
            var available = FindPreferredSensor(hardware.Sensors, SensorType.Data, "Available");
            var load = FindPreferredSensor(hardware.Sensors, SensorType.Load, "Memory");

            if (used?.Value.HasValue == true)
            {
                sourceNames["RamUsage"] = sourceName;
            }

            if (available?.Value.HasValue == true)
            {
                sourceNames["RamAvailable"] = sourceName;
                sourceNames["RamLoad"] = sourceName;
                return;
            }

            if (load?.Value.HasValue == true)
            {
                sourceNames["RamLoad"] = sourceName;
            }
        }

        private static string ResolveRamSourceName(IHardware hardware)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            lock (RamSourceCacheLock)
            {
                if (!string.IsNullOrWhiteSpace(s_cachedRamSourceName) &&
                    s_cachedRamSourceNameExpiresUtc > now)
                {
                    return s_cachedRamSourceName!;
                }
            }

            string sourceName = BuildMemorySourceName(LoadPhysicalMemoryModules());
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = NormalizeSourceName(hardware.Name, DefaultRamSourceName);
            }

            lock (RamSourceCacheLock)
            {
                s_cachedRamSourceName = sourceName;
                s_cachedRamSourceNameExpiresUtc = now.Add(RamSourceCacheDuration);
            }

            return sourceName;
        }

        internal static string BuildMemorySourceName(IEnumerable<PhysicalMemoryModuleInfo> modules)
        {
            var moduleList = modules
                .Where(module => module != null)
                .ToList();
            if (moduleList.Count == 0)
            {
                return DefaultRamSourceName;
            }

            ulong totalCapacityBytes = moduleList.Aggregate<PhysicalMemoryModuleInfo, ulong>(0, (total, module) => total + module.CapacityBytes);
            string capacitySummary = FormatMemoryCapacity(totalCapacityBytes);

            var names = moduleList
                .Select(GetPreferredMemoryModuleName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                return moduleList.Count == 1
                    ? capacitySummary
                    : $"{capacitySummary} ({moduleList.Count} modules)";
            }

            if (names.Count == 1)
            {
                return moduleList.Count == 1
                    ? $"{capacitySummary} ({names[0]})"
                    : $"{capacitySummary} ({moduleList.Count}x {names[0]})";
            }

            string joinedNames = names.Count <= 2
                ? string.Join(" + ", names)
                : $"{moduleList.Count} modules";

            return $"{capacitySummary} ({joinedNames})";
        }

        private static IReadOnlyList<PhysicalMemoryModuleInfo> LoadPhysicalMemoryModules()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Manufacturer, PartNumber, Capacity, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                using ManagementObjectCollection results = searcher.Get();

                return results
                    .OfType<ManagementObject>()
                    .Select(result => new PhysicalMemoryModuleInfo
                    {
                        Manufacturer = NormalizeManagementString(result["Manufacturer"]),
                        PartNumber = NormalizeManagementString(result["PartNumber"]),
                        CapacityBytes = TryReadUInt64(result["Capacity"]),
                        ConfiguredClockSpeedMHz = TryReadUInt32(result["ConfiguredClockSpeed"])
                    })
                    .Where(module => module.CapacityBytes > 0 || !string.IsNullOrWhiteSpace(module.PartNumber))
                    .ToList();
            }
            catch
            {
                return Array.Empty<PhysicalMemoryModuleInfo>();
            }
        }

        private static DashboardMetricValue CreateDeviceMetricValue(string baseKey, string sourceKey, string sourceName, string value)
        {
            return CreateDeviceMetricValue(baseKey, sourceKey, sourceName, value, defaultVisibleOverride: null);
        }

        private static DashboardMetricValue CreateDeviceMetricValue(
            string baseKey,
            string sourceKey,
            string sourceName,
            string value,
            bool? defaultVisibleOverride)
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
                DefaultVisible = defaultVisibleOverride ?? definition.DefaultVisible
            };
        }

        private static bool IsDeviceSpecificMetric(string key)
        {
            return DeviceSpecificMetricKeys.Any(metricKey => string.Equals(metricKey, key, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeSourceName(string? sourceName, string fallbackName)
        {
            return string.IsNullOrWhiteSpace(sourceName) ? fallbackName : sourceName.Trim();
        }

        private static string GetPreferredMemoryModuleName(PhysicalMemoryModuleInfo module)
        {
            string partNumber = module.PartNumber.Trim();
            if (!string.IsNullOrWhiteSpace(partNumber))
            {
                return partNumber;
            }

            string manufacturer = module.Manufacturer.Trim();
            return string.IsNullOrWhiteSpace(manufacturer) ? string.Empty : manufacturer;
        }

        private static string FormatMemoryCapacity(ulong bytes)
        {
            if (bytes == 0)
            {
                return DefaultRamSourceName;
            }

            double gibibytes = bytes / (1024d * 1024d * 1024d);
            if (gibibytes >= 10d)
            {
                return $"{Math.Round(gibibytes):0} GB";
            }

            return $"{gibibytes:0.#} GB";
        }

        private static string NormalizeManagementString(object? value)
        {
            return value?.ToString()?.Trim() ?? string.Empty;
        }

        private static ulong TryReadUInt64(object? value)
        {
            try
            {
                return value == null ? 0UL : Convert.ToUInt64(value);
            }
            catch
            {
                return 0UL;
            }
        }

        private static uint? TryReadUInt32(object? value)
        {
            try
            {
                return value == null ? null : Convert.ToUInt32(value);
            }
            catch
            {
                return null;
            }
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

        private static ISensor? SelectCpuTemperatureSensor(IEnumerable<ISensor> sensors)
        {
            return FindPreferredSensor(
                       sensors,
                       SensorType.Temperature,
                       "Core Max",
                       "Package",
                       "Tctl",
                       "Tdie")
                   ?? sensors.FirstOrDefault(sensor => sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue);
        }

        private static ISensor? SelectNetworkSensor(IEnumerable<ISensor> sensors, params string[] preferredTerms)
        {
            return FindPreferredSensor(sensors, SensorType.Throughput, preferredTerms);
        }

        private static ISensor? SelectGpuPowerSensor(IEnumerable<ISensor> sensors)
        {
            return FindPreferredSensor(
                       sensors,
                       SensorType.Power,
                       "Board",
                       "Total",
                       "Package")
                   ?? sensors.FirstOrDefault(sensor => sensor.SensorType == SensorType.Power && sensor.Value.HasValue);
        }

        private static bool IsPreferredNetworkSource(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return false;
            }

            if (ContainsIgnoreCase(sourceName, "Kernel Debugger") ||
                ContainsIgnoreCase(sourceName, "QoS Packet Scheduler") ||
                ContainsIgnoreCase(sourceName, "WFP ") ||
                ContainsIgnoreCase(sourceName, "Filter Driver"))
            {
                return false;
            }

            if (sourceName.StartsWith("Local Area Connection*", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.Equals(sourceName, "Bluetooth Network Connection", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPeakCpuClockSensor(string sensorName)
        {
            if (!ContainsIgnoreCase(sensorName, "Core #"))
            {
                return false;
            }

            return !ContainsIgnoreCase(sensorName, "Effective");
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
