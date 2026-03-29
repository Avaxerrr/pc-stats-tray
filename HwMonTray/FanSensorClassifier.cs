using System;
using LibreHardwareMonitor.Hardware;

namespace HwMonTray
{
    internal enum FanRole
    {
        Unknown,
        Cpu,
        Gpu,
        Case
    }

    internal static class FanSensorClassifier
    {
        public static FanRole Classify(HardwareType hardwareType, string? sensorName, string? hardwareName, string? parentName)
        {
            string safeSensorName = sensorName ?? string.Empty;
            string combined = $"{safeSensorName} {hardwareName ?? string.Empty} {parentName ?? string.Empty}".ToLowerInvariant();

            if (hardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel ||
                combined.Contains("gpu") || combined.Contains("geforce") || combined.Contains("radeon"))
            {
                return FanRole.Gpu;
            }

            if (safeSensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                safeSensorName.Contains("Processor", StringComparison.OrdinalIgnoreCase) ||
                hardwareType == HardwareType.Cpu)
            {
                return FanRole.Cpu;
            }

            if (combined.Contains("chassis") ||
                combined.Contains("case") ||
                combined.Contains("system fan") ||
                combined.Contains("sys fan") ||
                combined.Contains("aux fan") ||
                combined.Contains("rear fan") ||
                combined.Contains("front fan"))
            {
                return FanRole.Case;
            }

            if (hardwareType is HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController)
            {
                return FanRole.Case;
            }

            return FanRole.Unknown;
        }
    }
}
