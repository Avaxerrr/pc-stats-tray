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
    }
}
