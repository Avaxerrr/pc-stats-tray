using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
{
    internal static class GpuSensorDebugSnapshotBuilder
    {
        public static GpuDebugSnapshot Build(Computer computer)
        {
            computer.Accept(new UpdateVisitor());

            return new GpuDebugSnapshot
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                MachineName = Environment.MachineName,
                Gpus = computer.Hardware
                    .Where(hardware =>
                        hardware.HardwareType == HardwareType.GpuNvidia ||
                        hardware.HardwareType == HardwareType.GpuAmd ||
                        hardware.HardwareType == HardwareType.GpuIntel)
                    .Select(BuildGpuHardwareReport)
                    .ToList()
            };
        }

        private static GpuHardwareDebugReport BuildGpuHardwareReport(IHardware hardware)
        {
            var topLevelSensors = hardware.Sensors
                .Select(sensor => BuildSensorReport(sensor, hardware.Name, hardware.Identifier.ToString(), isSubHardware: false))
                .ToList();

            var subHardwareReports = hardware.SubHardware
                .Select(subHardware => new GpuSubHardwareDebugReport
                {
                    Name = subHardware.Name,
                    Identifier = subHardware.Identifier.ToString(),
                    HardwareType = subHardware.HardwareType.ToString(),
                    Sensors = subHardware.Sensors
                        .Select(sensor => BuildSensorReport(sensor, subHardware.Name, subHardware.Identifier.ToString(), isSubHardware: true))
                        .ToList()
                })
                .ToList();

            var allSensors = topLevelSensors
                .Concat(subHardwareReports.SelectMany(report => report.Sensors))
                .ToList();

            return new GpuHardwareDebugReport
            {
                Name = hardware.Name,
                Identifier = hardware.Identifier.ToString(),
                HardwareType = hardware.HardwareType.ToString(),
                SelectedGpuPowerSensor = SelectGpuPowerSensor(allSensors),
                PowerSensors = allSensors
                    .Where(sensor => string.Equals(sensor.SensorType, SensorType.Power.ToString(), StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                TopLevelSensors = topLevelSensors,
                SubHardware = subHardwareReports,
                AllSensors = allSensors
            };
        }

        private static GpuSensorDebugReport BuildSensorReport(ISensor sensor, string parentHardwareName, string parentHardwareIdentifier, bool isSubHardware)
        {
            return new GpuSensorDebugReport
            {
                Name = sensor.Name,
                Identifier = sensor.Identifier.ToString(),
                SensorType = sensor.SensorType.ToString(),
                Value = sensor.Value,
                Min = sensor.Min,
                Max = sensor.Max,
                ParentHardwareName = parentHardwareName,
                ParentHardwareIdentifier = parentHardwareIdentifier,
                IsSubHardware = isSubHardware,
                MatchesPreferredGpuPowerTerm =
                    sensor.SensorType == SensorType.Power &&
                    (ContainsIgnoreCase(sensor.Name, "Board") ||
                     ContainsIgnoreCase(sensor.Name, "Total") ||
                     ContainsIgnoreCase(sensor.Name, "Package"))
            };
        }

        private static GpuPowerSelectionDebugReport? SelectGpuPowerSensor(IReadOnlyList<GpuSensorDebugReport> sensors)
        {
            var powerSensors = sensors
                .Where(sensor => string.Equals(sensor.SensorType, SensorType.Power.ToString(), StringComparison.OrdinalIgnoreCase))
                .Where(sensor => sensor.Value.HasValue)
                .ToList();

            if (powerSensors.Count == 0)
            {
                return null;
            }

            foreach (string term in new[] { "Board", "Total", "Package" })
            {
                var match = powerSensors.FirstOrDefault(sensor => ContainsIgnoreCase(sensor.Name, term));
                if (match != null)
                {
                    return new GpuPowerSelectionDebugReport
                    {
                        SelectionMode = $"preferred term: {term}",
                        Sensor = match
                    };
                }
            }

            return new GpuPowerSelectionDebugReport
            {
                SelectionMode = "fallback: first power sensor with value",
                Sensor = powerSensors[0]
            };
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class GpuDebugSnapshot
    {
        public DateTimeOffset GeneratedAtUtc { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public List<GpuHardwareDebugReport> Gpus { get; init; } = new();
    }

    internal sealed class GpuHardwareDebugReport
    {
        public string Name { get; init; } = string.Empty;
        public string Identifier { get; init; } = string.Empty;
        public string HardwareType { get; init; } = string.Empty;
        public GpuPowerSelectionDebugReport? SelectedGpuPowerSensor { get; init; }
        public List<GpuSensorDebugReport> PowerSensors { get; init; } = new();
        public List<GpuSensorDebugReport> TopLevelSensors { get; init; } = new();
        public List<GpuSubHardwareDebugReport> SubHardware { get; init; } = new();
        public List<GpuSensorDebugReport> AllSensors { get; init; } = new();
    }

    internal sealed class GpuSubHardwareDebugReport
    {
        public string Name { get; init; } = string.Empty;
        public string Identifier { get; init; } = string.Empty;
        public string HardwareType { get; init; } = string.Empty;
        public List<GpuSensorDebugReport> Sensors { get; init; } = new();
    }

    internal sealed class GpuPowerSelectionDebugReport
    {
        public string SelectionMode { get; init; } = string.Empty;
        public GpuSensorDebugReport Sensor { get; init; } = new();
    }

    internal sealed class GpuSensorDebugReport
    {
        public string Name { get; init; } = string.Empty;
        public string Identifier { get; init; } = string.Empty;
        public string SensorType { get; init; } = string.Empty;
        public float? Value { get; init; }
        public float? Min { get; init; }
        public float? Max { get; init; }
        public string ParentHardwareName { get; init; } = string.Empty;
        public string ParentHardwareIdentifier { get; init; } = string.Empty;
        public bool IsSubHardware { get; init; }
        public bool MatchesPreferredGpuPowerTerm { get; init; }
    }
}
