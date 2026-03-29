using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
{
    internal static class FanSensorResolver
    {
        public static void PopulateFanMetrics(Computer computer, OverlayConfig config, IDictionary<string, string> currentValues)
        {
            ISensor? cpuFan = null;
            ISensor? gpuFan = null;
            ISensor? caseFan = null;

            var fanSensors = new Dictionary<string, ISensor>(StringComparer.OrdinalIgnoreCase);
            foreach (var hardware in computer.Hardware)
            {
                CollectFanSensorsByKeyRecursive(hardware, fanSensors);
            }

            cpuFan = ResolveConfiguredFan(config.CpuFanSensorKey, fanSensors);
            gpuFan = ResolveConfiguredFan(config.GpuFanSensorKey, fanSensors);
            caseFan = ResolveConfiguredFan(config.CaseFanSensorKey, fanSensors);

            foreach (var hardware in computer.Hardware)
            {
                CollectFanMetricsRecursive(hardware, ref cpuFan, ref gpuFan, ref caseFan);
            }

            if (cpuFan?.Value.HasValue == true)
            {
                currentValues["CpuFan"] = $"{cpuFan.Value.Value:0} RPM";
            }

            if (gpuFan?.Value.HasValue == true)
            {
                currentValues["GpuFan"] = $"{gpuFan.Value.Value:0} RPM";
            }

            if (caseFan?.Value.HasValue == true)
            {
                currentValues["CaseFan"] = $"{caseFan.Value.Value:0} RPM";
            }
        }

        private static void CollectFanSensorsByKeyRecursive(IHardware hardware, Dictionary<string, ISensor> fanSensors)
        {
            foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Fan))
            {
                fanSensors[SensorIdentity.GetSensorKey(sensor)] = sensor;
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectFanSensorsByKeyRecursive(subHardware, fanSensors);
            }
        }

        private static ISensor? ResolveConfiguredFan(string sensorKey, Dictionary<string, ISensor> fanSensors)
        {
            if (string.IsNullOrWhiteSpace(sensorKey))
            {
                return null;
            }

            if (fanSensors.TryGetValue(sensorKey, out var sensor) && sensor.Value.HasValue)
            {
                return sensor;
            }

            return null;
        }

        private static void CollectFanMetricsRecursive(IHardware hardware, ref ISensor? cpuFan, ref ISensor? gpuFan, ref ISensor? caseFan)
        {
            foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue))
            {
                AssignFanSensor(hardware, sensor, ref cpuFan, ref gpuFan, ref caseFan);
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectFanMetricsRecursive(subHardware, ref cpuFan, ref gpuFan, ref caseFan);
            }
        }

        private static void AssignFanSensor(IHardware hardware, ISensor sensor, ref ISensor? cpuFan, ref ISensor? gpuFan, ref ISensor? caseFan)
        {
            FanRole role = FanSensorClassifier.Classify(hardware.HardwareType, sensor.Name, hardware.Name, hardware.Parent?.Name);
            switch (role)
            {
                case FanRole.Cpu when cpuFan == null:
                    cpuFan = sensor;
                    break;
                case FanRole.Gpu when gpuFan == null:
                    gpuFan = sensor;
                    break;
                case FanRole.Case when caseFan == null:
                    caseFan = sensor;
                    break;
            }
        }
    }
}
