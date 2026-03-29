using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HwMonTray.Tests
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
        public void GetMetricGroupLabel_ReturnsExpectedGroups()
        {
            Assert.AreEqual("CPU", OverlaySettingsOptionHelper.GetMetricGroupLabel("CpuTemp"));
            Assert.AreEqual("GPU", OverlaySettingsOptionHelper.GetMetricGroupLabel("GpuTemp"));
            Assert.AreEqual("System", OverlaySettingsOptionHelper.GetMetricGroupLabel("RamUsage"));
        }
    }
}
