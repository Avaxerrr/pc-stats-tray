using System;

namespace PCStatsTray
{
    internal sealed class OverlaySettingsState
    {
        public HotkeyBinding ToggleAllHotkey { get; init; }
        public HotkeyBinding ToggleDesktopHotkey { get; init; }
        public HotkeyBinding ToggleRtssHotkey { get; init; }
        public HotkeyBinding SettingsHotkey { get; init; }
        public bool Enabled { get; init; }
        public bool DesktopOverlayEnabled { get; init; }
        public bool RtssOverlayEnabled { get; init; }
        public bool AlignRight { get; init; }
        public bool AlignBottom { get; init; }
        public int OffsetX { get; init; }
        public int OffsetY { get; init; }
        public int OpacityPercent { get; init; }
        public int FontSize { get; init; }
        public string FontFamily { get; init; } = string.Empty;
        public bool HasBackground { get; init; }
        public bool ShowTextShadow { get; init; }
        public bool ShowBorder { get; init; }
        public bool ShowTextOutline { get; init; }
        public int TextOutlineThickness { get; init; }
        public bool ShowRamAsPercentage { get; init; }
        public bool ShowVramAsPercentage { get; init; }
        public string CpuFanSensorKey { get; init; } = string.Empty;
        public string GpuFanSensorKey { get; init; } = string.Empty;
        public string CaseFanSensorKey { get; init; } = string.Empty;
        public bool[] DesktopMetricEnabledStates { get; init; } = Array.Empty<bool>();
        public bool[] RtssMetricEnabledStates { get; init; } = Array.Empty<bool>();
    }

    internal static class OverlaySettingsConfigMapper
    {
        public static void Apply(OverlayConfig config, OverlaySettingsState state)
        {
            if (!state.ToggleAllHotkey.IsEmpty)
            {
                config.HotkeyDisplay = state.ToggleAllHotkey.Display;
                config.HotkeyModifiers = state.ToggleAllHotkey.Modifiers;
                config.HotkeyVk = state.ToggleAllHotkey.VirtualKey;
            }

            if (!state.ToggleDesktopHotkey.IsEmpty)
            {
                config.DesktopHotkeyDisplay = state.ToggleDesktopHotkey.Display;
                config.DesktopHotkeyModifiers = state.ToggleDesktopHotkey.Modifiers;
                config.DesktopHotkeyVk = state.ToggleDesktopHotkey.VirtualKey;
            }

            if (!state.ToggleRtssHotkey.IsEmpty)
            {
                config.RtssHotkeyDisplay = state.ToggleRtssHotkey.Display;
                config.RtssHotkeyModifiers = state.ToggleRtssHotkey.Modifiers;
                config.RtssHotkeyVk = state.ToggleRtssHotkey.VirtualKey;
            }

            if (!state.SettingsHotkey.IsEmpty)
            {
                config.SettingsHotkeyDisplay = state.SettingsHotkey.Display;
                config.SettingsHotkeyModifiers = state.SettingsHotkey.Modifiers;
                config.SettingsHotkeyVk = state.SettingsHotkey.VirtualKey;
            }

            config.Enabled = state.Enabled;
            config.DesktopOverlayEnabled = state.DesktopOverlayEnabled;
            config.RtssOverlayEnabled = state.RtssOverlayEnabled;
            config.Position = (state.AlignRight, state.AlignBottom) switch
            {
                (false, false) => "TopLeft",
                (true, false) => "TopRight",
                (false, true) => "BottomLeft",
                (true, true) => "BottomRight"
            };

            config.OffsetX = state.OffsetX;
            config.OffsetY = state.OffsetY;
            config.Opacity = state.OpacityPercent / 100f;
            config.FontSize = state.FontSize;
            config.FontFamily = string.IsNullOrWhiteSpace(state.FontFamily) ? config.FontFamily : state.FontFamily;
            config.BackgroundMode = state.HasBackground ? OverlayConfig.BackgroundSolid : OverlayConfig.BackgroundNone;
            config.ShowTextShadow = state.ShowTextShadow;
            config.ShowBorder = state.ShowBorder;
            config.ShowTextOutline = state.ShowTextOutline;
            config.TextOutlineThickness = state.TextOutlineThickness;
            config.RamDisplayMode = state.ShowRamAsPercentage
                ? OverlayConfig.RamDisplayPercentage
                : OverlayConfig.RamDisplayUsedAndTotal;
            config.VramDisplayMode = state.ShowVramAsPercentage
                ? OverlayConfig.VramDisplayPercentage
                : OverlayConfig.VramDisplayUsedAndTotal;
            config.CpuFanSensorKey = state.CpuFanSensorKey;
            config.GpuFanSensorKey = state.GpuFanSensorKey;
            config.CaseFanSensorKey = state.CaseFanSensorKey;

            for (int i = 0; i < config.Metrics.Count; i++)
            {
                bool desktopEnabled = i < state.DesktopMetricEnabledStates.Length
                    ? state.DesktopMetricEnabledStates[i]
                    : config.Metrics[i].IsEnabledFor(OverlayDisplayTarget.Desktop);
                bool rtssEnabled = i < state.RtssMetricEnabledStates.Length
                    ? state.RtssMetricEnabledStates[i]
                    : config.Metrics[i].IsEnabledFor(OverlayDisplayTarget.Rtss);
                config.Metrics[i].SetEnabledStates(desktopEnabled, rtssEnabled);
            }
        }
    }
}
