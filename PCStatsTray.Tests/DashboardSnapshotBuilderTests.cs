using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class DashboardSnapshotBuilderTests
    {
        [TestMethod]
        public void Build_UsesDashboardCatalogInsteadOfOverlaySelection()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("CpuTemp", "CPU Temp", true),
                    new("GpuLoad", "GPU Load", false)
                }
            };

            var snapshot = DashboardSnapshotBuilder.Build(
                config,
                new Dictionary<string, string>
                {
                    ["CpuTemp"] = "72\u00B0C",
                    ["GpuLoad"] = "91%",
                    ["CpuClock"] = "5200 MHz",
                    ["CpuClockEffectiveAvg"] = "4100 MHz"
                },
                1000);

            Assert.AreEqual(4, snapshot.Metrics.Count);
            Assert.AreEqual(1000, snapshot.RefreshIntervalMs);

            var cpuTemp = snapshot.Metrics.Single(metric => metric.Key == "CpuTemp");
            Assert.AreEqual("CPU", cpuTemp.Group);
            Assert.AreEqual("72\u00B0C", cpuTemp.Value);
            Assert.AreEqual(string.Empty, cpuTemp.SourceName);
            Assert.IsTrue(cpuTemp.Available);
            Assert.IsTrue(cpuTemp.DefaultVisible);

            var gpuLoad = snapshot.Metrics.Single(metric => metric.Key == "GpuLoad");
            Assert.AreEqual("GPU", gpuLoad.Group);
            Assert.AreEqual("91%", gpuLoad.Value);
            Assert.IsTrue(gpuLoad.Available);
            Assert.IsTrue(gpuLoad.DefaultVisible);

            var cpuClock = snapshot.Metrics.Single(metric => metric.Key == "CpuClock");
            Assert.IsFalse(cpuClock.DefaultVisible);

            var effectiveClock = snapshot.Metrics.Single(metric => metric.Key == "CpuClockEffectiveAvg");
            Assert.AreEqual("CPU", effectiveClock.Group);
            Assert.AreEqual("4100 MHz", effectiveClock.Value);
            Assert.IsFalse(effectiveClock.DefaultVisible);
        }

        [TestMethod]
        public void Build_PreservesSourceNamesForDeviceSpecificCards()
        {
            var snapshot = DashboardSnapshotBuilder.Build(
                new[]
                {
                    new DashboardMetricValue
                    {
                        Key = "StorageTemp::drive0",
                        Label = "Storage Temp",
                        Group = "Storage",
                        SourceName = "Samsung SSD 990 PRO",
                        Value = "56\u00B0C",
                        DefaultVisible = true
                    },
                    new DashboardMetricValue
                    {
                        Key = "StorageTemp::drive1",
                        Label = "Storage Temp",
                        Group = "Storage",
                        SourceName = "Samsung SSD 990 PRO #2",
                        Value = "49\u00B0C",
                        DefaultVisible = true
                    }
                },
                1000);

            Assert.AreEqual(2, snapshot.Metrics.Count);

            var firstDrive = snapshot.Metrics.Single(metric => metric.Key == "StorageTemp::drive0");
            Assert.AreEqual("Storage", firstDrive.Group);
            Assert.AreEqual("Samsung SSD 990 PRO", firstDrive.SourceName);
            Assert.AreEqual("56\u00B0C", firstDrive.Value);
            Assert.IsTrue(firstDrive.DefaultVisible);

            var secondDrive = snapshot.Metrics.Single(metric => metric.Key == "StorageTemp::drive1");
            Assert.AreEqual("Samsung SSD 990 PRO #2", secondDrive.SourceName);
        }
    }
}
