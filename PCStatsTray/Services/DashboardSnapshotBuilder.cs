using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
{
    internal static class DashboardSnapshotBuilder
    {
        public static DashboardSnapshot Build(Computer computer, OverlayConfig config)
        {
            return Build(config, OverlayMetricCollector.Collect(computer, config));
        }

        internal static DashboardSnapshot Build(OverlayConfig config, IReadOnlyDictionary<string, string> currentValues)
        {
            var metrics = DashboardMetricCatalog.GetDefinitions()
                .Where(metric => currentValues.ContainsKey(metric.Key))
                .Select(metric => new DashboardMetricSnapshot
                {
                    Key = metric.Key,
                    Label = metric.Label,
                    Group = metric.Group,
                    Value = currentValues.TryGetValue(metric.Key, out string? value) ? value : null,
                    Available = true,
                    DefaultVisible = metric.DefaultVisible
                })
                .ToList();

            return new DashboardSnapshot
            {
                GeneratedAtUtc = System.DateTimeOffset.UtcNow,
                MachineName = System.Environment.MachineName,
                RefreshIntervalMs = 2500,
                Metrics = metrics
            };
        }
    }
}
