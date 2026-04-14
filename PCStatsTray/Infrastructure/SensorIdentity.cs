using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
{
    internal sealed class FanSensorOption
    {
        public string Key { get; }
        public string Display { get; }

        public FanSensorOption(string key, string display)
        {
            Key = key;
            Display = display;
        }

        public override string ToString() => Display;
    }

    internal sealed class MetricSourceOption
    {
        public string Key { get; }
        public string Display { get; }

        public MetricSourceOption(string key, string display)
        {
            Key = key;
            Display = display;
        }

        public override string ToString() => Display;
    }

    internal static class SensorIdentity
    {
        public static string GetSensorKey(ISensor sensor)
        {
            return $"{sensor.Hardware.Identifier}/{sensor.SensorType}/{sensor.Name}";
        }

        public static string GetFanDisplayName(ISensor sensor)
        {
            return $"{sensor.Hardware.Name} / {sensor.Name}";
        }

        public static List<FanSensorOption> GetFanSensorOptions(Computer computer)
        {
            var result = new List<FanSensorOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var hardware in computer.Hardware)
            {
                CollectFanOptionsRecursive(hardware, result, seen);
            }

            return result
                .OrderBy(option => option.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<MetricSourceOption> GetStorageSourceOptions(Computer computer)
        {
            return GetHardwareSourceOptions(computer, HardwareType.Storage, "Storage Device");
        }

        public static List<MetricSourceOption> GetNetworkSourceOptions(Computer computer)
        {
            return GetHardwareSourceOptions(computer, HardwareType.Network, "Network Adapter");
        }

        private static void CollectFanOptionsRecursive(IHardware hardware, List<FanSensorOption> result, HashSet<string> seen)
        {
            foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Fan))
            {
                string key = GetSensorKey(sensor);
                if (seen.Add(key))
                {
                    result.Add(new FanSensorOption(key, GetFanDisplayName(sensor)));
                }
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectFanOptionsRecursive(subHardware, result, seen);
            }
        }

        private static List<MetricSourceOption> GetHardwareSourceOptions(Computer computer, HardwareType hardwareType, string fallbackName)
        {
            var hardwareItems = computer.Hardware
                .Where(hardware => hardware.HardwareType == hardwareType)
                .ToList();
            var duplicateCounts = hardwareItems
                .GroupBy(hardware => NormalizeSourceName(hardware.Name, fallbackName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var duplicateIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            return hardwareItems
                .Select(hardware =>
                {
                    string normalizedName = NormalizeSourceName(hardware.Name, fallbackName);
                    string display = BuildUniqueSourceName(normalizedName, duplicateCounts, duplicateIndexes);
                    return new MetricSourceOption(hardware.Identifier.ToString(), display);
                })
                .OrderBy(option => option.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeSourceName(string? sourceName, string fallbackName)
        {
            return string.IsNullOrWhiteSpace(sourceName) ? fallbackName : sourceName.Trim();
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
    }
}
