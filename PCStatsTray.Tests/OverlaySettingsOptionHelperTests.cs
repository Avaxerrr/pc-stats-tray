using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class OverlaySettingsOptionHelperTests
    {
        [TestMethod]
        public void BuildFanSensorItems_AddsMissingSensor_WhenSelectionIsUnavailable()
        {
            var items = OverlaySettingsOptionHelper.BuildFanSensorItems(
                new[]
                {
                    new FanSensorOption("cpu", "CPU Fan"),
                    new FanSensorOption("gpu", "GPU Fan")
                },
                "missing");

            Assert.AreEqual(string.Empty, items[0].Key);
            Assert.IsTrue(items.Any(item => item.Key == "missing" && item.Display == "Missing sensor"));
        }

        [TestMethod]
        public void BuildMetricSourceItems_AddsMissingSource_WhenSelectionIsUnavailable()
        {
            var items = OverlaySettingsOptionHelper.BuildMetricSourceItems(
                new[]
                {
                    new MetricSourceOption("drive0", "Samsung SSD"),
                    new MetricSourceOption("drive1", "WD Black")
                },
                "missing",
                "Aggregate",
                "Missing source");

            Assert.AreEqual(string.Empty, items[0].Key);
            Assert.AreEqual("Aggregate", items[0].Display);
            Assert.IsTrue(items.Any(item => item.Key == "missing" && item.Display == "Missing source"));
        }

        [TestMethod]
        public void GetMetricGroupLabel_ReturnsExpectedGroups()
        {
            Assert.AreEqual("CPU", OverlaySettingsOptionHelper.GetMetricGroupLabel("CpuTemp"));
            Assert.AreEqual("GPU", OverlaySettingsOptionHelper.GetMetricGroupLabel("GpuTemp"));
            Assert.AreEqual("Storage", OverlaySettingsOptionHelper.GetMetricGroupLabel("StorageTemp"));
            Assert.AreEqual("Network", OverlaySettingsOptionHelper.GetMetricGroupLabel("NetworkDownload"));
            Assert.AreEqual("Power", OverlaySettingsOptionHelper.GetMetricGroupLabel("BatteryLevel"));
            Assert.AreEqual("System", OverlaySettingsOptionHelper.GetMetricGroupLabel("RamUsage"));
        }
    }
}
