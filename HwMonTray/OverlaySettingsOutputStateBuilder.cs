namespace HwMonTray
{
    internal enum OverlaySettingsOutputTone
    {
        Success,
        Warning
    }

    internal readonly record struct OverlaySettingsOutputState(string Text, OverlaySettingsOutputTone Tone);

    internal static class OverlaySettingsOutputStateBuilder
    {
        public static OverlaySettingsOutputState Build(bool enabled, bool desktopEnabled, bool rtssEnabled, RtssStatusSnapshot? snapshot)
        {
            if (!enabled)
            {
                return new OverlaySettingsOutputState(
                    "OSD is currently off." + System.Environment.NewLine + "Enable it here or use the tray toggle/hotkey.",
                    OverlaySettingsOutputTone.Warning);
            }

            if (rtssEnabled && snapshot != null)
            {
                bool healthy = snapshot.IsProcessRunning && snapshot.HasSharedMemory && snapshot.IsSlotOwned;
                return new OverlaySettingsOutputState(
                    snapshot.Status,
                    healthy ? OverlaySettingsOutputTone.Success : OverlaySettingsOutputTone.Warning);
            }

            if (desktopEnabled)
            {
                return new OverlaySettingsOutputState("Desktop OSD is enabled.", OverlaySettingsOutputTone.Success);
            }

            return new OverlaySettingsOutputState("No output backend is enabled.", OverlaySettingsOutputTone.Warning);
        }
    }
}
