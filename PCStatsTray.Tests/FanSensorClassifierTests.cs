using LibreHardwareMonitor.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class FanSensorClassifierTests
    {
        [TestMethod]
        public void Classify_ReturnsGpu_ForGpuHardware()
        {
            var result = FanSensorClassifier.Classify(HardwareType.GpuNvidia, "Fan", "RTX 3070", string.Empty);

            Assert.AreEqual(FanRole.Gpu, result);
        }

        [TestMethod]
        public void Classify_ReturnsCpu_ForCpuNamedSensor()
        {
            var result = FanSensorClassifier.Classify(HardwareType.Motherboard, "CPU Fan", "ASUS Board", string.Empty);

            Assert.AreEqual(FanRole.Cpu, result);
        }

        [TestMethod]
        public void Classify_ReturnsCase_ForMotherboardSystemFan()
        {
            var result = FanSensorClassifier.Classify(HardwareType.Motherboard, "System Fan 1", "ASUS Board", string.Empty);

            Assert.AreEqual(FanRole.Case, result);
        }
    }
}
