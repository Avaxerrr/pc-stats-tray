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
                    ["CpuClock"] = "5200 MHz"
                });

            Assert.AreEqual(3, snapshot.Metrics.Count);

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
        }
    }
}
