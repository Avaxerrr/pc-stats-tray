using System.Collections.Generic;
using System.Text;

namespace PCStatsTray
{
    internal readonly record struct OverlayMetricDisplay(string Label, string Value, string Key);

    internal static class OverlayTextFormatter
    {
        public static List<OverlayMetricDisplay> GetVisibleMetrics(OverlayConfig config, IReadOnlyDictionary<string, string> currentValues, OverlayDisplayTarget target)
        {
            var result = new List<OverlayMetricDisplay>();
            foreach (var metric in config.Metrics)
            {
                if (!metric.IsEnabledFor(target))
                {
                    continue;
                }

                string value = currentValues.TryGetValue(metric.Key, out var currentValue) ? currentValue : "--";
                result.Add(new OverlayMetricDisplay(metric.Label, value, metric.Key));
            }

            return result;
        }

        public static string BuildOsdText(OverlayConfig config, IReadOnlyDictionary<string, string> currentValues, OverlayDisplayTarget target)
        {
            var metrics = GetVisibleMetrics(config, currentValues, target);
            if (metrics.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var metric in metrics)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(metric.Label);
                builder.Append(": ");
                builder.Append(metric.Value);
            }

            return builder.ToString();
        }
    }
}
