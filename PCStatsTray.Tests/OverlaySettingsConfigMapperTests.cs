using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class OverlaySettingsConfigMapperTests
    {
        [TestMethod]
        public void Apply_MapsSettingsStateToOverlayConfig()
        {
            var config = new OverlayConfig();
            config.NormalizeMetrics();

            var state = new OverlaySettingsState
            {
                ToggleAllHotkey = new HotkeyBinding(6, 0x4F, "Ctrl+Shift+O"),
                ToggleDesktopHotkey = new HotkeyBinding(6, 0x44, "Ctrl+Shift+D"),
                ToggleRtssHotkey = new HotkeyBinding(6, 0x52, "Ctrl+Shift+R"),
                SettingsHotkey = new HotkeyBinding(6, 0x53, "Ctrl+Shift+S"),
                Enabled = false,
                DesktopOverlayEnabled = true,
                RtssOverlayEnabled = true,
                AlignRight = false,
                AlignBottom = true,
                OffsetX = 12,
                OffsetY = 30,
                OpacityPercent = 90,
                FontSize = 18,
                FontFamily = "Bahnschrift",
                HasBackground = false,
                ShowTextShadow = true,
                ShowBorder = false,
                ShowTextOutline = true,
                TextOutlineThickness = 3,
                ShowRamAsPercentage = true,
                ShowVramAsPercentage = true,
                CpuFanSensorKey = "cpu-fan",
                GpuFanSensorKey = "gpu-fan",
                CaseFanSensorKey = "case-fan",
                StorageSourceKey = "/storage/0",
                NetworkSourceKey = "/network/1",
                DesktopMetricEnabledStates = new[] { true, false, true },
                RtssMetricEnabledStates = new[] { false, true, true }
            };

            OverlaySettingsConfigMapper.Apply(config, state);

            Assert.IsFalse(config.Enabled);
            Assert.AreEqual("Ctrl+Shift+D", config.DesktopHotkeyDisplay);
            Assert.AreEqual("Ctrl+Shift+R", config.RtssHotkeyDisplay);
            Assert.IsTrue(config.RtssOverlayEnabled);
            Assert.AreEqual("BottomLeft", config.Position);
            Assert.AreEqual(12, config.OffsetX);
            Assert.AreEqual(30, config.OffsetY);
            Assert.AreEqual(0.9f, config.Opacity, 0.001f);
            Assert.AreEqual(18f, config.FontSize, 0.001f);
            Assert.AreEqual("Bahnschrift", config.FontFamily);
            Assert.AreEqual(OverlayConfig.BackgroundNone, config.BackgroundMode);
            Assert.AreEqual(OverlayConfig.RamDisplayPercentage, config.RamDisplayMode);
            Assert.AreEqual(OverlayConfig.VramDisplayPercentage, config.VramDisplayMode);
            Assert.AreEqual("cpu-fan", config.CpuFanSensorKey);
            Assert.AreEqual("/storage/0", config.StorageSourceKey);
            Assert.AreEqual("/network/1", config.NetworkSourceKey);
            Assert.IsTrue(config.Metrics[0].IsEnabledFor(OverlayDisplayTarget.Desktop));
            Assert.IsFalse(config.Metrics[0].IsEnabledFor(OverlayDisplayTarget.Rtss));
            Assert.IsFalse(config.Metrics[1].IsEnabledFor(OverlayDisplayTarget.Desktop));
            Assert.IsTrue(config.Metrics[1].IsEnabledFor(OverlayDisplayTarget.Rtss));
        }
    }
}
