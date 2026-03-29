using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HwMonTray.Tests
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

            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuFan" && metric.Enabled));
            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuTemp" && !metric.Enabled));
        }
    }
}
