using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace HwMonTray
{
    /// <summary>
    /// A borderless, click-through, always-on-top overlay that displays hardware metrics.
    /// Uses WS_EX_TRANSPARENT | WS_EX_LAYERED so all mouse events pass through.
    /// Uses UpdateLayeredWindow for per-pixel alpha blending (soft edges).
    /// </summary>
    public class OverlayForm : Form
    {
        #region Win32 API
        
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, IntPtr hdcSrc, ref Point pprSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const uint ULW_ALPHA = 2;
        
        #endregion
        // Win32 extended styles for click-through
        private const int WS_EX_LAYERED     = 0x00080000;
        private const int WS_EX_TRANSPARENT  = 0x00000020;
        private const int WS_EX_TOPMOST      = 0x00000008;
        private const int WS_EX_TOOLWINDOW   = 0x00000080;
        private const int WS_EX_NOACTIVATE   = 0x08000000;

        private readonly Computer _computer;
        private OverlayConfig _config;
        private Dictionary<string, string> _currentValues = new();

        // Cached rendering resources
        private Font? _metricFont;
        private Font? _labelFont;

        public OverlayForm(Computer computer, OverlayConfig config)
        {
            _computer = computer;
            _config = config;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            
            // Required for UpdateLayeredWindow
            SetStyle(ControlStyles.Selectable, false);

            ApplyConfig();
        }

        /// <summary>
        /// Override CreateParams to set click-through extended window styles.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // Prevent the form from ever taking focus
        protected override bool ShowWithoutActivation => true;

        public void ApplyConfig()
        {
            _metricFont?.Dispose();
            _labelFont?.Dispose();
            
            // Segoe UI for clean typography
            _metricFont = new Font("Segoe UI", _config.FontSize, FontStyle.Bold);
            _labelFont = new Font("Segoe UI", _config.FontSize * 0.75f, FontStyle.Bold);

            RecalcSize();
            RepositionOnScreen();
            
            if (Visible && IsHandleCreated)
                UpdateOverlay();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated)
            {
                RecalcSize();
                RepositionOnScreen();
                UpdateOverlay();
            }
        }

        /// <summary>
        /// Update the config reference and re-apply.
        /// </summary>
        public void UpdateConfig(OverlayConfig config)
        {
            _config = config;
            ApplyConfig();
        }

        /// <summary>
        /// Refresh the sensor data and repaint.
        /// </summary>
        public void RefreshData()
        {
            _currentValues.Clear();

            foreach (var hw in _computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    CollectCpuMetrics(hw);
                }
                else if (hw.HardwareType == HardwareType.GpuNvidia ||
                         hw.HardwareType == HardwareType.GpuAmd ||
                         hw.HardwareType == HardwareType.GpuIntel)
                {
                    CollectGpuMetrics(hw);
                }
                else if (hw.HardwareType == HardwareType.Memory)
                {
                    CollectRamMetrics(hw);
                }
            }

            // Fan — check CPU cooler sub-hardware or motherboard SuperIO
            if (!_currentValues.ContainsKey("FanSpeed"))
            {
                foreach (var hw in _computer.Hardware)
                {
                    foreach (var sub in hw.SubHardware)
                    {
                        var fan = sub.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Value.HasValue);
                        if (fan != null)
                        {
                            _currentValues["FanSpeed"] = $"{fan.Value!.Value:0} RPM";
                            break;
                        }
                    }
                    if (_currentValues.ContainsKey("FanSpeed")) break;

                    var topFan = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Value.HasValue);
                    if (topFan != null)
                    {
                        _currentValues["FanSpeed"] = $"{topFan.Value!.Value:0} RPM";
                        break;
                    }
                }
            }

            RecalcSize();
            RepositionOnScreen();
            Invalidate();
        }

        private void CollectCpuMetrics(IHardware hw)
        {
            // Temperature
            var temp = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core Max"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Package"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp?.Value.HasValue == true)
                _currentValues["CpuTemp"] = $"{temp.Value.Value:0}°C";

            // Load (Total)
            var load = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            if (load?.Value.HasValue == true)
                _currentValues["CpuLoad"] = $"{load.Value.Value:0}%";

            // Clock (max of all cores)
            var clocks = hw.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue && s.Name.Contains("Core")).ToList();
            if (clocks.Count > 0)
                _currentValues["CpuClock"] = $"{clocks.Max(s => s.Value!.Value):0} MHz";

            // Power (Package)
            var power = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"))
                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
            if (power?.Value.HasValue == true)
                _currentValues["CpuPower"] = $"{power.Value.Value:0.#} W";
        }

        private void CollectGpuMetrics(IHardware hw)
        {
            // Temperature
            var temp = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp?.Value.HasValue == true)
                _currentValues["GpuTemp"] = $"{temp.Value.Value:0}°C";

            // Load
            var load = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            if (load?.Value.HasValue == true)
                _currentValues["GpuLoad"] = $"{load.Value.Value:0}%";

            // Clock
            var clock = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
            if (clock?.Value.HasValue == true)
                _currentValues["GpuClock"] = $"{clock.Value.Value:0} MHz";

            // VRAM usage
            var vramUsed = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used"))
                        ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("GPU Memory Used"));
            var vramTotal = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total"))
                         ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("GPU Memory Total"));
            if (vramUsed?.Value.HasValue == true)
            {
                float usedMb = vramUsed.Value.Value;
                if (vramTotal?.Value.HasValue == true)
                {
                    float totalMb = vramTotal.Value.Value;
                    _currentValues["GpuVram"] = $"{usedMb / 1024:0.#} / {totalMb / 1024:0.#} GB";
                }
                else
                {
                    _currentValues["GpuVram"] = $"{usedMb / 1024:0.#} GB";
                }
            }

            // Power
            var power = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
            if (power?.Value.HasValue == true)
                _currentValues["GpuPower"] = $"{power.Value.Value:0.#} W";
        }

        private void CollectRamMetrics(IHardware hw)
        {
            var used = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Used"));
            var avail = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Available"));
            if (used?.Value.HasValue == true)
            {
                float usedGb = used.Value.Value;
                if (avail?.Value.HasValue == true)
                {
                    float totalGb = usedGb + avail.Value.Value;
                    _currentValues["RamUsage"] = $"{usedGb:0.#} / {totalGb:0.#} GB";
                }
                else
                {
                    _currentValues["RamUsage"] = $"{usedGb:0.#} GB";
                }
            }
        }

        private List<(string label, string value, string key)> GetVisibleMetrics()
        {
            var result = new List<(string, string, string)>();
            foreach (var m in _config.Metrics)
            {
                if (!m.Enabled) continue;
                string val = _currentValues.TryGetValue(m.Key, out var v) ? v : "—";
                result.Add((m.Label, val, m.Key));
            }
            return result;
        }

        private void RecalcSize()
        {
            var metrics = GetVisibleMetrics();
            if (metrics.Count == 0)
            {
                Size = new Size(1, 1);
                return;
            }

            int padding = (int)(_config.FontSize * 1.5f); // scalable padding
            int rowHeight = (int)(_config.FontSize * 1.8f);
            int contentHeight = (metrics.Count * rowHeight) + (padding * 2);

            // Measure width
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            float maxLabelW = 0, maxValueW = 0;
            
            foreach (var (label, value, _) in metrics)
            {
                var lw = g.MeasureString(label.ToUpper(), _labelFont!).Width;
                var vw = g.MeasureString(value, _metricFont!).Width;
                if (lw > maxLabelW) maxLabelW = lw;
                if (vw > maxValueW) maxValueW = vw;
            }

            // Gap between label and value
            int gap = (int)(_config.FontSize * 1.5f);
            int contentWidth = (int)(maxLabelW + gap + maxValueW + (padding * 2));
            
            // Ensure a nice minimum width so it doesn't look too squished
            contentWidth = Math.Max(contentWidth, 180);

            Size = new Size(contentWidth, contentHeight);
        }

        private void RepositionOnScreen()
        {
            var screen = Screen.PrimaryScreen!.WorkingArea;
            var pos = _config.GetPosition();

            int x = pos switch
            {
                OverlayPosition.TopLeft or OverlayPosition.BottomLeft => screen.Left + _config.OffsetX,
                _ => screen.Right - Width - _config.OffsetX
            };

            int y = pos switch
            {
                OverlayPosition.TopLeft or OverlayPosition.TopRight => screen.Top + _config.OffsetY,
                _ => screen.Bottom - Height - _config.OffsetY
            };

            Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Do not paint via base. OnPaint is bypassed for UpdateLayeredWindow
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Bypass background painting
        }

        private void UpdateOverlay()
        {
            var metrics = GetVisibleMetrics();
            if (metrics.Count == 0 || Width <= 0 || Height <= 0) return;

            using var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                int padding = (int)(_config.FontSize * 1.5f);
                int radius = 12;
                int rowHeight = (int)(_config.FontSize * 1.8f);

                // Background with transparent per-pixel blending
                var bgRect = new Rectangle(0, 0, Width - 1, Height - 1);
                using var bgPath = RoundedRect(bgRect, radius);
                using (var bgBrush = new SolidBrush(Color.FromArgb(215, 12, 12, 15))) 
                    g.FillPath(bgBrush, bgPath);
                
                if (_config.ShowBorder)
                {
                    using (var borderPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f)) 
                        g.DrawPath(borderPen, bgPath);
                }

                var fmtLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                var fmtRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

                int currentY = padding;

                // Metrics
                foreach (var (rawLabel, value, key) in metrics)
                {
                    string label = rawLabel.ToUpper();
                    
                    var rowRectLeft = new RectangleF(padding, currentY, Width - (padding * 2), rowHeight);
                    var rowRectRight = new RectangleF(padding, currentY + 1, Width - (padding * 2), rowHeight);

                    using (var labelBrush = new SolidBrush(Color.FromArgb(150, 160, 175)))
                        g.DrawString(label, _labelFont!, labelBrush, rowRectLeft, fmtLeft);

                    Color valueColor = GetValueColor(key, value);
                    using (var valBrush = new SolidBrush(valueColor))
                        g.DrawString(value, _metricFont!, valBrush, rowRectRight, fmtRight);

                    currentY += rowHeight;
                }
            }

            // Push to DWM via Win32 UpdateLayeredWindow for per-pixel alpha
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0)); 
                oldBitmap = SelectObject(memDc, hBitmap);

                var ptSrc = new Point(0, 0);
                var ptDst = new Point(Left, Top);
                var size = new Size(bmp.Width, bmp.Height);

                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = (byte)(_config.Opacity * 255f),
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                    DeleteObject(hBitmap);
                }
                DeleteDC(memDc);
            }
        }

        // Removed remaining OnPaint metrics block

        private Color GetValueColor(string key, string value)
        {
            // Color-code temperature values
            if (key.Contains("Temp") && value.Contains("°C"))
            {
                if (float.TryParse(value.Replace("°C", ""), out float temp))
                {
                    if (temp >= 85) return Color.FromArgb(255, 80, 70);    // red
                    if (temp >= 70) return Color.FromArgb(255, 165, 40);   // orange
                    if (temp >= 55) return Color.FromArgb(230, 210, 50);   // yellow
                    return Color.FromArgb(60, 210, 120);                    // green
                }
            }
            // Color-code load
            if (key.Contains("Load") && value.Contains("%"))
            {
                if (float.TryParse(value.Replace("%", ""), out float load))
                {
                    if (load >= 90) return Color.FromArgb(255, 80, 70);
                    if (load >= 70) return Color.FromArgb(255, 165, 40);
                    if (load >= 50) return Color.FromArgb(230, 210, 50);
                    return Color.FromArgb(60, 210, 120);
                }
            }
            return Color.FromArgb(220, 225, 230);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _metricFont?.Dispose();
                _labelFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
