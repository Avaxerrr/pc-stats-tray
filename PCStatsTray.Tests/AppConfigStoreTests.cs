using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class AppConfigStoreTests
    {
        [TestMethod]
        public void SaveOverlayConfig_PreservesHiddenSensors()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

            try
            {
                AppConfigStore.SaveHiddenSensors(path, new[] { "cpu/temp", "gpu/temp" });

                var overlay = new OverlayConfig
                {
                    Enabled = false,
                    FontFamily = "Bahnschrift",
                    DesktopHotkeyDisplay = "Ctrl+Shift+D",
                    RtssHotkeyDisplay = "Ctrl+Shift+R",
                    VramDisplayMode = OverlayConfig.VramDisplayPercentage
                };

                AppConfigStore.SaveOverlayConfig(path, overlay);

                var hiddenSensors = AppConfigStore.LoadHiddenSensors(path);
                var reloadedOverlay = AppConfigStore.LoadOverlayConfig(path);

                CollectionAssert.AreEquivalent(new[] { "cpu/temp", "gpu/temp" }, hiddenSensors.ToArray());
                Assert.IsFalse(reloadedOverlay.Enabled);
                Assert.AreEqual("Bahnschrift", reloadedOverlay.FontFamily);
                Assert.AreEqual("Ctrl+Shift+D", reloadedOverlay.DesktopHotkeyDisplay);
                Assert.AreEqual("Ctrl+Shift+R", reloadedOverlay.RtssHotkeyDisplay);
                Assert.AreEqual(OverlayConfig.VramDisplayPercentage, reloadedOverlay.VramDisplayMode);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void LoadOverlayConfig_ReturnsNormalizedDefaults_WhenFileIsMissing()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

            var overlay = AppConfigStore.LoadOverlayConfig(path);

            Assert.IsTrue(overlay.Enabled);
            Assert.IsTrue(overlay.Metrics.Count > 0);
            Assert.AreEqual("CpuTemp", overlay.Metrics[0].Key);
        }

        [TestMethod]
        public void SaveSuppressPawnIoPrompt_PreservesOverlayAndHiddenSensors()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

            try
            {
                AppConfigStore.SaveHiddenSensors(path, new[] { "cpu/temp" });
                AppConfigStore.SaveOverlayConfig(path, new OverlayConfig
                {
                    Enabled = false,
                    FontFamily = "Consolas"
                });

                AppConfigStore.SaveSuppressPawnIoPrompt(path, true);

                var hiddenSensors = AppConfigStore.LoadHiddenSensors(path);
                var overlay = AppConfigStore.LoadOverlayConfig(path);
                bool suppressPrompt = AppConfigStore.LoadSuppressPawnIoPrompt(path);

                CollectionAssert.AreEquivalent(new[] { "cpu/temp" }, hiddenSensors.ToArray());
                Assert.IsFalse(overlay.Enabled);
                Assert.AreEqual("Consolas", overlay.FontFamily);
                Assert.IsTrue(suppressPrompt);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
