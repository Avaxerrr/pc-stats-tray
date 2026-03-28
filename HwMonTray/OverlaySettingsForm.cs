using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace HwMonTray
{
    /// <summary>
    /// Modern OSD settings dialog with custom-painted controls and live preview.
    /// </summary>
    public class OverlaySettingsForm : Form
    {
        // ── Theme ──
        private static readonly Color BgBase = Color.FromArgb(22, 22, 28);
        private static readonly Color BgCard = Color.FromArgb(30, 32, 38);
        private static readonly Color BgInput = Color.FromArgb(38, 40, 48);
        private static readonly Color FgPrimary = Color.FromArgb(230, 232, 238);
        private static readonly Color FgSecondary = Color.FromArgb(140, 145, 160);
        private static readonly Color Accent = Color.FromArgb(80, 150, 255);
        private static readonly Color AccentGreen = Color.FromArgb(72, 210, 120);
        private static readonly Color Border = Color.FromArgb(50, 52, 60);

        private readonly OverlayConfig _config;
        private readonly Action<OverlayConfig> _onSave;

        // Controls
        private TextBox _hotkeyBox = null!;
        private int _capturedMods, _capturedVk;
        private string _capturedDisplay = "";
        private bool _isCapturing;

        // Position
        private int _selectedCorner; // 0=TL, 1=TR, 2=BL, 3=BR
        private Panel _cornerPicker = null!;
        private TrackBar _marginSlider = null!;
        private Label _marginValue = null!;

        // Appearance
        private TrackBar _opacitySlider = null!;
        private Label _opacityValue = null!;
        private TrackBar _fontSlider = null!;
        private Label _fontValue = null!;
        private CheckBox _borderCheck = null!;

        // Metrics
        private Panel _metricsPanel = null!;
        private CheckBox[] _metricChecks = null!;

        public OverlaySettingsForm(OverlayConfig config, Action<OverlayConfig> onSave)
        {
            _config = config;
            _onSave = onSave;
            _capturedMods = config.HotkeyModifiers;
            _capturedVk = config.HotkeyVk;
            _capturedDisplay = config.HotkeyDisplay;
            _selectedCorner = config.Position switch
            {
                "TopLeft" => 0, "TopRight" => 1,
                "BottomLeft" => 2, "BottomRight" => 3, _ => 1
            };

            Text = "OSD Settings";
            ClientSize = new Size(380, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgBase;
            ForeColor = FgPrimary;
            Font = new Font("Segoe UI", 10f);
            TopMost = true;
            DoubleBuffered = true;

            BuildUI();
        }

        private void BuildUI()
        {
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            int y = 16;
            int cardW = 315; // Account for 380 total width and scrollbar
            int marginX = 24;

            // ── HOTKEY CARD ──
            var hotkeyCard = MakeCard(scroll, "Hotkey", ref y, 90, cardW, marginX);
            _hotkeyBox = new TextBox
            {
                Text = _capturedDisplay,
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Center,
                Location = new Point(16, 40),
                Size = new Size(hotkeyCard.Width - 32, 36),
                BackColor = BgInput,
                ForeColor = Accent,
                Font = new Font("Consolas", 14f, FontStyle.Bold),
                BorderStyle = BorderStyle.None,
                Cursor = Cursors.Hand
            };
            _hotkeyBox.GotFocus += (s, e) =>
            {
                _isCapturing = true;
                _hotkeyBox.Text = "press shortcut…";
                _hotkeyBox.ForeColor = Color.FromArgb(255, 195, 70);
            };
            _hotkeyBox.LostFocus += (s, e) =>
            {
                _isCapturing = false;
                _hotkeyBox.ForeColor = Accent;
                _hotkeyBox.Text = string.IsNullOrEmpty(_capturedDisplay)
                    ? _config.HotkeyDisplay : _capturedDisplay;
            };
            _hotkeyBox.KeyDown += OnHotkeyKeyDown;
            hotkeyCard.Controls.Add(_hotkeyBox);
            y += 8;

            // ── POSITION CARD ──
            var posCard = MakeCard(scroll, "Position", ref y, 120, cardW, marginX);

            // Visual corner picker: 4 clickable squares in a 2x2 grid
            _cornerPicker = new Panel
            {
                Location = new Point(16, 38),
                Size = new Size(72, 56),
                BackColor = Color.Transparent
            };
            _cornerPicker.Paint += PaintCornerPicker;
            _cornerPicker.MouseClick += OnCornerPickerClick;
            _cornerPicker.Cursor = Cursors.Hand;
            posCard.Controls.Add(_cornerPicker);

            // Corner label
            var cornerLabel = new Label
            {
                Text = "← pick a corner",
                Location = new Point(96, 42),
                AutoSize = true,
                ForeColor = FgSecondary,
                Font = new Font("Segoe UI", 9f)
            };
            posCard.Controls.Add(cornerLabel);

            // Edge margin
            var edgeLabel = new Label
            {
                Text = "Edge margin",
                Location = new Point(96, 72),
                AutoSize = true,
                ForeColor = FgSecondary,
                Font = new Font("Segoe UI", 9f)
            };
            posCard.Controls.Add(edgeLabel);

            _marginValue = new Label
            {
                Text = $"{_config.OffsetX} px",
                Location = new Point(posCard.Width - 60, 72),
                Size = new Size(48, 20),
                ForeColor = FgPrimary,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight
            };
            posCard.Controls.Add(_marginValue);

            _marginSlider = MakeSlider(posCard, 96, 92, posCard.Width - 116, 5, 100,
                Math.Max(_config.OffsetX, _config.OffsetY));
            _marginSlider.ValueChanged += (s, e) =>
            {
                _marginValue.Text = $"{_marginSlider.Value} px";
                ApplyLive();
            };
            y += 8;

            // ── APPEARANCE CARD ──
            var appCard = MakeCard(scroll, "Appearance", ref y, 160, cardW, marginX);

            AddSliderRow(appCard, "Opacity", 38, 30, 100, (int)(_config.Opacity * 100),
                v => $"{v}%", out _opacitySlider, out _opacityValue);
            _opacitySlider.ValueChanged += (s, e) =>
            {
                _opacityValue.Text = $"{_opacitySlider.Value}%";
                ApplyLive();
            };

            AddSliderRow(appCard, "Text size", 82, 8, 24, (int)_config.FontSize,
                v => $"{v} pt", out _fontSlider, out _fontValue);
            _fontSlider.ValueChanged += (s, e) =>
            {
                _fontValue.Text = $"{_fontSlider.Value} pt";
                ApplyLive();
            };
            
            _borderCheck = new CheckBox
            {
                Text = "  Show border outline",
                Checked = _config.ShowBorder,
                Location = new Point(16, 126),
                AutoSize = true,
                ForeColor = FgSecondary,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            _borderCheck.CheckedChanged += (s, e) => ApplyLive();
            appCard.Controls.Add(_borderCheck);
            
            y += 8;

            // ── METRICS CARD ──
            int metricCount = _config.Metrics.Count;
            int metricRowH = 32;
            int metricsH = 36 + metricCount * metricRowH + 8;
            var metCard = MakeCard(scroll, "Metrics", ref y, metricsH, cardW, marginX);

            _metricChecks = new CheckBox[metricCount];
            _metricsPanel = new Panel
            {
                Location = new Point(8, 34),
                Size = new Size(metCard.Width - 16, metricCount * metricRowH + 4),
                BackColor = Color.Transparent
            };

            for (int i = 0; i < metricCount; i++)
            {
                var m = _config.Metrics[i];
                string icon = m.Key switch
                {
                    var k when k.Contains("Temp") => "🌡",
                    var k when k.Contains("Load") => "📈",
                    var k when k.Contains("Clock") => "⏱",
                    var k when k.Contains("Power") => "⚡",
                    var k when k.Contains("Ram") || k.Contains("Vram") => "💾",
                    var k when k.Contains("Fan") => "🌀",
                    _ => "•"
                };

                var cb = new CheckBox
                {
                    Text = $"  {icon}   {m.Label}",
                    Checked = m.Enabled,
                    Location = new Point(8, i * metricRowH),
                    Size = new Size(_metricsPanel.Width - 16, metricRowH),
                    ForeColor = FgPrimary,
                    Font = new Font("Segoe UI", 10f),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                cb.FlatAppearance.BorderSize = 0; // Hide default border 
                
                // Custom paint for visible dark mode checkbox
                cb.Paint += (s, e) =>
                {
                    var senderCb = (CheckBox)s!;
                    var g = e.Graphics;
                    g.Clear(BgCard);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    
                    int boxSize = 16;
                    int yBox = (senderCb.Height - boxSize) / 2;
                    var boxRect = new Rectangle(8, yBox, boxSize, boxSize);

                    using (var brush = new SolidBrush(senderCb.Checked ? Accent : BgInput))
                        g.FillRectangle(brush, boxRect);
                        
                    using (var pen = new Pen(Border))
                        g.DrawRectangle(pen, boxRect);

                    if (senderCb.Checked)
                    {
                        using var pen = new Pen(Color.White, 2f);
                        g.DrawLines(pen, new[] {
                            new Point(boxRect.X + 3, boxRect.Y + 8),
                            new Point(boxRect.X + 6, boxRect.Y + 11),
                            new Point(boxRect.X + 12, boxRect.Y + 4)
                        });
                    }
                    
                    using var textBrush = new SolidBrush(FgPrimary);
                    var strFmt = new StringFormat { LineAlignment = StringAlignment.Center };
                    g.DrawString(senderCb.Text, senderCb.Font, textBrush, new RectangleF(32, 0, senderCb.Width - 32, senderCb.Height), strFmt);
                };

                int idx = i;
                cb.CheckedChanged += (s, e) => ApplyLive();
                _metricChecks[i] = cb;
                _metricsPanel.Controls.Add(cb);
            }
            metCard.Controls.Add(_metricsPanel);

            // ── BOTTOM BAR ──
            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Color.FromArgb(16, 16, 20)
            };
            bottom.Paint += (s, e) =>
            {
                using var pen = new Pen(Border);
                e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            var saveBtn = MakeButton("Save", 24, 12, 100, 34, AccentGreen,
                Color.FromArgb(10, 10, 10), FontStyle.Bold);
            saveBtn.Click += (s, e) =>
            {
                CommitConfig();
                _onSave(_config);
            };

            var closeBtn = MakeButton("Close", 132, 12, 80, 34,
                Color.FromArgb(50, 52, 60), FgPrimary, FontStyle.Regular);
            closeBtn.Click += (s, e) => Close();

            var liveIndicator = new Label
            {
                Text = "● live",
                ForeColor = AccentGreen,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(310, 20)
            };

            bottom.Controls.AddRange(new Control[] { saveBtn, closeBtn, liveIndicator });

            Controls.Add(scroll);
            Controls.Add(bottom);
        }

        // ══════════════════════════════════════════════
        //  CORNER PICKER — visual 2×2 grid
        // ══════════════════════════════════════════════

        private void PaintCornerPicker(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = _cornerPicker.Width, h = _cornerPicker.Height;
            int bw = 30, bh = 24, gap = 6;

            // Monitor outline
            using (var pen = new Pen(Color.FromArgb(60, 65, 75), 1.5f))
                g.DrawRoundedRectangle(pen, 1, 1, w - 3, h - 3, 4);

            var positions = new[]
            {
                new Point(gap, gap),                    // TL
                new Point(w - bw - gap, gap),           // TR
                new Point(gap, h - bh - gap),           // BL
                new Point(w - bw - gap, h - bh - gap)   // BR
            };

            for (int i = 0; i < 4; i++)
            {
                var r = new Rectangle(positions[i].X, positions[i].Y, bw, bh);
                if (i == _selectedCorner)
                {
                    using var brush = new SolidBrush(Accent);
                    g.FillRoundedRectangle(brush, r, 3);
                }
                else
                {
                    using var brush = new SolidBrush(Color.FromArgb(48, 50, 58));
                    g.FillRoundedRectangle(brush, r, 3);
                }
            }
        }

        private void OnCornerPickerClick(object? sender, MouseEventArgs e)
        {
            int w = _cornerPicker.Width, h = _cornerPicker.Height;
            bool left = e.X < w / 2;
            bool top = e.Y < h / 2;

            _selectedCorner = (top, left) switch
            {
                (true, true) => 0,
                (true, false) => 1,
                (false, true) => 2,
                (false, false) => 3
            };

            _cornerPicker.Invalidate();
            ApplyLive();
        }

        // ══════════════════════════════════════════════
        //  HOTKEY CAPTURE
        // ══════════════════════════════════════════════

        private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_isCapturing) return;
            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
                or Keys.LWin or Keys.RWin) return;

            int mods = 0;
            string display = "";
            if (e.Control) { mods |= 0x0002; display += "Ctrl+"; }
            if (e.Alt) { mods |= 0x0001; display += "Alt+"; }
            if (e.Shift) { mods |= 0x0004; display += "Shift+"; }

            if (mods == 0)
            {
                _hotkeyBox.Text = "need a modifier key!";
                return;
            }

            display += e.KeyCode.ToString();
            _capturedMods = mods;
            _capturedVk = (int)e.KeyCode;
            _capturedDisplay = display;
            _hotkeyBox.Text = display;
            _hotkeyBox.ForeColor = AccentGreen;
            _isCapturing = false;
        }

        // ══════════════════════════════════════════════
        //  LIVE PREVIEW & CONFIG
        // ══════════════════════════════════════════════

        private void ApplyLive()
        {
            CommitConfig();
            _onSave(_config);
        }

        private void CommitConfig()
        {
            _config.HotkeyDisplay = _capturedDisplay;
            _config.HotkeyModifiers = _capturedMods;
            _config.HotkeyVk = _capturedVk;

            _config.Position = _selectedCorner switch
            {
                0 => "TopLeft", 1 => "TopRight",
                2 => "BottomLeft", 3 => "BottomRight", _ => "TopRight"
            };

            int margin = _marginSlider.Value;
            _config.OffsetX = margin;
            _config.OffsetY = margin;
            _config.Opacity = _opacitySlider.Value / 100f;
            _config.FontSize = _fontSlider.Value;
            _config.ShowBorder = _borderCheck.Checked;

            for (int i = 0; i < _metricChecks.Length && i < _config.Metrics.Count; i++)
                _config.Metrics[i].Enabled = _metricChecks[i].Checked;
        }

        // ══════════════════════════════════════════════
        //  UI HELPERS
        // ══════════════════════════════════════════════

        private Panel MakeCard(Panel parent, string title, ref int y, int height, int cardW, int marginX)
        {
            var card = new Panel
            {
                Location = new Point(marginX, y),
                Size = new Size(cardW, height),
                BackColor = BgCard
            };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                // Card border
                using var pen = new Pen(Border, 1f);
                g.DrawRoundedRectangle(pen, 0, 0, card.Width - 1, card.Height - 1, 6);

                // Title
                using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                using var titleBrush = new SolidBrush(Accent);
                g.DrawString(title.ToUpper(), titleFont, titleBrush, 14, 10);
            };
            card.Region = CreateRoundedRegion(card.Width, card.Height, 6);
            parent.Controls.Add(card);
            y += height + 16;
            return card;
        }

        private void AddSliderRow(Panel card, string label, int y, int min, int max,
            int val, Func<int, string> fmt, out TrackBar slider, out Label valLabel)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(16, y + 2),
                AutoSize = true,
                ForeColor = FgSecondary,
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.Transparent
            };
            card.Controls.Add(lbl);

            valLabel = new Label
            {
                Text = fmt(val),
                Location = new Point(card.Width - 64, y + 2),
                Size = new Size(50, 20),
                ForeColor = FgPrimary,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            card.Controls.Add(valLabel);

            slider = MakeSlider(card, 16, y + 22, card.Width - 36, min, max, val);
        }

        private TrackBar MakeSlider(Panel parent, int x, int y, int w, int min, int max, int val)
        {
            var slider = new TrackBar
            {
                Location = new Point(x, y),
                AutoSize = false,
                Size = new Size(w, 20),
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(val, min, max),
                TickStyle = TickStyle.None,
                BackColor = BgCard
            };
            parent.Controls.Add(slider);
            return slider;
        }

        private Button MakeButton(string text, int x, int y, int w, int h,
            Color bg, Color fg, FontStyle style)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI", 10f, style),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(Math.Min(bg.R + 20, 255), Math.Min(bg.G + 20, 255), Math.Min(bg.B + 20, 255));
            return btn;
        }

        private static Region CreateRoundedRegion(int w, int h, int r)
        {
            using var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(w - d, 0, d, d, 270, 90);
            path.AddArc(w - d, h - d, d, d, 0, 90);
            path.AddArc(0, h - d, d, d, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }
    }

    // ══════════════════════════════════════════════
    //  Extension for drawing rounded rects on Graphics
    // ══════════════════════════════════════════════
    internal static class GraphicsExtensions
    {
        public static void DrawRoundedRectangle(this Graphics g, Pen pen,
            float x, float y, float w, float h, float r)
        {
            using var path = RoundedRectPath(x, y, w, h, r);
            g.DrawPath(pen, path);
        }

        public static void FillRoundedRectangle(this Graphics g, Brush brush,
            Rectangle rect, float r)
        {
            using var path = RoundedRectPath(rect.X, rect.Y, rect.Width, rect.Height, r);
            g.FillPath(brush, path);
        }

        private static GraphicsPath RoundedRectPath(float x, float y, float w, float h, float r)
        {
            float d = r * 2;
            var path = new GraphicsPath();
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
