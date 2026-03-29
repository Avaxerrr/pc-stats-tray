using System.Linq;
using System.Text;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace PCStatsTray
{
    internal sealed class CpuSensorSetupStatus
    {
        public bool HasCpuHardware { get; init; }
        public bool HasCpuLoad { get; init; }
        public bool HasCpuTemperature { get; init; }
        public bool HasCpuClock { get; init; }
        public bool HasCpuPower { get; init; }
        public bool IsPawnIoInstalled { get; init; }

        public bool HasAnyAdvancedCpuSensor => HasCpuTemperature || HasCpuClock || HasCpuPower;

        public bool ShouldRecommendPawnIo =>
            HasCpuHardware &&
            HasCpuLoad &&
            !HasAnyAdvancedCpuSensor &&
            !IsPawnIoInstalled;

        public string BuildStatusSummary()
        {
            if (!HasCpuHardware)
            {
                return "CPU hardware was not detected yet.";
            }

            var builder = new StringBuilder();
            builder.AppendLine("Current CPU sensor status:");
            builder.AppendLine($"- CPU load: {(HasCpuLoad ? "available" : "missing")}");
            builder.AppendLine($"- CPU temperature: {(HasCpuTemperature ? "available" : "missing")}");
            builder.AppendLine($"- CPU clock: {(HasCpuClock ? "available" : "missing")}");
            builder.AppendLine($"- CPU power: {(HasCpuPower ? "available" : "missing")}");
            builder.AppendLine($"- PawnIO installed: {(IsPawnIoInstalled ? "yes" : "no")}");
            return builder.ToString().TrimEnd();
        }
    }

    internal static class CpuSensorSetupAdvisor
    {
        public const string OfficialUrl = "https://pawnio.eu/";

        public static CpuSensorSetupStatus Evaluate(Computer computer)
        {
            bool isPawnIoInstalled = PawnIo.IsInstalled;
            var cpuHardware = computer.Hardware.FirstOrDefault(hw => hw.HardwareType == HardwareType.Cpu);
            if (cpuHardware == null)
            {
                return new CpuSensorSetupStatus
                {
                    IsPawnIoInstalled = isPawnIoInstalled
                };
            }

            bool hasCpuLoad = cpuHardware.Sensors.Any(s =>
                s.SensorType == SensorType.Load &&
                s.Value.HasValue &&
                s.Name.Contains("Total"));

            if (!hasCpuLoad)
            {
                hasCpuLoad = cpuHardware.Sensors.Any(s =>
                    s.SensorType == SensorType.Load &&
                    s.Value.HasValue);
            }

            return new CpuSensorSetupStatus
            {
                HasCpuHardware = true,
                HasCpuLoad = hasCpuLoad,
                HasCpuTemperature = cpuHardware.Sensors.Any(s =>
                    s.SensorType == SensorType.Temperature &&
                    s.Value.HasValue),
                HasCpuClock = cpuHardware.Sensors.Any(s =>
                    s.SensorType == SensorType.Clock &&
                    s.Value.HasValue &&
                    s.Name.Contains("Core")),
                HasCpuPower = cpuHardware.Sensors.Any(s =>
                    s.SensorType == SensorType.Power &&
                    s.Value.HasValue),
                IsPawnIoInstalled = isPawnIoInstalled
            };
        }

        public static string BuildPromptMessage(CpuSensorSetupStatus status, bool allowSuppress)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Full CPU sensors are unavailable right now.");
            builder.AppendLine();
            builder.AppendLine(status.BuildStatusSummary());
            builder.AppendLine();
            builder.AppendLine("PC Stats Tray can keep running without PawnIO, but CPU temperature, clock, and power may stay blank on some systems until PawnIO is installed.");
            builder.AppendLine("PawnIO is the official low-level hardware access backend used by LibreHardwareMonitor for systems like this.");
            builder.AppendLine($"Official site: {OfficialUrl}");
            builder.AppendLine();
            builder.AppendLine("Yes = open the official PawnIO site");
            builder.AppendLine("No = continue without installing it");

            if (allowSuppress)
            {
                builder.AppendLine("Cancel = do not show this reminder again");
            }

            return builder.ToString().TrimEnd();
        }

        public static string BuildManualHelpMessage(CpuSensorSetupStatus status)
        {
            var builder = new StringBuilder();
            builder.AppendLine(status.BuildStatusSummary());
            builder.AppendLine();
            builder.AppendLine("If CPU temperature, clock, or power stay blank on your system, PawnIO may be required for full CPU sensor access.");
            builder.AppendLine("PawnIO is the official backend used by LibreHardwareMonitor for this on supported systems.");
            builder.AppendLine($"Official site: {OfficialUrl}");
            builder.AppendLine();
            builder.AppendLine("Open the official PawnIO site now?");
            return builder.ToString().TrimEnd();
        }
    }
}
