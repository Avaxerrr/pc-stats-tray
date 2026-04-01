using System;
using System.Collections.Generic;

namespace PCStatsTray
{
    internal sealed class DashboardSnapshot
    {
        public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
        public string MachineName { get; init; } = Environment.MachineName;
        public int RefreshIntervalMs { get; init; } = 1000;
        public List<DashboardMetricSnapshot> Metrics { get; init; } = new();
    }

    internal sealed class DashboardMetricSnapshot
    {
        public string Key { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public string? Value { get; init; }
        public bool Available { get; init; }
        public bool DefaultVisible { get; init; }
    }

    internal sealed class DashboardApiResponse
    {
        public DateTimeOffset GeneratedAtUtc { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public int RefreshIntervalMs { get; init; }
        public string LocalUrl { get; init; } = string.Empty;
        public string? LanUrl { get; init; }
        public List<DashboardMetricSnapshot> Metrics { get; init; } = new();
    }
}
