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
            return CollectReadings(computer, config, useOverlayDisplayModes: true)
                .ToDictionary(pair => pair.Key, pair => pair.Value.DisplayValue);
        }

        internal static Dictionary<string, MetricReading> CollectReadings(
            Computer computer, OverlayConfig config, bool useOverlayDisplayModes = false)
        {
            var currentValues = new Dictionary<string, MetricReading>();
            string selectedStorageSourceKey = config.StorageSourceKey?.Trim() ?? string.Empty;
            string selectedNetworkSourceKey = config.NetworkSourceKey?.Trim() ?? string.Empty;
            float? hottestStorageTemp = null;
            float? busiestStorageLoad = null;
            double totalStorageReadBytes = 0;
            double totalStorageWriteBytes = 0;
            double totalNetworkDownloadBytes = 0;
            double totalNetworkUploadBytes = 0;
            bool sawStorageRead = false;
            bool sawStorageWrite = false;
            bool sawNetworkDownload = false;
            bool sawNetworkUpload = false;
            bool matchedSelectedStorageSource = false;
            bool matchedSelectedNetworkSource = false;
            float? selectedStorageTemp = null;
            float? selectedStorageLoad = null;
            double selectedStorageReadBytes = 0;
            double selectedStorageWriteBytes = 0;
            bool sawSelectedStorageRead = false;
            bool sawSelectedStorageWrite = false;
            double selectedNetworkDownloadBytes = 0;
            double selectedNetworkUploadBytes = 0;
            bool sawSelectedNetworkDownload = false;
            bool sawSelectedNetworkUpload = false;
            float? batteryLevel = null;
            float? batteryPower = null;
            IHardware? ramHardware = SelectRamHardware(computer.Hardware);

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
                        break;

                    case HardwareType.Storage:
                        CollectStorageMetrics(
                            hardware,
                            ref hottestStorageTemp,
                            ref busiestStorageLoad,
                            ref totalStorageReadBytes,
                            ref totalStorageWriteBytes,
                            ref sawStorageRead,
                            ref sawStorageWrite);
                        if (IsSelectedMetricSource(hardware, selectedStorageSourceKey))
                        {
                            matchedSelectedStorageSource = true;
                            CollectSelectedStorageMetrics(
                                hardware,
                                ref selectedStorageTemp,
                                ref selectedStorageLoad,
                                ref selectedStorageReadBytes,
                                ref selectedStorageWriteBytes,
                                ref sawSelectedStorageRead,
                                ref sawSelectedStorageWrite);
                        }
                        break;

                    case HardwareType.Network:
                        CollectNetworkMetrics(
                            hardware,
                            ref totalNetworkDownloadBytes,
                            ref totalNetworkUploadBytes,
                            ref sawNetworkDownload,
                            ref sawNetworkUpload);
                        if (IsSelectedMetricSource(hardware, selectedNetworkSourceKey))
                        {
                            matchedSelectedNetworkSource = true;
                            CollectSelectedNetworkMetrics(
                                hardware,
                                ref selectedNetworkDownloadBytes,
                                ref selectedNetworkUploadBytes,
                                ref sawSelectedNetworkDownload,
                                ref sawSelectedNetworkUpload);
                        }
                        break;

                    case HardwareType.Battery:
                        CollectBatteryMetrics(hardware, ref batteryLevel, ref batteryPower);
                        break;
                }
            }

            if (ramHardware != null)
            {
                CollectRamMetrics(ramHardware, config, currentValues, useOverlayDisplayModes);
            }

            if (matchedSelectedStorageSource && selectedStorageTemp.HasValue)
            {
                currentValues["StorageTemp"] = MetricReading.Scalar(selectedStorageTemp.Value, "°C", "0");
            }
            else if (hottestStorageTemp.HasValue)
            {
                currentValues["StorageTemp"] = MetricReading.Scalar(hottestStorageTemp.Value, "°C", "0");
            }

            if (matchedSelectedStorageSource && selectedStorageLoad.HasValue)
            {
                currentValues["StorageLoad"] = MetricReading.Scalar(selectedStorageLoad.Value, "%", "0");
            }
            else if (busiestStorageLoad.HasValue)
            {
                currentValues["StorageLoad"] = MetricReading.Scalar(busiestStorageLoad.Value, "%", "0");
            }

            if (matchedSelectedStorageSource && sawSelectedStorageRead)
            {
                currentValues["StorageRead"] = ReadThroughput(selectedStorageReadBytes);
            }
            else if (sawStorageRead)
            {
                currentValues["StorageRead"] = ReadThroughput(totalStorageReadBytes);
            }

            if (matchedSelectedStorageSource && sawSelectedStorageWrite)
            {
                currentValues["StorageWrite"] = ReadThroughput(selectedStorageWriteBytes);
            }
            else if (sawStorageWrite)
            {
                currentValues["StorageWrite"] = ReadThroughput(totalStorageWriteBytes);
            }

            if (matchedSelectedNetworkSource && sawSelectedNetworkDownload)
            {
                currentValues["NetworkDownload"] = ReadThroughput(selectedNetworkDownloadBytes);
            }
            else if (sawNetworkDownload)
            {
                currentValues["NetworkDownload"] = ReadThroughput(totalNetworkDownloadBytes);
            }

            if (matchedSelectedNetworkSource && sawSelectedNetworkUpload)
            {
                currentValues["NetworkUpload"] = ReadThroughput(selectedNetworkUploadBytes);
            }
            else if (sawNetworkUpload)
            {
                currentValues["NetworkUpload"] = ReadThroughput(totalNetworkUploadBytes);
            }

            if (batteryLevel.HasValue)
            {
                currentValues["BatteryLevel"] = MetricReading.Scalar(batteryLevel.Value, "%", "0");
            }

            if (batteryPower.HasValue)
            {
                currentValues["BatteryPower"] = MetricReading.Scalar(batteryPower.Value, "W", "0.#");
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

        private static void CollectCpuMetrics(IHardware hardware, IDictionary<string, MetricReading> currentValues)
        {
            var temp = SelectCpuTemperatureSensor(hardware.Sensors);
            if (temp?.Value.HasValue == true)
            {
                currentValues["CpuTemp"] = MetricReading.Scalar(temp.Value.Value, "°C", "0");
            }

            var load = FindPreferredSensor(hardware.Sensors,
                SensorType.Load,
                "Total");
            if (load?.Value.HasValue == true)
            {
                currentValues["CpuLoad"] = MetricReading.Scalar(load.Value.Value, "%", "0");
            }

            var peakClocks = hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Clock &&
                                 sensor.Value.HasValue &&
                                 IsPeakCpuClockSensor(sensor.Name))
                .ToList();
            if (peakClocks.Count > 0)
            {
                currentValues["CpuClock"] = MetricReading.Scalar(peakClocks.Max(sensor => sensor.Value!.Value), "MHz", "0");
            }

            var averageClock = FindPreferredSensor(hardware.Sensors,
                SensorType.Clock,
                "Cores (Average)");
            if (averageClock?.Value.HasValue == true)
            {
                currentValues["CpuClockAvg"] = MetricReading.Scalar(averageClock.Value.Value, "MHz", "0");
            }

            var effectiveAverageClock = FindPreferredSensor(hardware.Sensors,
                SensorType.Clock,
                "Cores (Average Effective)");
            if (effectiveAverageClock?.Value.HasValue == true)
            {
                currentValues["CpuClockEffectiveAvg"] = MetricReading.Scalar(effectiveAverageClock.Value.Value, "MHz", "0");
            }

            var power = FindPreferredSensor(hardware.Sensors,
                SensorType.Power,
                "Package");
            if (power?.Value.HasValue == true)
            {
                currentValues["CpuPower"] = MetricReading.Scalar(power.Value.Value, "W", "0.#");
            }
        }

        internal static void ApplyRamOverlayFormatting(Computer computer, OverlayConfig config, IDictionary<string, string> values)
        {
            if (!config.ShowRamAsPercentage()) return;
            var ram = SelectRamHardware(computer.Hardware);
            if (ram == null) return;
            var used = FindPreferredSensor(ram.Sensors, SensorType.Data, "Used");
            var available = FindPreferredSensor(ram.Sensors, SensorType.Data, "Available");
            if (used?.Value.HasValue != true) return;
            float usedGb = used.Value!.Value;
            float? availableGb = available?.Value.HasValue == true ? available.Value.Value : (float?)null;
            values["RamUsage"] = FormatRamUsage(usedGb, availableGb, showPercentage: true);
        }

        private static void CollectGpuMetrics(IHardware hardware, OverlayConfig config, IDictionary<string, MetricReading> currentValues)
        {
            var allSensors = hardware.Sensors
                .Concat(hardware.SubHardware.SelectMany(sub => sub.Sensors))
                .ToList();

            var temp = FindPreferredSensor(allSensors,
                SensorType.Temperature,
                "Core");
            if (temp?.Value.HasValue == true)
            {
                currentValues["GpuTemp"] = MetricReading.Scalar(temp.Value.Value, "°C", "0");
            }

            var hotspotTemp = FindPreferredSensor(allSensors,
                SensorType.Temperature,
                "Hot Spot",
                "Hotspot",
                "Junction");
            if (hotspotTemp?.Value.HasValue == true)
            {
                currentValues["GpuHotspotTemp"] = MetricReading.Scalar(hotspotTemp.Value.Value, "°C", "0");
            }

            var memoryTemp = FindPreferredSensor(allSensors,
                SensorType.Temperature,
                "Memory");
            if (memoryTemp?.Value.HasValue == true)
            {
                currentValues["GpuMemoryTemp"] = MetricReading.Scalar(memoryTemp.Value.Value, "°C", "0");
            }

            var load = FindPreferredSensor(allSensors,
                SensorType.Load,
                "Core");
            if (load?.Value.HasValue == true)
            {
                currentValues["GpuLoad"] = MetricReading.Scalar(load.Value.Value, "%", "0");
            }

            var coreClock = FindPreferredSensor(allSensors,
                SensorType.Clock,
                "Core");
            if (coreClock?.Value.HasValue == true)
            {
                currentValues["GpuClock"] = MetricReading.Scalar(coreClock.Value.Value, "MHz", "0");
            }

            var memoryClock = FindPreferredSensor(allSensors,
                SensorType.Clock,
                "Memory");
            if (memoryClock?.Value.HasValue == true)
            {
                currentValues["GpuMemoryClock"] = MetricReading.Scalar(memoryClock.Value.Value, "MHz", "0");
            }

            var vramUsed = FindPreferredSensor(allSensors,
                SensorType.SmallData,
                "Memory Used",
                "GPU Memory Used");
            var vramTotal = FindPreferredSensor(allSensors,
                SensorType.SmallData,
                "Memory Total",
                "GPU Memory Total");
            if (vramUsed?.Value.HasValue == true)
            {
                float usedMb = vramUsed.Value.Value;
                float? totalMb = vramTotal?.Value.HasValue == true ? vramTotal.Value.Value : null;
                currentValues["GpuVram"] = new MetricReading(
                    FormatVramUsage(usedMb, totalMb, config.ShowVramAsPercentage()),
                    usedMb / 1024f, "GB", totalMb / 1024f);
            }

            var power = SelectGpuPowerSensor(allSensors);
            if (power?.Value.HasValue == true)
            {
                currentValues["GpuPower"] = MetricReading.Scalar(power.Value.Value, "W", "0.#");
            }
        }

        private static void CollectRamMetrics(IHardware hardware, OverlayConfig config, IDictionary<string, MetricReading> currentValues, bool useOverlayDisplayModes)
        {
            var used = FindPreferredSensor(hardware.Sensors,
                SensorType.Data,
                "Used");
            var available = FindPreferredSensor(hardware.Sensors,
                SensorType.Data,
                "Available");
            if (used?.Value.HasValue != true)
            {
                return;
            }

            float usedGb = used.Value!.Value;
            float? availableGb = available?.Value.HasValue == true ? available.Value.Value : null;
            bool showRamAsPercentage = useOverlayDisplayModes && config.ShowRamAsPercentage();
            currentValues["RamUsage"] = new MetricReading(
                FormatRamUsage(usedGb, availableGb, showRamAsPercentage),
                usedGb, "GB", usedGb + availableGb);

            if (availableGb.HasValue)
            {
                currentValues["RamAvailable"] = MetricReading.Scalar(availableGb.Value, "GB", "0.#");
                float totalGb = usedGb + availableGb.Value;
                if (totalGb > 0)
                {
                    currentValues["RamLoad"] = MetricReading.Scalar((usedGb / totalGb) * 100f, "%", "0");
                }
            }
            else
            {
                var load = FindPreferredSensor(hardware.Sensors,
                    SensorType.Load,
                    "Memory");
                if (load?.Value.HasValue == true)
                {
                    currentValues["RamLoad"] = MetricReading.Scalar(load.Value.Value, "%", "0");
                }
            }
        }

        internal static IHardware? SelectRamHardware(IEnumerable<IHardware> hardwareItems)
        {
            return hardwareItems
                .Where(hardware => hardware.HardwareType == HardwareType.Memory)
                .OrderBy(hardware => GetMemoryHardwarePriority(hardware.Name))
                .FirstOrDefault();
        }

        internal static int GetMemoryHardwarePriority(string? hardwareName)
        {
            if (string.IsNullOrWhiteSpace(hardwareName))
            {
                return 1;
            }

            if (ContainsIgnoreCase(hardwareName, "Total") || ContainsIgnoreCase(hardwareName, "Physical"))
            {
                return 0;
            }

            if (ContainsIgnoreCase(hardwareName, "Virtual"))
            {
                return 2;
            }

            return 1;
        }

        private static void CollectStorageMetrics(
            IHardware hardware,
            ref float? hottestStorageTemp,
            ref float? busiestStorageLoad,
            ref double totalStorageReadBytes,
            ref double totalStorageWriteBytes,
            ref bool sawStorageRead,
            ref bool sawStorageWrite)
        {
            var preferredTemperature = SelectStorageTemperatureSensor(hardware.Sensors);
            if (preferredTemperature?.Value.HasValue == true)
            {
                float hottestOnDrive = preferredTemperature.Value.Value;
                hottestStorageTemp = !hottestStorageTemp.HasValue
                    ? hottestOnDrive
                    : Math.Max(hottestStorageTemp.Value, hottestOnDrive);
            }

            var activity = FindPreferredSensor(hardware.Sensors,
                SensorType.Load,
                "Total Activity",
                "Activity");
            if (activity?.Value.HasValue == true)
            {
                busiestStorageLoad = !busiestStorageLoad.HasValue
                    ? activity.Value.Value
                    : Math.Max(busiestStorageLoad.Value, activity.Value.Value);
            }

            var read = FindPreferredSensor(hardware.Sensors,
                SensorType.Throughput,
                "Read");
            if (read?.Value.HasValue == true)
            {
                sawStorageRead = true;
                totalStorageReadBytes += read.Value.Value;
            }

            var write = FindPreferredSensor(hardware.Sensors,
                SensorType.Throughput,
                "Write");
            if (write?.Value.HasValue == true)
            {
                sawStorageWrite = true;
                totalStorageWriteBytes += write.Value.Value;
            }
        }

        private static void CollectNetworkMetrics(
            IHardware hardware,
            ref double totalNetworkDownloadBytes,
            ref double totalNetworkUploadBytes,
            ref bool sawNetworkDownload,
            ref bool sawNetworkUpload)
        {
            foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Throughput && sensor.Value.HasValue))
            {
                if (ContainsIgnoreCase(sensor.Name, "Download") ||
                    ContainsIgnoreCase(sensor.Name, "Receive") ||
                    ContainsIgnoreCase(sensor.Name, "Received"))
                {
                    sawNetworkDownload = true;
                    totalNetworkDownloadBytes += sensor.Value!.Value;
                    continue;
                }

                if (ContainsIgnoreCase(sensor.Name, "Upload") ||
                    ContainsIgnoreCase(sensor.Name, "Transmit") ||
                    ContainsIgnoreCase(sensor.Name, "Sent"))
                {
                    sawNetworkUpload = true;
                    totalNetworkUploadBytes += sensor.Value!.Value;
                }
            }
        }

        private static void CollectSelectedStorageMetrics(
            IHardware hardware,
            ref float? storageTemp,
            ref float? storageLoad,
            ref double storageReadBytes,
            ref double storageWriteBytes,
            ref bool sawStorageRead,
            ref bool sawStorageWrite)
        {
            var preferredTemperature = SelectStorageTemperatureSensor(hardware.Sensors);
            if (preferredTemperature?.Value.HasValue == true)
            {
                storageTemp = preferredTemperature.Value.Value;
            }

            var activity = FindPreferredSensor(hardware.Sensors,
                SensorType.Load,
                "Total Activity",
                "Activity");
            if (activity?.Value.HasValue == true)
            {
                storageLoad = activity.Value.Value;
            }

            var read = FindPreferredSensor(hardware.Sensors,
                SensorType.Throughput,
                "Read");
            if (read?.Value.HasValue == true)
            {
                sawStorageRead = true;
                storageReadBytes = read.Value.Value;
            }

            var write = FindPreferredSensor(hardware.Sensors,
                SensorType.Throughput,
                "Write");
            if (write?.Value.HasValue == true)
            {
                sawStorageWrite = true;
                storageWriteBytes = write.Value.Value;
            }
        }

        private static void CollectSelectedNetworkMetrics(
            IHardware hardware,
            ref double networkDownloadBytes,
            ref double networkUploadBytes,
            ref bool sawNetworkDownload,
            ref bool sawNetworkUpload)
        {
            foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Throughput && sensor.Value.HasValue))
            {
                if (ContainsIgnoreCase(sensor.Name, "Download") ||
                    ContainsIgnoreCase(sensor.Name, "Receive") ||
                    ContainsIgnoreCase(sensor.Name, "Received"))
                {
                    sawNetworkDownload = true;
                    networkDownloadBytes += sensor.Value!.Value;
                    continue;
                }

                if (ContainsIgnoreCase(sensor.Name, "Upload") ||
                    ContainsIgnoreCase(sensor.Name, "Transmit") ||
                    ContainsIgnoreCase(sensor.Name, "Sent"))
                {
                    sawNetworkUpload = true;
                    networkUploadBytes += sensor.Value!.Value;
                }
            }
        }

        private static void CollectBatteryMetrics(IHardware hardware, ref float? batteryLevel, ref float? batteryPower)
        {
            var level = FindPreferredSensor(hardware.Sensors,
                SensorType.Level,
                "Charge",
                "Level")
                ?? FindPreferredSensor(hardware.Sensors,
                    SensorType.Load,
                    "Charge",
                    "Level");
            if (level?.Value.HasValue == true)
            {
                batteryLevel = level.Value.Value;
            }

            var power = FindPreferredSensor(hardware.Sensors,
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

        private static bool IsSelectedMetricSource(IHardware hardware, string selectedSourceKey)
        {
            return !string.IsNullOrWhiteSpace(selectedSourceKey) &&
                   string.Equals(hardware.Identifier.ToString(), selectedSourceKey, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPeakCpuClockSensor(string sensorName)
        {
            if (!ContainsIgnoreCase(sensorName, "Core #"))
            {
                return false;
            }

            return !ContainsIgnoreCase(sensorName, "Effective");
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

        internal static bool IsStorageTemperatureThresholdSensor(string? sensorName)
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

        internal static int GetStorageTemperaturePriority(string? sensorName)
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

        internal static MetricReading ReadThroughput(double bytesPerSecond) =>
            new(FormatThroughput(bytesPerSecond), bytesPerSecond, "B/s");

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
