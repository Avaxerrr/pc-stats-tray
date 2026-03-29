using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HwMonTray.Tests
{
    [TestClass]
    public class OverlaySettingsOutputStateBuilderTests
    {
        [TestMethod]
        public void Build_ReturnsOffMessage_WhenOverlayDisabled()
        {
            var state = OverlaySettingsOutputStateBuilder.Build(enabled: false, desktopEnabled: true, rtssEnabled: true, snapshot: null);

            Assert.AreEqual(OverlaySettingsOutputTone.Warning, state.Tone);
            StringAssert.Contains(state.Text, "OSD is currently off.");
        }

        [TestMethod]
        public void Build_ReturnsHealthyRtssStatus_WhenRtssIsReady()
        {
            var snapshot = new RtssStatusSnapshot
            {
                IsProcessRunning = true,
                HasSharedMemory = true,
                IsSlotOwned = true,
                Status = "RTSS ready"
            };

            var state = OverlaySettingsOutputStateBuilder.Build(enabled: true, desktopEnabled: false, rtssEnabled: true, snapshot: snapshot);

            Assert.AreEqual(OverlaySettingsOutputTone.Success, state.Tone);
            Assert.AreEqual("RTSS ready", state.Text);
        }
    }
}
