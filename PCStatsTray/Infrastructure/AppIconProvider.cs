using System.Drawing;
using System.Windows.Forms;

namespace PCStatsTray
{
    internal static class AppIconProvider
    {
        private static Icon? _cachedIcon;

        public static Icon? GetAppIcon()
        {
            if (_cachedIcon != null)
            {
                return (Icon)_cachedIcon.Clone();
            }

            using var extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted == null)
            {
                return null;
            }

            _cachedIcon = (Icon)extracted.Clone();
            return (Icon)_cachedIcon.Clone();
        }
    }
}
