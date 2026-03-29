using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class OverlayTextFormatterTests
    {
        [TestMethod]
        public void BuildOsdText_UsesMetricOrder_AndFallbackPlaceholder()
        {
            var gpuTemp = new OverlayMetric("GpuTemp", "GPU Temp", true);
            gpuTemp.SetEnabledStates(desktopEnabled: false, rtssEnabled: true);

            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("CpuTemp", "CPU Temp", true),
                    gpuTemp,
                    new("GpuFan", "GPU Fan", false),
                }
            };

            var values = new Dictionary<string, string>
            {
                ["CpuTemp"] = "60C"
            };

            string text = OverlayTextFormatter.BuildOsdText(config, values, OverlayDisplayTarget.Rtss);

            Assert.AreEqual("CPU Temp: 60C" + Environment.NewLine + "GPU Temp: --", text);
        }

        [TestMethod]
        public void GetVisibleMetrics_UsesTargetSpecificMetricState()
        {
            var gpuTemp = new OverlayMetric("GpuTemp", "GPU Temp", true);
            gpuTemp.SetEnabledStates(desktopEnabled: false, rtssEnabled: true);

            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("CpuTemp", "CPU Temp", true),
                    gpuTemp
                }
            };

            var desktopMetrics = OverlayTextFormatter.GetVisibleMetrics(config, new Dictionary<string, string>(), OverlayDisplayTarget.Desktop);
            var rtssMetrics = OverlayTextFormatter.GetVisibleMetrics(config, new Dictionary<string, string>(), OverlayDisplayTarget.Rtss);

            Assert.AreEqual(1, desktopMetrics.Count);
            Assert.AreEqual("CpuTemp", desktopMetrics[0].Key);
            Assert.AreEqual(2, rtssMetrics.Count);
            Assert.AreEqual("GpuTemp", rtssMetrics[1].Key);
        }
    }
}
