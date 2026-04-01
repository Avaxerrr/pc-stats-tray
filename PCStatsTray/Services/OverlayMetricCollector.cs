using System;
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
            float? hottestStorageTemp = null;
            float? busiestStorageLoad = null;
            double totalStorageReadBytes = 0;
            double totalStorageWriteBytes = 0;
            double totalNetworkDownloadBytes = 0;
            double totalNetworkUploadBytes = 0;
            float? batteryLevel = null;
            float? batteryPower = null;

            foreach (var hardware in computer.Hardware)
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        CollectCpuMetrics(hardware, currentValues);
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        CollectGpuMetrics(hardware, config, currentValues);
                        break;

                    case HardwareType.Memory:
                        CollectRamMetrics(hardware, config, currentValues);
                        break;

                    case HardwareType.Storage:
                        CollectStorageMetrics(hardware, ref hottestStorageTemp, ref busiestStorageLoad, ref totalStorageReadBytes, ref totalStorageWriteBytes);
                        break;

                    case HardwareType.Network:
                        CollectNetworkMetrics(hardware, ref totalNetworkDownloadBytes, ref totalNetworkUploadBytes);
                        break;

                    case HardwareType.Battery:
                        CollectBatteryMetrics(hardware, ref batteryLevel, ref batteryPower);
                        break;
                }
            }

            if (hottestStorageTemp.HasValue)
            {
                currentValues["StorageTemp"] = $"{hottestStorageTemp.Value:0}°C";
            }

            if (busiestStorageLoad.HasValue)
            {
                currentValues["StorageLoad"] = $"{busiestStorageLoad.Value:0}%";
            }

            if (totalStorageReadBytes > 0)
            {
                currentValues["StorageRead"] = FormatThroughput(totalStorageReadBytes);
            }

            if (totalStorageWriteBytes > 0)
            {
                currentValues["StorageWrite"] = FormatThroughput(totalStorageWriteBytes);
            }

            if (totalNetworkDownloadBytes > 0)
            {
                currentValues["NetworkDownload"] = FormatThroughput(totalNetworkDownloadBytes);
            }

            if (totalNetworkUploadBytes > 0)
            {
                currentValues["NetworkUpload"] = FormatThroughput(totalNetworkUploadBytes);
            }

            if (batteryLevel.HasValue)
            {
                currentValues["BatteryLevel"] = $"{batteryLevel.Value:0}%";
            }

            if (batteryPower.HasValue)
            {
                currentValues["BatteryPower"] = $"{batteryPower.Value:0.#} W";
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
            var temp = FindSensor(hardware.Sensors,
                SensorType.Temperature,
                "Core Max",
                "Package");
            if (temp?.Value.HasValue == true)
            {
                currentValues["CpuTemp"] = $"{temp.Value.Value:0}°C";
            }

            var load = FindSensor(hardware.Sensors,
                SensorType.Load,
                "Total");
            if (load?.Value.HasValue == true)
            {
                currentValues["CpuLoad"] = $"{load.Value.Value:0}%";
            }

            var clocks = hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Clock &&
                                 sensor.Value.HasValue &&
                                 ContainsIgnoreCase(sensor.Name, "Core"))
                .ToList();
            if (clocks.Count > 0)
            {
                currentValues["CpuClock"] = $"{clocks.Max(sensor => sensor.Value!.Value):0} MHz";
                currentValues["CpuClockAvg"] = $"{clocks.Average(sensor => sensor.Value!.Value):0} MHz";
            }

            var power = FindSensor(hardware.Sensors,
                SensorType.Power,
                "Package");
            if (power?.Value.HasValue == true)
            {
                currentValues["CpuPower"] = $"{power.Value.Value:0.#} W";
            }
        }

        private static void CollectGpuMetrics(IHardware hardware, OverlayConfig config, IDictionary<string, string> currentValues)
        {
            var temp = FindSensor(hardware.Sensors,
                SensorType.Temperature,
                "Core");
            if (temp?.Value.HasValue == true)
            {
                currentValues["GpuTemp"] = $"{temp.Value.Value:0}°C";
            }

            var hotspotTemp = FindSensor(hardware.Sensors,
                SensorType.Temperature,
                "Hot Spot",
                "Hotspot",
                "Junction");
            if (hotspotTemp?.Value.HasValue == true)
            {
                currentValues["GpuHotspotTemp"] = $"{hotspotTemp.Value.Value:0}°C";
            }

            var memoryTemp = FindSensor(hardware.Sensors,
                SensorType.Temperature,
                "Memory");
            if (memoryTemp?.Value.HasValue == true)
            {
                currentValues["GpuMemoryTemp"] = $"{memoryTemp.Value.Value:0}°C";
            }

            var load = FindSensor(hardware.Sensors,
                SensorType.Load,
                "Core");
            if (load?.Value.HasValue == true)
            {
                currentValues["GpuLoad"] = $"{load.Value.Value:0}%";
            }

            var coreClock = FindSensor(hardware.Sensors,
                SensorType.Clock,
                "Core");
            if (coreClock?.Value.HasValue == true)
            {
                currentValues["GpuClock"] = $"{coreClock.Value.Value:0} MHz";
            }

            var memoryClock = FindSensor(hardware.Sensors,
                SensorType.Clock,
                "Memory");
            if (memoryClock?.Value.HasValue == true)
            {
                currentValues["GpuMemoryClock"] = $"{memoryClock.Value.Value:0} MHz";
            }

            var vramUsed = FindSensor(hardware.Sensors,
                SensorType.SmallData,
                "Memory Used",
                "GPU Memory Used");
            var vramTotal = FindSensor(hardware.Sensors,
                SensorType.SmallData,
                "Memory Total",
                "GPU Memory Total");
            if (vramUsed?.Value.HasValue == true)
            {
                float usedMb = vramUsed.Value.Value;
                float? totalMb = vramTotal?.Value.HasValue == true ? vramTotal.Value.Value : null;
                currentValues["GpuVram"] = FormatVramUsage(usedMb, totalMb, config.ShowVramAsPercentage());
            }

            var power = FindSensor(hardware.Sensors,
                SensorType.Power,
                "Board",
                "Total");
            if (power?.Value.HasValue == true)
            {
                currentValues["GpuPower"] = $"{power.Value.Value:0.#} W";
            }
        }

        private static void CollectRamMetrics(IHardware hardware, OverlayConfig config, IDictionary<string, string> currentValues)
        {
            var used = FindSensor(hardware.Sensors,
                SensorType.Data,
                "Used");
            var available = FindSensor(hardware.Sensors,
                SensorType.Data,
                "Available");
            if (used?.Value.HasValue != true)
            {
                return;
            }

            float usedGb = used.Value!.Value;
            float? availableGb = available?.Value.HasValue == true ? available.Value.Value : null;
            currentValues["RamUsage"] = FormatRamUsage(usedGb, availableGb, config.ShowRamAsPercentage());

            if (availableGb.HasValue)
            {
                currentValues["RamAvailable"] = $"{availableGb.Value:0.#} GB";
                float totalGb = usedGb + availableGb.Value;
                if (totalGb > 0)
                {
                    currentValues["RamLoad"] = $"{(usedGb / totalGb) * 100f:0}%";
                }
            }
            else
            {
                var load = FindSensor(hardware.Sensors,
                    SensorType.Load,
                    "Memory");
                if (load?.Value.HasValue == true)
                {
                    currentValues["RamLoad"] = $"{load.Value.Value:0}%";
                }
            }
        }

        private static void CollectStorageMetrics(
            IHardware hardware,
            ref float? hottestStorageTemp,
            ref float? busiestStorageLoad,
            ref double totalStorageReadBytes,
            ref double totalStorageWriteBytes)
        {
            var tempSensors = hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                .ToList();
            if (tempSensors.Count > 0)
            {
                float hottestOnDrive = tempSensors.Max(sensor => sensor.Value!.Value);
                hottestStorageTemp = !hottestStorageTemp.HasValue
                    ? hottestOnDrive
                    : Math.Max(hottestStorageTemp.Value, hottestOnDrive);
            }

            var activity = FindSensor(hardware.Sensors,
                SensorType.Load,
                "Total Activity",
                "Activity");
            if (activity?.Value.HasValue == true)
            {
                busiestStorageLoad = !busiestStorageLoad.HasValue
                    ? activity.Value.Value
                    : Math.Max(busiestStorageLoad.Value, activity.Value.Value);
            }

            var read = FindSensor(hardware.Sensors,
                SensorType.Throughput,
                "Read");
            if (read?.Value.HasValue == true)
            {
                totalStorageReadBytes += read.Value.Value;
            }

            var write = FindSensor(hardware.Sensors,
                SensorType.Throughput,
                "Write");
            if (write?.Value.HasValue == true)
            {
                totalStorageWriteBytes += write.Value.Value;
            }
        }

        private static void CollectNetworkMetrics(
            IHardware hardware,
            ref double totalNetworkDownloadBytes,
            ref double totalNetworkUploadBytes)
        {
            foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Throughput && sensor.Value.HasValue))
            {
                if (ContainsIgnoreCase(sensor.Name, "Download") ||
                    ContainsIgnoreCase(sensor.Name, "Receive") ||
                    ContainsIgnoreCase(sensor.Name, "Received"))
                {
                    totalNetworkDownloadBytes += sensor.Value!.Value;
                    continue;
                }

                if (ContainsIgnoreCase(sensor.Name, "Upload") ||
                    ContainsIgnoreCase(sensor.Name, "Transmit") ||
                    ContainsIgnoreCase(sensor.Name, "Sent"))
                {
                    totalNetworkUploadBytes += sensor.Value!.Value;
                }
            }
        }

        private static void CollectBatteryMetrics(IHardware hardware, ref float? batteryLevel, ref float? batteryPower)
        {
            var level = FindSensor(hardware.Sensors,
                SensorType.Level,
                "Charge",
                "Level")
                ?? FindSensor(hardware.Sensors,
                    SensorType.Load,
                    "Charge",
                    "Level");
            if (level?.Value.HasValue == true)
            {
                batteryLevel = level.Value.Value;
            }

            var power = FindSensor(hardware.Sensors,
                SensorType.Power,
                "Charge",
                "Discharge",
                "Rate")
                ?? hardware.Sensors.FirstOrDefault(sensor => sensor.SensorType == SensorType.Power && sensor.Value.HasValue);
            if (power?.Value.HasValue == true)
            {
                batteryPower = Math.Abs(power.Value.Value);
            }
        }

        private static ISensor? FindSensor(IEnumerable<ISensor> sensors, SensorType sensorType, params string[] preferredTerms)
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

            return candidates.FirstOrDefault();
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
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
