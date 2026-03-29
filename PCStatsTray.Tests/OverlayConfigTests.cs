using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class OverlayConfigTests
    {
        [TestMethod]
        public void NormalizeMetrics_MigratesLegacyFanSpeed_ToCpuFan()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("FanSpeed", "Fan Speed", true),
                    new("CpuTemp", "CPU Temp", false)
                }
            };

            config.NormalizeMetrics();

            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuFan" && metric.IsEnabledFor(OverlayDisplayTarget.Desktop)));
            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuFan" && metric.IsEnabledFor(OverlayDisplayTarget.Rtss)));
            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuTemp" && !metric.IsEnabledFor(OverlayDisplayTarget.Desktop)));
        }

        [TestMethod]
        public void NormalizeMetrics_MigratesLegacyEnabledState_ToBothTargets()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new()
                    {
                        Key = "GpuTemp",
                        Label = "GPU Temp",
                        Enabled = false
                    }
                }
            };

            config.NormalizeMetrics();

            var metric = config.Metrics.Single(entry => entry.Key == "GpuTemp");
            Assert.IsFalse(metric.IsEnabledFor(OverlayDisplayTarget.Desktop));
            Assert.IsFalse(metric.IsEnabledFor(OverlayDisplayTarget.Rtss));
        }

        [TestMethod]
        public void ShowVramAsPercentage_ReturnsTrue_WhenConfigured()
        {
            var config = new OverlayConfig
            {
                VramDisplayMode = OverlayConfig.VramDisplayPercentage
            };

            Assert.IsTrue(config.ShowVramAsPercentage());
        }
    }
}
