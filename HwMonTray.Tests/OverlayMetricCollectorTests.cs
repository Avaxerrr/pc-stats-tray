using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HwMonTray.Tests
{
    [TestClass]
    public class OverlayMetricCollectorTests
    {
        [TestMethod]
        public void FormatRamUsage_ReturnsPercentage_WhenRequested()
        {
            string value = OverlayMetricCollector.FormatRamUsage(12f, 4f, showPercentage: true);

            Assert.AreEqual("75%", value);
        }

        [TestMethod]
        public void FormatRamUsage_ReturnsUsedAndTotal_WhenPercentageDisabled()
        {
            string value = OverlayMetricCollector.FormatRamUsage(12f, 4f, showPercentage: false);

            Assert.AreEqual("12 / 16 GB", value);
        }
    }
}
