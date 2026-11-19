using System;
using System.Collections.Generic;

namespace PCStatsTray
{
    internal readonly record struct MetricReading(
        string DisplayValue, double NumericValue, string Unit, double? TotalValue = null)
    {
        public static MetricReading Scalar(float value, string unit, string format)
        {
            string separator = unit is "%" or "°C" ? string.Empty : " ";
            return new MetricReading(value.ToString(format) + separator + unit, value, unit);
        }
    }

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
        public string SourceName { get; init; } = string.Empty;
        public string? Value { get; init; }
        public double? NumericValue { get; init; }
        public string? Unit { get; init; }
        public double? TotalValue { get; init; }
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
