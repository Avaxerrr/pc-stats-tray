using System.Windows.Forms;

namespace HwMonTray
{
    internal readonly record struct HotkeyBinding(int Modifiers, int VirtualKey, string Display)
    {
        public bool IsEmpty => Modifiers == 0 || VirtualKey == 0 || string.IsNullOrWhiteSpace(Display);

        public static HotkeyBinding FromStored(int modifiers, int virtualKey, string display)
        {
            return new HotkeyBinding(modifiers, virtualKey, display ?? string.Empty);
        }
    }

    internal static class HotkeyCaptureHelper
    {
        private const int ModAlt = 0x0001;
        private const int ModControl = 0x0002;
        private const int ModShift = 0x0004;

        public static bool TryBuild(Keys keyCode, bool control, bool alt, bool shift, out HotkeyBinding binding, out string message)
        {
            binding = default;
            message = string.Empty;

            if (keyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            {
                message = "Modifier only";
                return false;
            }

            int modifiers = 0;
            string display = string.Empty;

            if (control)
            {
                modifiers |= ModControl;
                display += "Ctrl+";
            }

            if (alt)
            {
                modifiers |= ModAlt;
                display += "Alt+";
            }

            if (shift)
            {
                modifiers |= ModShift;
                display += "Shift+";
            }

            if (modifiers == 0)
            {
                message = "Need a modifier key";
                return false;
            }

            display += keyCode;
            binding = new HotkeyBinding(modifiers, (int)keyCode, display);
            message = display;
            return true;
        }
    }
}
