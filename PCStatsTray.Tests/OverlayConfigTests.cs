using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class OverlayConfigTests
    {
        [TestMethod]
        public void NormalizeMetrics_MigratesLegacyFanSpeed_ToCpuFan()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("FanSpeed", "Fan Speed", true),
                    new("CpuTemp", "CPU Temp", false)
                }
            };

            config.NormalizeMetrics();

            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuFan" && metric.IsEnabledFor(OverlayDisplayTarget.Desktop)));
            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuFan" && metric.IsEnabledFor(OverlayDisplayTarget.Rtss)));
            Assert.IsTrue(config.Metrics.Any(metric => metric.Key == "CpuTemp" && !metric.IsEnabledFor(OverlayDisplayTarget.Desktop)));
        }

        [TestMethod]
        public void NormalizeMetrics_MigratesLegacyEnabledState_ToBothTargets()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new()
                    {
                        Key = "GpuTemp",
                        Label = "GPU Temp",
                        Enabled = false
                    }
                }
            };

            config.NormalizeMetrics();

            var metric = config.Metrics.Single(entry => entry.Key == "GpuTemp");
            Assert.IsFalse(metric.IsEnabledFor(OverlayDisplayTarget.Desktop));
            Assert.IsFalse(metric.IsEnabledFor(OverlayDisplayTarget.Rtss));
        }

        [TestMethod]
        public void ShowVramAsPercentage_ReturnsTrue_WhenConfigured()
        {
            var config = new OverlayConfig
            {
                VramDisplayMode = OverlayConfig.VramDisplayPercentage
            };

            Assert.IsTrue(config.ShowVramAsPercentage());
        }

        [TestMethod]
        public void NormalizeHotkeys_MigratesLegacyDefaultShortcuts()
        {
            var config = new OverlayConfig
            {
                HotkeyDisplay = "Ctrl+Shift+O",
                HotkeyModifiers = 0x0002 | 0x0004,
                HotkeyVk = 0x4F,
                DesktopHotkeyDisplay = "Ctrl+Shift+D",
                DesktopHotkeyModifiers = 0x0002 | 0x0004,
                DesktopHotkeyVk = 0x44,
                RtssHotkeyDisplay = "Ctrl+Shift+R",
                RtssHotkeyModifiers = 0x0002 | 0x0004,
                RtssHotkeyVk = 0x52,
                SettingsHotkeyDisplay = "Ctrl+Shift+S",
                SettingsHotkeyModifiers = 0x0002 | 0x0004,
                SettingsHotkeyVk = 0x53
            };

            config.NormalizeHotkeys();

            Assert.AreEqual("Ctrl+Alt+Shift+F7", config.HotkeyDisplay);
            Assert.AreEqual(0x0001 | 0x0002 | 0x0004, config.HotkeyModifiers);
            Assert.AreEqual(0x76, config.HotkeyVk);
            Assert.AreEqual("Ctrl+Alt+Shift+F8", config.DesktopHotkeyDisplay);
            Assert.AreEqual(0x77, config.DesktopHotkeyVk);
            Assert.AreEqual("Ctrl+Alt+Shift+F10", config.RtssHotkeyDisplay);
            Assert.AreEqual(0x79, config.RtssHotkeyVk);
            Assert.AreEqual("Ctrl+Alt+Shift+F12", config.SettingsHotkeyDisplay);
            Assert.AreEqual(0x7B, config.SettingsHotkeyVk);
        }

        [TestMethod]
        public void NormalizeHotkeys_DoesNotOverrideCustomShortcuts()
        {
            var config = new OverlayConfig
            {
                HotkeyDisplay = "Ctrl+Alt+Shift+F6",
                HotkeyModifiers = 0x0001 | 0x0002 | 0x0004,
                HotkeyVk = 0x75
            };

            config.NormalizeHotkeys();

            Assert.AreEqual("Ctrl+Alt+Shift+F6", config.HotkeyDisplay);
            Assert.AreEqual(0x75, config.HotkeyVk);
        }

        [TestMethod]
        public void NormalizeDashboard_ClampsInvalidPort()
        {
            var config = new OverlayConfig
            {
                PhoneDashboardPort = 70000
            };

            config.NormalizeDashboard();

            Assert.AreEqual(4587, config.PhoneDashboardPort);
        }
    }
}
