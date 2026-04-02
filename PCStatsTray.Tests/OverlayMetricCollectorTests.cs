using System.Linq;
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

        [TestMethod]
        public void IsStorageTemperatureThresholdSensor_DetectsThresholdStyleNames()
        {
            Assert.IsTrue(OverlayMetricCollector.IsStorageTemperatureThresholdSensor("Critical Temperature"));
            Assert.IsTrue(OverlayMetricCollector.IsStorageTemperatureThresholdSensor("Warning Temperature"));
            Assert.IsTrue(OverlayMetricCollector.IsStorageTemperatureThresholdSensor("Temperature Limit"));
            Assert.IsFalse(OverlayMetricCollector.IsStorageTemperatureThresholdSensor("Composite temperature"));
            Assert.IsFalse(OverlayMetricCollector.IsStorageTemperatureThresholdSensor("Temperature #1"));
        }

        [TestMethod]
        public void GetStorageTemperaturePriority_PrefersCompositeBeforeOtherLiveTemperatures()
        {
            Assert.AreEqual(0, OverlayMetricCollector.GetStorageTemperaturePriority("Composite temperature"));
            Assert.AreEqual(1, OverlayMetricCollector.GetStorageTemperaturePriority("Temperature #1"));
            Assert.AreEqual(2, OverlayMetricCollector.GetStorageTemperaturePriority("Temperature #2"));
            Assert.AreEqual(10, OverlayMetricCollector.GetStorageTemperaturePriority("Temperature #4"));
            Assert.AreEqual(20, OverlayMetricCollector.GetStorageTemperaturePriority("Drive Temperature"));
            Assert.AreEqual(50, OverlayMetricCollector.GetStorageTemperaturePriority("Controller Sensor"));
        }

        [TestMethod]
        public void DefaultMetrics_ContainsAllDashboardCatalogKeys()
        {
            var catalogKeys = DashboardMetricCatalog.GetDefinitions().Select(d => d.Key).ToHashSet();
            var defaultKeys = OverlayConfig.DefaultMetrics().Select(m => m.Key).ToHashSet();

            var missing = catalogKeys.Except(defaultKeys).OrderBy(k => k).ToList();
            Assert.AreEqual(0, missing.Count,
                $"Catalog keys missing from DefaultMetrics: {string.Join(", ", missing)}");
        }

        [TestMethod]
        public void DefaultMetrics_ContainsNewGpuMetrics()
        {
            var keys = OverlayConfig.DefaultMetrics().Select(m => m.Key).ToHashSet();

            Assert.IsTrue(keys.Contains("GpuHotspotTemp"), "GpuHotspotTemp should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("GpuMemoryTemp"),  "GpuMemoryTemp should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("GpuMemoryClock"), "GpuMemoryClock should be in DefaultMetrics");
        }

        [TestMethod]
        public void DefaultMetrics_ContainsNetworkStorageBatteryMetrics()
        {
            var keys = OverlayConfig.DefaultMetrics().Select(m => m.Key).ToHashSet();

            Assert.IsTrue(keys.Contains("NetworkDownload"), "NetworkDownload should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("NetworkUpload"),   "NetworkUpload should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("StorageTemp"),     "StorageTemp should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("StorageLoad"),     "StorageLoad should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("StorageRead"),     "StorageRead should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("StorageWrite"),    "StorageWrite should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("BatteryLevel"),    "BatteryLevel should be in DefaultMetrics");
            Assert.IsTrue(keys.Contains("BatteryPower"),    "BatteryPower should be in DefaultMetrics");
        }

        [TestMethod]
        public void DefaultMetrics_NewMetricsAreDisabledByDefault()
        {
            var newKeys = new[]
            {
                "GpuHotspotTemp", "GpuMemoryTemp", "GpuMemoryClock",
                "RamLoad", "RamAvailable",
                "NetworkDownload", "NetworkUpload",
                "StorageTemp", "StorageLoad", "StorageRead", "StorageWrite",
                "BatteryLevel", "BatteryPower"
            };

            var defaultMetrics = OverlayConfig.DefaultMetrics()
                .ToDictionary(m => m.Key);

            foreach (var key in newKeys)
            {
                Assert.IsTrue(defaultMetrics.ContainsKey(key), $"{key} should be in DefaultMetrics");
                Assert.IsFalse(defaultMetrics[key].Enabled, $"{key} should be disabled by default");
            }
        }
    }
}
