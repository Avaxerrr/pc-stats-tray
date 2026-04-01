using System.Collections.Generic;

namespace PCStatsTray
{
    internal sealed class DashboardMetricDefinition
    {
        public DashboardMetricDefinition(string key, string label, string group, bool defaultVisible)
        {
            Key = key;
            Label = label;
            Group = group;
            DefaultVisible = defaultVisible;
        }

        public string Key { get; }
        public string Label { get; }
        public string Group { get; }
        public bool DefaultVisible { get; }
    }

    internal static class DashboardMetricCatalog
    {
        private static readonly IReadOnlyList<DashboardMetricDefinition> Definitions = new List<DashboardMetricDefinition>
        {
            new("CpuTemp", "CPU Temp", "CPU", true),
            new("CpuLoad", "CPU Load", "CPU", true),
            new("CpuClock", "CPU Clock", "CPU", false),
            new("CpuPower", "CPU Power", "CPU", true),
            new("CpuFan", "CPU Fan", "CPU", false),
            new("GpuTemp", "GPU Temp", "GPU", true),
            new("GpuLoad", "GPU Load", "GPU", true),
            new("GpuClock", "GPU Clock", "GPU", false),
            new("GpuVram", "GPU VRAM", "GPU", true),
            new("GpuPower", "GPU Power", "GPU", true),
            new("GpuFan", "GPU Fan", "GPU", false),
            new("RamUsage", "RAM Usage", "System", true),
            new("CaseFan", "Case Fan", "System", false)
        };

        public static IReadOnlyList<DashboardMetricDefinition> GetDefinitions()
        {
            return Definitions;
        }
    }
}
