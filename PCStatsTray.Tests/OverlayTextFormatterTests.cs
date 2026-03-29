using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class OverlayTextFormatterTests
    {
        [TestMethod]
        public void BuildOsdText_UsesMetricOrder_AndFallbackPlaceholder()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("CpuTemp", "CPU Temp", true),
                    new("GpuTemp", "GPU Temp", true),
                    new("GpuFan", "GPU Fan", false),
                }
            };

            var values = new Dictionary<string, string>
            {
                ["CpuTemp"] = "60°C"
            };

            string text = OverlayTextFormatter.BuildOsdText(config, values);

            Assert.AreEqual("CPU Temp: 60°C" + Environment.NewLine + "GPU Temp: --", text);
        }

        [TestMethod]
        public void GetVisibleMetrics_SkipsDisabledMetrics()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("CpuTemp", "CPU Temp", true),
                    new("GpuTemp", "GPU Temp", false)
                }
            };

            var metrics = OverlayTextFormatter.GetVisibleMetrics(config, new Dictionary<string, string>());

            Assert.AreEqual(1, metrics.Count);
            Assert.AreEqual("CpuTemp", metrics[0].Key);
        }
    }
}
