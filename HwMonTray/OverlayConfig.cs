using System;
using System.Collections.Generic;

namespace HwMonTray
{
    public enum OverlayPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Defines which metric to display on the OSD overlay.
    /// </summary>
    public class OverlayMetric
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public bool Enabled { get; set; } = true;

        public OverlayMetric() { }
        public OverlayMetric(string key, string label, bool enabled = true)
        {
            Key = key;
            Label = label;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// All OSD overlay settings, persisted in hwmon_config.json.
    /// </summary>
    public class OverlayConfig
    {
        public const string BackgroundSolid = "Solid";
        public const string BackgroundNone = "None";
        public const string RamDisplayUsedAndTotal = "UsedAndTotal";
        public const string RamDisplayPercentage = "Percentage";

        public bool Enabled { get; set; } = true;

        // Hotkey (Win32 RegisterHotKey values)
        public string HotkeyDisplay { get; set; } = "Ctrl+Shift+O";
        public int HotkeyModifiers { get; set; } = 0x0002 | 0x0004; // MOD_CONTROL | MOD_SHIFT
        public int HotkeyVk { get; set; } = 0x4F; // 'O'

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
        public int SettingsWindowX { get; set; } = -1;
        public int SettingsWindowY { get; set; } = -1;
        public int SettingsWindowWidth { get; set; } = 420;
        public int SettingsWindowHeight { get; set; } = 840;

        // Which metrics to show
        public List<OverlayMetric> Metrics { get; set; } = DefaultMetrics();

        public static List<OverlayMetric> DefaultMetrics() => new()
        {
            new("CpuTemp",  "CPU Temp",  true),
            new("CpuLoad",  "CPU Load",  true),
            new("CpuClock", "CPU Clock", false),
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
            var normalized = new List<OverlayMetric>(defaults.Count);

            foreach (var defaultMetric in defaults)
            {
                if (existingByKey.TryGetValue(defaultMetric.Key, out var currentMetric))
                {
                    normalized.Add(new OverlayMetric(defaultMetric.Key, defaultMetric.Label, currentMetric.Enabled));
                    continue;
                }

                bool enabled = legacyFanEnabled && string.Equals(defaultMetric.Key, "CpuFan", StringComparison.OrdinalIgnoreCase)
                    ? true
                    : defaultMetric.Enabled;
                normalized.Add(new OverlayMetric(defaultMetric.Key, defaultMetric.Label, enabled));
            }

            Metrics = normalized;
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
    }
}
