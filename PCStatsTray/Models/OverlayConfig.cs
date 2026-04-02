using System;
using System.Collections.Generic;

namespace PCStatsTray
{
    public enum OverlayPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public enum OverlayDisplayTarget
    {
        Desktop,
        Rtss
    }

    /// <summary>
    /// Defines which metric to display on the OSD overlay.
    /// </summary>
    public class OverlayMetric
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool? DesktopEnabled { get; set; }
        public bool? RtssEnabled { get; set; }

        public OverlayMetric() { }
        public OverlayMetric(string key, string label, bool enabled = true)
        {
            Key = key;
            Label = label;
            Enabled = enabled;
            DesktopEnabled = enabled;
            RtssEnabled = enabled;
        }

        public bool IsEnabledFor(OverlayDisplayTarget target)
        {
            return target switch
            {
                OverlayDisplayTarget.Desktop => DesktopEnabled ?? Enabled,
                OverlayDisplayTarget.Rtss => RtssEnabled ?? Enabled,
                _ => Enabled
            };
        }

        public void SetEnabledStates(bool desktopEnabled, bool rtssEnabled)
        {
            Enabled = desktopEnabled || rtssEnabled;
            DesktopEnabled = desktopEnabled;
            RtssEnabled = rtssEnabled;
        }
    }

    /// <summary>
    /// All OSD overlay settings, persisted in hwmon_config.json.
    /// </summary>
    public class OverlayConfig
    {
        private const int ModAlt = 0x0001;
        private const int ModControl = 0x0002;
        private const int ModShift = 0x0004;
        private const int DefaultHotkeyModifiers = ModAlt | ModControl | ModShift;
        private const int LegacyHotkeyModifiers = ModControl | ModShift;

        public const string BackgroundSolid = "Solid";
        public const string BackgroundNone = "None";
        public const string RamDisplayUsedAndTotal = "UsedAndTotal";
        public const string RamDisplayPercentage = "Percentage";
        public const string VramDisplayUsedAndTotal = "UsedAndTotal";
        public const string VramDisplayPercentage = "Percentage";

        public bool Enabled { get; set; } = true;

        // Hotkey (Win32 RegisterHotKey values)
        public string HotkeyDisplay { get; set; } = "Ctrl+Alt+Shift+F7";
        public int HotkeyModifiers { get; set; } = DefaultHotkeyModifiers;
        public int HotkeyVk { get; set; } = 0x76; // F7
        public string DesktopHotkeyDisplay { get; set; } = "Ctrl+Alt+Shift+F8";
        public int DesktopHotkeyModifiers { get; set; } = DefaultHotkeyModifiers;
        public int DesktopHotkeyVk { get; set; } = 0x77; // F8
        public string RtssHotkeyDisplay { get; set; } = "Ctrl+Alt+Shift+F10";
        public int RtssHotkeyModifiers { get; set; } = DefaultHotkeyModifiers;
        public int RtssHotkeyVk { get; set; } = 0x79; // F10
        public string SettingsHotkeyDisplay { get; set; } = "Ctrl+Alt+Shift+F12";
        public int SettingsHotkeyModifiers { get; set; } = DefaultHotkeyModifiers;
        public int SettingsHotkeyVk { get; set; } = 0x7B; // F12

        // Position & layout
        public bool DesktopOverlayEnabled { get; set; } = true;
        public bool RtssOverlayEnabled { get; set; } = false;
        public string Position { get; set; } = "TopRight";
        public int OffsetX { get; set; } = 20;
        public int OffsetY { get; set; } = 20;
        public float Opacity { get; set; } = 0.85f;
        public float FontSize { get; set; } = 11f;
        public string FontFamily { get; set; } = "Segoe UI";
        public string BackgroundMode { get; set; } = BackgroundSolid;
        public bool ShowTextShadow { get; set; } = true;
        public bool ShowBorder { get; set; } = true;
        public bool ShowTextOutline { get; set; } = true;
        public int TextOutlineThickness { get; set; } = 2;
        public string RamDisplayMode { get; set; } = RamDisplayUsedAndTotal;
        public string VramDisplayMode { get; set; } = VramDisplayUsedAndTotal;
        public int RefreshIntervalMs { get; set; } = 1000;
        public int SettingsWindowX { get; set; } = -1;
        public int SettingsWindowY { get; set; } = -1;
        public int SettingsWindowWidth { get; set; } = 420;
        public int SettingsWindowHeight { get; set; } = 840;
        public bool PhoneDashboardEnabled { get; set; }
        public int PhoneDashboardPort { get; set; } = 4587;
        public string CpuFanSensorKey { get; set; } = string.Empty;
        public string GpuFanSensorKey { get; set; } = string.Empty;
        public string CaseFanSensorKey { get; set; } = string.Empty;

        // Which metrics to show
        public List<OverlayMetric> Metrics { get; set; } = DefaultMetrics();

        public static List<OverlayMetric> DefaultMetrics() => new()
        {
            new("CpuTemp",  "CPU Temp",  true),
            new("CpuLoad",  "CPU Load",  true),
            new("CpuClockAvg", "CPU Avg Clock", true),
            new("CpuClock", "CPU Peak Clock", false),
            new("CpuClockEffectiveAvg", "CPU Avg Eff Clock", false),
            new("CpuPower", "CPU Power", false),
            new("CpuFan",   "CPU Fan",   false),
            new("GpuTemp",  "GPU Temp",  true),
            new("GpuLoad",  "GPU Load",  true),
            new("GpuClock", "GPU Clock", false),
            new("GpuVram",  "GPU VRAM",  false),
            new("GpuPower", "GPU Power", false),
            new("GpuFan",   "GPU Fan",   false),
            new("RamUsage", "RAM Usage", true),
            new("CaseFan",  "Case Fan",  false),
        };

        public void NormalizeMetrics()
        {
            var defaults = DefaultMetrics();
            var existing = Metrics ?? new List<OverlayMetric>();
            var existingByKey = new Dictionary<string, OverlayMetric>(StringComparer.OrdinalIgnoreCase);

            foreach (var metric in existing)
            {
                if (!string.IsNullOrWhiteSpace(metric.Key) && !existingByKey.ContainsKey(metric.Key))
                {
                    existingByKey[metric.Key] = metric;
                }
            }

            bool legacyFanEnabled = existingByKey.TryGetValue("FanSpeed", out var legacyFanMetric) && legacyFanMetric.Enabled;
            bool migrateLegacyCpuClockState =
                existingByKey.TryGetValue("CpuClock", out var legacyCpuClockMetric) &&
                !existingByKey.ContainsKey("CpuClockAvg") &&
                !existingByKey.ContainsKey("CpuClockEffectiveAvg");
            var normalized = new List<OverlayMetric>(defaults.Count);

            foreach (var defaultMetric in defaults)
            {
                if (migrateLegacyCpuClockState &&
                    string.Equals(defaultMetric.Key, "CpuClockAvg", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizedMetric = new OverlayMetric(defaultMetric.Key, defaultMetric.Label);
                    normalizedMetric.SetEnabledStates(
                        legacyCpuClockMetric!.IsEnabledFor(OverlayDisplayTarget.Desktop),
                        legacyCpuClockMetric.IsEnabledFor(OverlayDisplayTarget.Rtss));
                    normalized.Add(normalizedMetric);
                    continue;
                }

                if (existingByKey.TryGetValue(defaultMetric.Key, out var currentMetric))
                {
                    var normalizedMetric = new OverlayMetric(defaultMetric.Key, defaultMetric.Label);
                    bool desktopEnabled = currentMetric.IsEnabledFor(OverlayDisplayTarget.Desktop);
                    bool rtssEnabled = currentMetric.IsEnabledFor(OverlayDisplayTarget.Rtss);
                    if (migrateLegacyCpuClockState &&
                        string.Equals(defaultMetric.Key, "CpuClock", StringComparison.OrdinalIgnoreCase))
                    {
                        desktopEnabled = false;
                        rtssEnabled = false;
                    }

                    normalizedMetric.SetEnabledStates(
                        desktopEnabled,
                        rtssEnabled);
                    normalized.Add(normalizedMetric);
                    continue;
                }

                bool enabled = legacyFanEnabled && string.Equals(defaultMetric.Key, "CpuFan", StringComparison.OrdinalIgnoreCase)
                    ? true
                    : defaultMetric.Enabled;
                normalized.Add(new OverlayMetric(defaultMetric.Key, defaultMetric.Label, enabled));
            }

            Metrics = normalized;
        }

        public void NormalizeHotkeys()
        {
            if (MatchesLegacyDefault(HotkeyDisplay, HotkeyModifiers, HotkeyVk, "Ctrl+Shift+O", 0x4F))
            {
                HotkeyDisplay = "Ctrl+Alt+Shift+F7";
                HotkeyModifiers = DefaultHotkeyModifiers;
                HotkeyVk = 0x76;
            }

            if (MatchesLegacyDefault(DesktopHotkeyDisplay, DesktopHotkeyModifiers, DesktopHotkeyVk, "Ctrl+Shift+D", 0x44))
            {
                DesktopHotkeyDisplay = "Ctrl+Alt+Shift+F8";
                DesktopHotkeyModifiers = DefaultHotkeyModifiers;
                DesktopHotkeyVk = 0x77;
            }

            if (MatchesLegacyDefault(RtssHotkeyDisplay, RtssHotkeyModifiers, RtssHotkeyVk, "Ctrl+Shift+R", 0x52))
            {
                RtssHotkeyDisplay = "Ctrl+Alt+Shift+F10";
                RtssHotkeyModifiers = DefaultHotkeyModifiers;
                RtssHotkeyVk = 0x79;
            }

            if (MatchesLegacyDefault(SettingsHotkeyDisplay, SettingsHotkeyModifiers, SettingsHotkeyVk, "Ctrl+Shift+S", 0x53))
            {
                SettingsHotkeyDisplay = "Ctrl+Alt+Shift+F12";
                SettingsHotkeyModifiers = DefaultHotkeyModifiers;
                SettingsHotkeyVk = 0x7B;
            }
        }

        public void NormalizeDashboard()
        {
            if (PhoneDashboardPort is < 1024 or > 65535)
            {
                PhoneDashboardPort = 4587;
            }

            if (RefreshIntervalMs is not (1000 or 2000 or 5000))
            {
                RefreshIntervalMs = 1000;
            }
        }

        public OverlayPosition GetPosition()
        {
            return Position switch
            {
                "TopLeft" => OverlayPosition.TopLeft,
                "BottomLeft" => OverlayPosition.BottomLeft,
                "BottomRight" => OverlayPosition.BottomRight,
                _ => OverlayPosition.TopRight
            };
        }

        public bool ShowRamAsPercentage()
        {
            return string.Equals(RamDisplayMode, RamDisplayPercentage, StringComparison.OrdinalIgnoreCase);
        }

        public bool ShowVramAsPercentage()
        {
            return string.Equals(VramDisplayMode, VramDisplayPercentage, StringComparison.OrdinalIgnoreCase);
        }

        public bool HasBackground()
        {
            return !string.Equals(BackgroundMode, BackgroundNone, StringComparison.OrdinalIgnoreCase);
        }

        public bool HasAnyOverlayBackendEnabled()
        {
            return DesktopOverlayEnabled || RtssOverlayEnabled;
        }

        public bool HasSavedSettingsWindowBounds()
        {
            return SettingsWindowX >= 0 &&
                   SettingsWindowY >= 0 &&
                   SettingsWindowWidth >= 320 &&
                   SettingsWindowHeight >= 480;
        }

        private static bool MatchesLegacyDefault(string? display, int modifiers, int virtualKey, string expectedDisplay, int expectedVirtualKey)
        {
            return modifiers == LegacyHotkeyModifiers &&
                   virtualKey == expectedVirtualKey &&
                   string.Equals(display, expectedDisplay, StringComparison.Ordinal);
        }
    }
}
