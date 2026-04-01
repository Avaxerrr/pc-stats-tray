using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
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

        [TestMethod]
        public void FormatRamUsage_ReturnsUsedOnly_WhenAvailableMemoryIsMissing()
        {
            string value = OverlayMetricCollector.FormatRamUsage(12f, availableGb: null, showPercentage: true);

            Assert.AreEqual("12 GB", value);
        }

        [TestMethod]
        public void GetMemoryHardwarePriority_PrefersTotalMemoryOverVirtualMemory()
        {
            Assert.AreEqual(0, OverlayMetricCollector.GetMemoryHardwarePriority("Total Memory"));
            Assert.AreEqual(1, OverlayMetricCollector.GetMemoryHardwarePriority("Memory"));
            Assert.AreEqual(2, OverlayMetricCollector.GetMemoryHardwarePriority("Virtual Memory"));
        }

        [TestMethod]
        public void FormatVramUsage_ReturnsPercentage_WhenRequested()
        {
            string value = OverlayMetricCollector.FormatVramUsage(4096f, 8192f, showPercentage: true);

            Assert.AreEqual("50%", value);
        }

        [TestMethod]
        public void FormatVramUsage_ReturnsUsedAndTotal_WhenPercentageDisabled()
        {
            string value = OverlayMetricCollector.FormatVramUsage(4096f, 8192f, showPercentage: false);

            Assert.AreEqual("4 / 8 GB", value);
        }
    }
}