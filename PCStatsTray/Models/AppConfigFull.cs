using System.Collections.Generic;

namespace PCStatsTray
{
    /// <summary>
    /// Full config model that includes both sensor filter settings and overlay settings.
    /// </summary>
    internal class StoredAppConfig
    {
        public List<string> HiddenSensors { get; set; } = new();
        public OverlayConfig? Overlay { get; set; }
        public bool SuppressPawnIoPrompt { get; set; }
        public DetailsWindowLayout DetailsWindow { get; set; } = new();
    }

    internal class DetailsWindowLayout
    {
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public int Width { get; set; } = 820;
        public int Height { get; set; } = 560;
        public int SidebarWidth { get; set; } = 200;
    }
}
