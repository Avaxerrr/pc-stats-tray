using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class DashboardSnapshotBuilderTests
    {
        [TestMethod]
        public void Build_UsesConfiguredMetricsAndMarksUnavailableValues()
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
                    ["CpuTemp"] = "72°C"
                });

            Assert.AreEqual(OverlayConfig.DefaultMetrics().Count, snapshot.Metrics.Count);

            var cpuTemp = snapshot.Metrics.Single(metric => metric.Key == "CpuTemp");
            Assert.AreEqual("CPU", cpuTemp.Group);
            Assert.AreEqual("72°C", cpuTemp.Value);
            Assert.IsTrue(cpuTemp.Available);
            Assert.IsTrue(cpuTemp.DefaultVisible);

            var gpuLoad = snapshot.Metrics.Single(metric => metric.Key == "GpuLoad");
            Assert.AreEqual("GPU", gpuLoad.Group);
            Assert.IsNull(gpuLoad.Value);
            Assert.IsFalse(gpuLoad.Available);
            Assert.IsFalse(gpuLoad.DefaultVisible);
        }
    }
}
