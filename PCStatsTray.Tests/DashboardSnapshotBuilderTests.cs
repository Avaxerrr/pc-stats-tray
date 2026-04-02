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
                    ["CpuTemp"] = "72°C",
                    ["GpuLoad"] = "91%",
                    ["CpuClock"] = "5200 MHz",
                    ["CpuClockEffectiveAvg"] = "4100 MHz"
                },
                1000);

            Assert.AreEqual(4, snapshot.Metrics.Count);
            Assert.AreEqual(1000, snapshot.RefreshIntervalMs);

            var cpuTemp = snapshot.Metrics.Single(metric => metric.Key == "CpuTemp");
            Assert.AreEqual("CPU", cpuTemp.Group);
            Assert.AreEqual("72°C", cpuTemp.Value);
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
    }
}
