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
                    DesktopHotkeyDisplay = "Ctrl+Alt+Shift+F8",
                    RtssHotkeyDisplay = "Ctrl+Alt+Shift+F10",
                    VramDisplayMode = OverlayConfig.VramDisplayPercentage,
                    PhoneDashboardEnabled = true,
                    PhoneDashboardPort = 4588
                };

                AppConfigStore.SaveOverlayConfig(path, overlay);

                var hiddenSensors = AppConfigStore.LoadHiddenSensors(path);
                var reloadedOverlay = AppConfigStore.LoadOverlayConfig(path);

                CollectionAssert.AreEquivalent(new[] { "cpu/temp", "gpu/temp" }, hiddenSensors.ToArray());
                Assert.IsFalse(reloadedOverlay.Enabled);
                Assert.AreEqual("Bahnschrift", reloadedOverlay.FontFamily);
                Assert.AreEqual("Ctrl+Alt+Shift+F8", reloadedOverlay.DesktopHotkeyDisplay);
                Assert.AreEqual("Ctrl+Alt+Shift+F10", reloadedOverlay.RtssHotkeyDisplay);
                Assert.AreEqual(OverlayConfig.VramDisplayPercentage, reloadedOverlay.VramDisplayMode);
                Assert.IsTrue(reloadedOverlay.PhoneDashboardEnabled);
                Assert.AreEqual(4588, reloadedOverlay.PhoneDashboardPort);
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
            Assert.AreEqual("Ctrl+Alt+Shift+F12", overlay.SettingsHotkeyDisplay);
        }

        [TestMethod]
        public void LoadOverlayConfig_MigratesLegacyDefaultHotkeys()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

            try
            {
                File.WriteAllText(path,
                    """
                    {
                      "overlay": {
                        "hotkeyDisplay": "Ctrl+Shift+O",
                        "hotkeyModifiers": 6,
                        "hotkeyVk": 79,
                        "desktopHotkeyDisplay": "Ctrl+Shift+D",
                        "desktopHotkeyModifiers": 6,
                        "desktopHotkeyVk": 68,
                        "rtssHotkeyDisplay": "Ctrl+Shift+R",
                        "rtssHotkeyModifiers": 6,
                        "rtssHotkeyVk": 82,
                        "settingsHotkeyDisplay": "Ctrl+Shift+S",
                        "settingsHotkeyModifiers": 6,
                        "settingsHotkeyVk": 83
                      }
                    }
                    """);

                var overlay = AppConfigStore.LoadOverlayConfig(path);

                Assert.AreEqual("Ctrl+Alt+Shift+F7", overlay.HotkeyDisplay);
                Assert.AreEqual("Ctrl+Alt+Shift+F8", overlay.DesktopHotkeyDisplay);
                Assert.AreEqual("Ctrl+Alt+Shift+F10", overlay.RtssHotkeyDisplay);
                Assert.AreEqual("Ctrl+Alt+Shift+F12", overlay.SettingsHotkeyDisplay);
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

        [TestMethod]
        public void LoadOverlayConfig_NormalizesInvalidDashboardPort()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

            try
            {
                AppConfigStore.SaveOverlayConfig(path, new OverlayConfig
                {
                    PhoneDashboardEnabled = true,
                    PhoneDashboardPort = 99999
                });

                var overlay = AppConfigStore.LoadOverlayConfig(path);

                Assert.IsTrue(overlay.PhoneDashboardEnabled);
                Assert.AreEqual(4587, overlay.PhoneDashboardPort);
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
