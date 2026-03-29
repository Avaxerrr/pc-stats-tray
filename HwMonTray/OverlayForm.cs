using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace HwMonTray
{
    /// <summary>
    /// A borderless, click-through overlay that renders via UpdateLayeredWindow.
    /// </summary>
    public class OverlayForm : Form
    {
        #region Win32 API

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref Point pptDst,
            ref Size psize,
            IntPtr hdcSrc,
            ref Point pprSrc,
            uint crKey,
            ref BLENDFUNCTION pblend,
            uint dwFlags);

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

        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_DPICHANGED = 0x02E0;

        private readonly Computer _computer;
        private OverlayConfig _config;
        private readonly Dictionary<string, string> _currentValues = new();

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
            AutoScaleMode = AutoScaleMode.Dpi;
            SetStyle(ControlStyles.Selectable, false);

            ApplyConfig();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void ApplyConfig()
        {
            _metricFont?.Dispose();
            _labelFont?.Dispose();

            _metricFont = CreateOverlayFont(_config.FontFamily, _config.FontSize, FontStyle.Bold);
            _labelFont = CreateOverlayFont(_config.FontFamily, Math.Max(8f, _config.FontSize * 0.75f), FontStyle.Bold);

            RecalcSize();
            RepositionOnScreen();

            if (Visible && IsHandleCreated)
            {
                UpdateOverlay();
            }
        }

        public void UpdateConfig(OverlayConfig config)
        {
            _config = config;
            ApplyConfig();
        }

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

            CollectFanMetrics();

            RecalcSize();
            RepositionOnScreen();

            if (Visible && IsHandleCreated)
            {
                UpdateOverlay();
            }
        }

        public string BuildOsdText()
        {
            var metrics = GetVisibleMetrics();
            if (metrics.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var (label, value, _) in metrics)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(label);
                builder.Append(": ");
                builder.Append(value);
            }

            return builder.ToString();
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

        protected override void OnPaint(PaintEventArgs e)
        {
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_DPICHANGED && IsHandleCreated)
            {
                BeginInvoke((Action)(() =>
                {
                    ApplyConfig();
                    RefreshData();
                }));
            }
        }

        private void CollectCpuMetrics(IHardware hw)
        {
            var temp = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core Max"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Package"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp?.Value.HasValue == true)
            {
                _currentValues["CpuTemp"] = $"{temp.Value.Value:0}\u00B0C";
            }

            var load = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            if (load?.Value.HasValue == true)
            {
                _currentValues["CpuLoad"] = $"{load.Value.Value:0}%";
            }

            var clocks = hw.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue && s.Name.Contains("Core")).ToList();
            if (clocks.Count > 0)
            {
                _currentValues["CpuClock"] = $"{clocks.Max(s => s.Value!.Value):0} MHz";
            }

            var power = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"))
                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
            if (power?.Value.HasValue == true)
            {
                _currentValues["CpuPower"] = $"{power.Value.Value:0.#} W";
            }
        }

        private void CollectGpuMetrics(IHardware hw)
        {
            var temp = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            if (temp?.Value.HasValue == true)
            {
                _currentValues["GpuTemp"] = $"{temp.Value.Value:0}\u00B0C";
            }

            var load = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core"))
                    ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
            if (load?.Value.HasValue == true)
            {
                _currentValues["GpuLoad"] = $"{load.Value.Value:0}%";
            }

            var clock = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock);
            if (clock?.Value.HasValue == true)
            {
                _currentValues["GpuClock"] = $"{clock.Value.Value:0} MHz";
            }

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

            var power = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power);
            if (power?.Value.HasValue == true)
            {
                _currentValues["GpuPower"] = $"{power.Value.Value:0.#} W";
            }
        }

        private void CollectRamMetrics(IHardware hw)
        {
            var used = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Used"));
            var avail = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Available"));
            if (used?.Value.HasValue != true)
            {
                return;
            }

            float usedGb = used.Value!.Value;
            if (avail?.Value.HasValue == true)
            {
                float totalGb = usedGb + avail.Value!.Value;
                if (_config.ShowRamAsPercentage())
                {
                    float usedPercent = totalGb > 0 ? (usedGb / totalGb) * 100f : 0f;
                    _currentValues["RamUsage"] = $"{usedPercent:0}%";
                }
                else
                {
                    _currentValues["RamUsage"] = $"{usedGb:0.#} / {totalGb:0.#} GB";
                }
            }
            else
            {
                _currentValues["RamUsage"] = $"{usedGb:0.#} GB";
            }
        }

        private void CollectFanMetrics()
        {
            ISensor? cpuFan = null;
            ISensor? gpuFan = null;
            ISensor? caseFan = null;

            var fanSensors = new Dictionary<string, ISensor>(StringComparer.OrdinalIgnoreCase);
            foreach (var hardware in _computer.Hardware)
            {
                CollectFanSensorsByKeyRecursive(hardware, fanSensors);
            }

            cpuFan = ResolveConfiguredFan(_config.CpuFanSensorKey, fanSensors);
            gpuFan = ResolveConfiguredFan(_config.GpuFanSensorKey, fanSensors);
            caseFan = ResolveConfiguredFan(_config.CaseFanSensorKey, fanSensors);

            foreach (var hardware in _computer.Hardware)
            {
                CollectFanMetricsRecursive(hardware, ref cpuFan, ref gpuFan, ref caseFan);
            }

            if (cpuFan?.Value.HasValue == true)
            {
                _currentValues["CpuFan"] = $"{cpuFan.Value.Value:0} RPM";
            }

            if (gpuFan?.Value.HasValue == true)
            {
                _currentValues["GpuFan"] = $"{gpuFan.Value.Value:0} RPM";
            }

            if (caseFan?.Value.HasValue == true)
            {
                _currentValues["CaseFan"] = $"{caseFan.Value.Value:0} RPM";
            }
        }

        private void CollectFanSensorsByKeyRecursive(IHardware hardware, Dictionary<string, ISensor> fanSensors)
        {
            foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Fan))
            {
                fanSensors[SensorIdentity.GetSensorKey(sensor)] = sensor;
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectFanSensorsByKeyRecursive(subHardware, fanSensors);
            }
        }

        private ISensor? ResolveConfiguredFan(string sensorKey, Dictionary<string, ISensor> fanSensors)
        {
            if (string.IsNullOrWhiteSpace(sensorKey))
            {
                return null;
            }

            if (fanSensors.TryGetValue(sensorKey, out var sensor) && sensor.Value.HasValue)
            {
                return sensor;
            }

            return null;
        }

        private void CollectFanMetricsRecursive(IHardware hardware, ref ISensor? cpuFan, ref ISensor? gpuFan, ref ISensor? caseFan)
        {
            foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue))
            {
                AssignFanSensor(hardware, sensor, ref cpuFan, ref gpuFan, ref caseFan);
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectFanMetricsRecursive(subHardware, ref cpuFan, ref gpuFan, ref caseFan);
            }
        }

        private void AssignFanSensor(IHardware hardware, ISensor sensor, ref ISensor? cpuFan, ref ISensor? gpuFan, ref ISensor? caseFan)
        {
            FanRole role = ClassifyFanSensor(hardware, sensor);
            switch (role)
            {
                case FanRole.Cpu when cpuFan == null:
                    cpuFan = sensor;
                    break;
                case FanRole.Gpu when gpuFan == null:
                    gpuFan = sensor;
                    break;
                case FanRole.Case when caseFan == null:
                    caseFan = sensor;
                    break;
            }
        }

        private FanRole ClassifyFanSensor(IHardware hardware, ISensor sensor)
        {
            string sensorName = sensor.Name ?? string.Empty;
            string hardwareName = hardware.Name ?? string.Empty;
            string parentName = hardware.Parent?.Name ?? string.Empty;
            string combined = $"{sensorName} {hardwareName} {parentName}".ToLowerInvariant();

            if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel ||
                combined.Contains("gpu") || combined.Contains("geforce") || combined.Contains("radeon"))
            {
                return FanRole.Gpu;
            }

            if (sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                sensorName.Contains("Processor", StringComparison.OrdinalIgnoreCase) ||
                hardware.HardwareType == HardwareType.Cpu)
            {
                return FanRole.Cpu;
            }

            if (combined.Contains("chassis") ||
                combined.Contains("case") ||
                combined.Contains("system fan") ||
                combined.Contains("sys fan") ||
                combined.Contains("aux fan") ||
                combined.Contains("rear fan") ||
                combined.Contains("front fan"))
            {
                return FanRole.Case;
            }

            if (hardware.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController)
            {
                return FanRole.Case;
            }

            return FanRole.Unknown;
        }

        private List<(string label, string value, string key)> GetVisibleMetrics()
        {
            var result = new List<(string, string, string)>();
            foreach (var metric in _config.Metrics)
            {
                if (!metric.Enabled)
                {
                    continue;
                }

                string value = _currentValues.TryGetValue(metric.Key, out var currentValue) ? currentValue : "--";
                result.Add((metric.Label, value, metric.Key));
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

            using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            bmp.SetResolution(GetCurrentDpi(), GetCurrentDpi());

            using var g = Graphics.FromImage(bmp);
            ConfigureGraphics(g);

            int padding = ScaleInt(16f);
            int gap = ScaleInt(14f);
            int rowHeight = GetRowHeight(g);
            int contentHeight = (metrics.Count * rowHeight) + (padding * 2);

            float maxLabelWidth = 0f;
            float maxValueWidth = 0f;
            foreach (var (label, value, _) in metrics)
            {
                maxLabelWidth = Math.Max(maxLabelWidth, g.MeasureString(label.ToUpperInvariant(), _labelFont!, PointF.Empty, StringFormat.GenericTypographic).Width);
                maxValueWidth = Math.Max(maxValueWidth, g.MeasureString(value, _metricFont!, PointF.Empty, StringFormat.GenericTypographic).Width);
            }

            int contentWidth = (int)Math.Ceiling(maxLabelWidth + gap + maxValueWidth + (padding * 2));
            contentWidth = Math.Max(contentWidth, ScaleInt(180f));

            Size = new Size(contentWidth, contentHeight);
        }

        private void RepositionOnScreen()
        {
            var screen = IsHandleCreated ? Screen.FromHandle(Handle).WorkingArea : Screen.PrimaryScreen!.WorkingArea;
            var position = _config.GetPosition();
            int offsetX = ScaleInt(_config.OffsetX);
            int offsetY = ScaleInt(_config.OffsetY);

            int x = position switch
            {
                OverlayPosition.TopLeft or OverlayPosition.BottomLeft => screen.Left + offsetX,
                _ => screen.Right - Width - offsetX
            };

            int y = position switch
            {
                OverlayPosition.TopLeft or OverlayPosition.TopRight => screen.Top + offsetY,
                _ => screen.Bottom - Height - offsetY
            };

            Location = new Point(x, y);
        }

        private void UpdateOverlay()
        {
            var metrics = GetVisibleMetrics();
            if (metrics.Count == 0 || Width <= 0 || Height <= 0)
            {
                return;
            }

            using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            bmp.SetResolution(GetCurrentDpi(), GetCurrentDpi());

            using (var g = Graphics.FromImage(bmp))
            {
                ConfigureGraphics(g);

                int padding = ScaleInt(16f);
                int radius = ScaleInt(12f);
                int rowHeight = GetRowHeight(g);
                int borderWidth = Math.Max(1, ScaleInt(1f));

                if (_config.HasBackground())
                {
                    var bgRect = new Rectangle(0, 0, Width - 1, Height - 1);
                    using var bgPath = RoundedRect(bgRect, radius);
                    using var bgBrush = new SolidBrush(ApplyOpacity(Color.FromArgb(255, 12, 12, 15)));
                    g.FillPath(bgBrush, bgPath);

                    if (_config.ShowBorder)
                    {
                        using var borderPen = new Pen(ApplyOpacity(Color.FromArgb(78, 255, 255, 255)), borderWidth);
                        g.DrawPath(borderPen, bgPath);
                    }
                }

                using var fmtLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                using var fmtRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

                int currentY = padding;
                foreach (var (rawLabel, value, key) in metrics)
                {
                    string label = rawLabel.ToUpperInvariant();
                    var rowRectLeft = new RectangleF(padding, currentY, Width - (padding * 2), rowHeight);
                    var rowRectRight = new RectangleF(padding, currentY, Width - (padding * 2), rowHeight);

                    DrawText(g, label, _labelFont!, ApplyOpacity(Color.FromArgb(168, 174, 186)), rowRectLeft, fmtLeft, false);
                    DrawText(g, value, _metricFont!, ApplyOpacity(GetValueColor(key, value)), rowRectRight, fmtRight, true);

                    currentY += rowHeight;
                }
            }

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
                    SourceConstantAlpha = 255,
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

        private void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringFormat format, bool isValue)
        {
            bool hasShadow = _config.ShowTextShadow;
            bool hasOutline = _config.ShowTextOutline;

            if (!hasShadow && !hasOutline)
            {
                using var plainTextBrush = new SolidBrush(color);
                g.DrawString(text, font, plainTextBrush, rect, format);
                return;
            }

            using var textPath = CreateTextPath(g, text, font, rect, format);

            if (hasShadow)
            {
                DrawTextShadow(g, textPath, isValue);
            }

            if (hasOutline)
            {
                float outlineWidth = Math.Max(ScaleFloat(1f), ScaleFloat(Math.Clamp(_config.TextOutlineThickness, 1, 6)));
                int outlineAlpha = _config.HasBackground()
                    ? (isValue ? 150 : 138)
                    : (isValue ? 220 : 205);

                using var outlinePen = new Pen(ApplyOpacity(Color.FromArgb(outlineAlpha, 0, 0, 0)), outlineWidth)
                {
                    LineJoin = LineJoin.Round
                };
                g.DrawPath(outlinePen, textPath);
            }

            using var textBrush = new SolidBrush(color);
            g.FillPath(textBrush, textPath);
        }

        private void DrawTextShadow(Graphics g, GraphicsPath textPath, bool isValue)
        {
            float primaryOffset = _config.HasBackground() ? ScaleFloat(1.0f) : ScaleFloat(1.35f);
            float secondaryOffset = _config.HasBackground() ? ScaleFloat(1.8f) : ScaleFloat(2.4f);

            using var primaryShadow = (GraphicsPath)textPath.Clone();
            using (var translate = new Matrix())
            {
                translate.Translate(primaryOffset, primaryOffset);
                primaryShadow.Transform(translate);
            }

            int primaryFillAlpha = _config.HasBackground()
                ? (isValue ? 48 : 40)
                : (isValue ? 78 : 68);
            int primaryStrokeAlpha = _config.HasBackground()
                ? (isValue ? 28 : 22)
                : (isValue ? 44 : 38);

            using (var shadowBrush = new SolidBrush(ApplyOpacity(Color.FromArgb(primaryFillAlpha, 0, 0, 0))))
            using (var shadowPen = new Pen(ApplyOpacity(Color.FromArgb(primaryStrokeAlpha, 0, 0, 0)), _config.HasBackground() ? ScaleFloat(1.4f) : ScaleFloat(2.1f))
            {
                LineJoin = LineJoin.Round
            })
            {
                g.FillPath(shadowBrush, primaryShadow);
                g.DrawPath(shadowPen, primaryShadow);
            }

            if (_config.HasBackground())
            {
                return;
            }

            using var secondaryShadow = (GraphicsPath)textPath.Clone();
            using (var translate = new Matrix())
            {
                translate.Translate(secondaryOffset, secondaryOffset);
                secondaryShadow.Transform(translate);
            }

            using var secondaryBrush = new SolidBrush(ApplyOpacity(Color.FromArgb(isValue ? 34 : 28, 0, 0, 0)));
            using var secondaryPen = new Pen(ApplyOpacity(Color.FromArgb(isValue ? 18 : 14, 0, 0, 0)), ScaleFloat(2.8f))
            {
                LineJoin = LineJoin.Round
            };
            g.FillPath(secondaryBrush, secondaryShadow);
            g.DrawPath(secondaryPen, secondaryShadow);
        }

        private GraphicsPath CreateTextPath(Graphics g, string text, Font font, RectangleF rect, StringFormat format)
        {
            var path = new GraphicsPath();
            float emSize = g.DpiY * font.SizeInPoints / 72f;
            path.AddString(text, font.FontFamily, (int)font.Style, emSize, Rectangle.Round(rect), format);
            return path;
        }

        private Color GetValueColor(string key, string value)
        {
            if (key.Contains("Temp") && value.Contains("\u00B0C") &&
                float.TryParse(value.Replace("\u00B0C", string.Empty), out float temp))
            {
                if (temp >= 85) return Color.FromArgb(255, 80, 70);
                if (temp >= 70) return Color.FromArgb(255, 165, 40);
                if (temp >= 55) return Color.FromArgb(230, 210, 50);
                return Color.FromArgb(60, 210, 120);
            }

            if (key.Contains("Load") && value.Contains("%") &&
                float.TryParse(value.Replace("%", string.Empty), out float load))
            {
                if (load >= 90) return Color.FromArgb(255, 80, 70);
                if (load >= 70) return Color.FromArgb(255, 165, 40);
                if (load >= 50) return Color.FromArgb(230, 210, 50);
                return Color.FromArgb(60, 210, 120);
            }

            return Color.FromArgb(220, 225, 230);
        }

        private Color ApplyOpacity(Color color)
        {
            int alpha = (int)Math.Round(color.A * Math.Clamp(_config.Opacity, 0f, 1f));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        private Font CreateOverlayFont(string familyName, float size, FontStyle style)
        {
            try
            {
                return new Font(familyName, size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
        }

        private void ConfigureGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        private int GetRowHeight(Graphics g)
        {
            float fontHeight = Math.Max(_metricFont!.GetHeight(g), _labelFont!.GetHeight(g));
            return (int)Math.Ceiling(fontHeight + ScaleFloat(6f));
        }

        private float GetCurrentDpi()
        {
            return IsHandleCreated ? DeviceDpi : 96f;
        }

        private float GetDpiScale()
        {
            return GetCurrentDpi() / 96f;
        }

        private int ScaleInt(float value)
        {
            return Math.Max(1, (int)Math.Round(value * GetDpiScale()));
        }

        private int ScaleInt(int value)
        {
            return ScaleInt((float)value);
        }

        private float ScaleFloat(float value)
        {
            return value * GetDpiScale();
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
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

        private enum FanRole
        {
            Unknown,
            Cpu,
            Gpu,
            Case
        }
    }
}
