using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
{
    internal static class OverlayMetricCollector
    {
        public static Dictionary<string, string> Collect(Computer computer, OverlayConfig config)
        {
            var currentValues = new Dictionary<string, string>();

            foreach (var hardware in computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    CollectCpuMetrics(hardware, currentValues);
                }
                else if (hardware.HardwareType == HardwareType.GpuNvidia ||
                         hardware.HardwareType == HardwareType.GpuAmd ||
                         hardware.HardwareType == HardwareType.GpuIntel)
                {
                    CollectGpuMetrics(hardware, config, currentValues);
                }
                else if (hardware.HardwareType == HardwareType.Memory)
                {
                    CollectRamMetrics(hardware, config, currentValues);
                }
            }

            FanSensorResolver.PopulateFanMetrics(computer, config, currentValues);
            return currentValues;
        }

        internal static string FormatRamUsage(float usedGb, float? availableGb, bool showPercentage)
        {
            if (availableGb.HasValue)
            {
                float totalGb = usedGb + availableGb.Value;
                return FormatUsageInGigabytes(usedGb, totalGb, showPercentage);
            }

            return $"{usedGb:0.#} GB";
        }

        internal static string FormatVramUsage(float usedMb, float? totalMb, bool showPercentage)
        {
            float usedGb = usedMb / 1024f;
            if (totalMb.HasValue)
            {
                return FormatUsageInGigabytes(usedGb, totalMb.Value / 1024f, showPercentage);
            }

            return $"{usedGb:0.#} GB";
        }

        private static void CollectCpuMetrics(IHardware hardware, IDictionary<string, string> currentValues)
        {
            var temp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core Max"))
                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Package"))
                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp?.Value.HasValue == true)
            {
                currentValues["CpuTemp"] = $"{temp.Value.Value:0}\u00B0C";
            }

            var load = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"))
                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            if (load?.Value.HasValue == true)
            {
                currentValues["CpuLoad"] = $"{load.Value.Value:0}%";
            }

            var clocks = hardware.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue && s.Name.Contains("Core")).ToList();
            if (clocks.Count > 0)
            {
                currentValues["CpuClock"] = $"{clocks.Max(s => s.Value!.Value):0} MHz";
            }

            var power = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"))
                     ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
            if (power?.Value.HasValue == true)
            {
                currentValues["CpuPower"] = $"{power.Value.Value:0.#} W";
            }
        }

        private static void CollectGpuMetrics(IHardware hardware, OverlayConfig config, IDictionary<string, string> currentValues)
        {
            var temp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core"))
                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp?.Value.HasValue == true)
            {
                currentValues["GpuTemp"] = $"{temp.Value.Value:0}\u00B0C";
            }

            var load = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core"))
                    ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            if (load?.Value.HasValue == true)
            {
                currentValues["GpuLoad"] = $"{load.Value.Value:0}%";
            }

            var clock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                     ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
            if (clock?.Value.HasValue == true)
            {
                currentValues["GpuClock"] = $"{clock.Value.Value:0} MHz";
            }

            var vramUsed = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"))
                        ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("GPU Memory Used"));
            var vramTotal = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total"))
                         ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("GPU Memory Total"));
            if (vramUsed?.Value.HasValue == true)
            {
                float usedMb = vramUsed.Value.Value;
                float? totalMb = vramTotal?.Value.HasValue == true ? vramTotal.Value.Value : null;
                currentValues["GpuVram"] = FormatVramUsage(usedMb, totalMb, config.ShowVramAsPercentage());
            }

            var power = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
            if (power?.Value.HasValue == true)
            {
                currentValues["GpuPower"] = $"{power.Value.Value:0.#} W";
            }
        }

        private static void CollectRamMetrics(IHardware hardware, OverlayConfig config, IDictionary<string, string> currentValues)
        {
            var used = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Used"));
            var available = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Available"));
            if (used?.Value.HasValue != true)
            {
                return;
            }

            float usedGb = used.Value!.Value;
            float? availableGb = available?.Value.HasValue == true ? available.Value!.Value : null;
            currentValues["RamUsage"] = FormatRamUsage(usedGb, availableGb, config.ShowRamAsPercentage());
        }

        private static string FormatUsageInGigabytes(float usedGb, float totalGb, bool showPercentage)
        {
            if (showPercentage)
            {
                float usedPercent = totalGb > 0 ? (usedGb / totalGb) * 100f : 0f;
                return $"{usedPercent:0}%";
            }

            return $"{usedGb:0.#} / {totalGb:0.#} GB";
        }
    }
}
