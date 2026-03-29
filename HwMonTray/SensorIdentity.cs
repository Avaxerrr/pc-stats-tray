using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace HwMonTray
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
    }
}
