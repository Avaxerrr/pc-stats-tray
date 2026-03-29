using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PCStatsTray
{
    internal sealed class GlobalHotkeyService : IDisposable
    {
        private readonly HotkeyWindow _window;

        public event Action<int>? HotkeyPressed;

        public GlobalHotkeyService()
        {
            _window = new HotkeyWindow();
            _window.HotkeyPressed += id => HotkeyPressed?.Invoke(id);
        }

        public void ApplyConfig(OverlayConfig config)
        {
            UnregisterAll();

            if (config.HotkeyVk != 0)
            {
                HotkeyWindow.RegisterHotKey(_window.Handle, 1, (uint)config.HotkeyModifiers, (uint)config.HotkeyVk);
            }

            if (config.SettingsHotkeyVk != 0)
            {
                HotkeyWindow.RegisterHotKey(_window.Handle, 2, (uint)config.SettingsHotkeyModifiers, (uint)config.SettingsHotkeyVk);
            }
        }

        public void Dispose()
        {
            UnregisterAll();
            _window.DestroyHandle();
        }

        private void UnregisterAll()
        {
            HotkeyWindow.UnregisterHotKey(_window.Handle, 1);
            HotkeyWindow.UnregisterHotKey(_window.Handle, 2);
        }

        /// <summary>
        /// Invisible NativeWindow that listens for WM_HOTKEY messages.
        /// </summary>
        private sealed class HotkeyWindow : NativeWindow
        {
            private const int WM_HOTKEY = 0x0312;

            public event Action<int>? HotkeyPressed;

            [DllImport("user32.dll")]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [DllImport("user32.dll")]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            public HotkeyWindow()
            {
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    HotkeyPressed?.Invoke(m.WParam.ToInt32());
                }

                base.WndProc(ref m);
            }
        }
    }
}
