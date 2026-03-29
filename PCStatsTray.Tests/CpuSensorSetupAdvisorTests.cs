using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class CpuSensorSetupAdvisorTests
    {
        [TestMethod]
        public void ShouldRecommendPawnIo_WhenOnlyCpuLoadIsAvailableAndPawnIoIsMissing()
        {
            var status = new CpuSensorSetupStatus
            {
                HasCpuHardware = true,
                HasCpuLoad = true,
                HasCpuTemperature = false,
                HasCpuClock = false,
                HasCpuPower = false,
                IsPawnIoInstalled = false
            };

            Assert.IsTrue(status.ShouldRecommendPawnIo);
        }

        [TestMethod]
        public void ShouldNotRecommendPawnIo_WhenAdvancedCpuSensorIsAvailable()
        {
            var status = new CpuSensorSetupStatus
            {
                HasCpuHardware = true,
                HasCpuLoad = true,
                HasCpuTemperature = true,
                HasCpuClock = false,
                HasCpuPower = false,
                IsPawnIoInstalled = false
            };

            Assert.IsFalse(status.ShouldRecommendPawnIo);
        }

        [TestMethod]
        public void BuildPromptMessage_IncludesOfficialSiteAndDismissOptionWhenAllowed()
        {
            var status = new CpuSensorSetupStatus
            {
                HasCpuHardware = true,
                HasCpuLoad = true,
                IsPawnIoInstalled = false
            };

            string message = CpuSensorSetupAdvisor.BuildPromptMessage(status, allowSuppress: true);

            StringAssert.Contains(message, "https://pawnio.eu/");
            StringAssert.Contains(message, "Cancel = do not show this reminder again");
        }
    }
}
