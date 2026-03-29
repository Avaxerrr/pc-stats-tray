using System;
using System.Collections.Generic;
using System.Linq;

namespace PCStatsTray
{
    internal static class OverlaySettingsOptionHelper
    {
        public static List<FanSensorOption> BuildFanSensorItems(IEnumerable<FanSensorOption> availableOptions, string selectedKey)
        {
            var available = availableOptions.ToList();
            var result = new List<FanSensorOption>
            {
                new(string.Empty, "Auto detect")
            };

            result.AddRange(available);

            if (!string.IsNullOrWhiteSpace(selectedKey) &&
                !available.Any(option => string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new FanSensorOption(selectedKey, "Missing sensor"));
            }

            return result;
        }

        public static string GetSelectedFanSensorKey(object? selectedItem)
        {
            return selectedItem is FanSensorOption option ? option.Key : string.Empty;
        }

        public static string GetMetricGroupLabel(string key)
        {
            if (key.StartsWith("Cpu", StringComparison.OrdinalIgnoreCase))
            {
                return "CPU";
            }

            if (key.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
            {
                return "GPU";
            }

            return "System";
        }
    }
}
