using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
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
            Icon = AppIconProvider.GetAppIcon();
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
            foreach (var pair in OverlayMetricCollector.Collect(_computer, _config))
            {
                _currentValues[pair.Key] = pair.Value;
            }

            RecalcSize();
            RepositionOnScreen();

            if (Visible && IsHandleCreated)
            {
                UpdateOverlay();
            }
        }

        public string BuildOsdText(OverlayDisplayTarget target = OverlayDisplayTarget.Desktop)
        {
            return OverlayTextFormatter.BuildOsdText(_config, _currentValues, target);
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

        private List<OverlayMetricDisplay> GetVisibleMetrics()
        {
            return OverlayTextFormatter.GetVisibleMetrics(_config, _currentValues, OverlayDisplayTarget.Desktop);
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
            foreach (var metric in metrics)
            {
                maxLabelWidth = Math.Max(maxLabelWidth, g.MeasureString(metric.Label.ToUpperInvariant(), _labelFont!, PointF.Empty, StringFormat.GenericTypographic).Width);
                maxValueWidth = Math.Max(maxValueWidth, g.MeasureString(metric.Value, _metricFont!, PointF.Empty, StringFormat.GenericTypographic).Width);
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

            int minX = screen.Left;
            int maxX = Math.Max(minX, screen.Right - Width);
            int minY = screen.Top;
            int maxY = Math.Max(minY, screen.Bottom - Height);

            Location = new Point(
                Math.Clamp(x, minX, maxX),
                Math.Clamp(y, minY, maxY));
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
                foreach (var metric in metrics)
                {
                    string label = metric.Label.ToUpperInvariant();
                    var rowRectLeft = new RectangleF(padding, currentY, Width - (padding * 2), rowHeight);
                    var rowRectRight = new RectangleF(padding, currentY, Width - (padding * 2), rowHeight);

                    DrawText(g, label, _labelFont!, ApplyOpacity(Color.FromArgb(168, 174, 186)), rowRectLeft, fmtLeft, false);
                    DrawText(g, metric.Value, _metricFont!, ApplyOpacity(GetValueColor(metric.Key, metric.Value)), rowRectRight, fmtRight, true);

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
    }
}
