using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace HwMonTray
{
    /// <summary>
    /// Overlay settings with live preview and DPI-aware layout.
    /// </summary>
    public class OverlaySettingsForm : Form
    {
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

        private TextBox _hotkeyBox = null!;
        private int _capturedMods;
        private int _capturedVk;
        private string _capturedDisplay = "";
        private bool _isCapturing;

        private int _selectedCorner;
        private Panel _cornerPicker = null!;
        private TrackBar _marginSlider = null!;
        private Label _marginValue = null!;
        private TrackBar _opacitySlider = null!;
        private Label _opacityValue = null!;
        private TrackBar _fontSlider = null!;
        private Label _fontValue = null!;
        private ComboBox _backgroundModeBox = null!;
        private ComboBox _fontFamilyBox = null!;
        private ComboBox _ramDisplayModeBox = null!;
        private CheckBox _shadowCheck = null!;
        private CheckBox _borderCheck = null!;
        private CheckBox _outlineCheck = null!;
        private TrackBar _outlineThicknessSlider = null!;
        private Label _outlineThicknessValue = null!;
        private CheckBox[] _metricChecks = Array.Empty<CheckBox>();
        private ModernScrollContainer? _scrollContainer;

        public OverlaySettingsForm(OverlayConfig config, Action<OverlayConfig> onSave)
        {
            _config = config;
            _onSave = onSave;
            _capturedMods = config.HotkeyModifiers;
            _capturedVk = config.HotkeyVk;
            _capturedDisplay = config.HotkeyDisplay;
            _selectedCorner = config.Position switch
            {
                "TopLeft" => 0,
                "TopRight" => 1,
                "BottomLeft" => 2,
                "BottomRight" => 3,
                _ => 1
            };

            Text = "OSD Settings";
            ClientSize = new Size(FixedWindowWidth(), 840);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgBase;
            ForeColor = FgPrimary;
            Font = new Font("Segoe UI", 10f);
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(FixedWindowWidth(), 640);
            MaximumSize = new Size(FixedWindowWidth(), 2000);

            BuildUI();
            UpdateAppearanceState();
            RestoreWindowBounds();

            ResizeEnd += (_, _) => PersistWindowBounds();
            FormClosing += (_, _) => PersistWindowBounds();
        }

        private void BuildUI()
        {
            _scrollContainer = new ModernScrollContainer(BgBase, Border, Accent)
            {
                Dock = DockStyle.Fill
            };
            var content = _scrollContainer.Content;

            int y = Ui(16);
            int cardWidth = Ui(350);
            int marginX = Ui(24);

            var hotkeyCard = MakeCard(content, "Hotkey", ref y, Ui(92), cardWidth, marginX);
            _hotkeyBox = new TextBox
            {
                Text = _capturedDisplay,
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Center,
                Location = new Point(Ui(16), Ui(42)),
                Size = new Size(hotkeyCard.Width - Ui(32), Ui(34)),
                BackColor = BgInput,
                ForeColor = Accent,
                Font = new Font("Consolas", 13f, FontStyle.Bold),
                BorderStyle = BorderStyle.FixedSingle
            };
            _hotkeyBox.GotFocus += (_, _) =>
            {
                _isCapturing = true;
                _hotkeyBox.Text = "Press shortcut...";
                _hotkeyBox.ForeColor = Color.FromArgb(255, 195, 70);
            };
            _hotkeyBox.LostFocus += (_, _) =>
            {
                _isCapturing = false;
                _hotkeyBox.ForeColor = Accent;
                _hotkeyBox.Text = string.IsNullOrEmpty(_capturedDisplay) ? _config.HotkeyDisplay : _capturedDisplay;
            };
            _hotkeyBox.KeyDown += OnHotkeyKeyDown;
            hotkeyCard.Controls.Add(_hotkeyBox);

            var posCard = MakeCard(content, "Position", ref y, Ui(126), cardWidth, marginX);
            _cornerPicker = new Panel
            {
                Location = new Point(Ui(16), Ui(38)),
                Size = new Size(Ui(84), Ui(62)),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _cornerPicker.Paint += PaintCornerPicker;
            _cornerPicker.MouseClick += OnCornerPickerClick;
            posCard.Controls.Add(_cornerPicker);

            posCard.Controls.Add(MakeLabel("Pick a corner", Ui(112), Ui(42)));
            posCard.Controls.Add(MakeLabel("Edge margin", Ui(112), Ui(72)));

            _marginValue = MakeValueLabel($"{_config.OffsetX} px", posCard.Width - Ui(68), Ui(72));
            posCard.Controls.Add(_marginValue);
            _marginSlider = MakeSlider(posCard, Ui(112), Ui(92), posCard.Width - Ui(132), 5, 160, Math.Max(_config.OffsetX, _config.OffsetY));
            _marginSlider.ValueChanged += (_, _) =>
            {
                _marginValue.Text = $"{_marginSlider.Value} px";
                ApplyLive();
            };

            var appearanceCard = MakeCard(content, "Appearance", ref y, Ui(356), cardWidth, marginX);
            appearanceCard.Controls.Add(MakeLabel("Background", Ui(16), Ui(42)));
            _backgroundModeBox = MakeComboBox(new Point(Ui(132), Ui(38)), new Size(appearanceCard.Width - Ui(148), Ui(28)));
            _backgroundModeBox.Items.AddRange(new object[] { "Solid background", "No background" });
            _backgroundModeBox.SelectedIndex = _config.HasBackground() ? 0 : 1;
            _backgroundModeBox.SelectedIndexChanged += (_, _) =>
            {
                UpdateAppearanceState();
                ApplyLive();
            };
            appearanceCard.Controls.Add(_backgroundModeBox);

            AddSliderRow(appearanceCard, "Opacity", Ui(80), 30, 100, (int)Math.Round(_config.Opacity * 100f), value => $"{value}%", out _opacitySlider, out _opacityValue);
            _opacitySlider.ValueChanged += (_, _) =>
            {
                _opacityValue.Text = $"{_opacitySlider.Value}%";
                ApplyLive();
            };

            AddSliderRow(appearanceCard, "Text size", Ui(126), 8, 28, (int)Math.Round(_config.FontSize), value => $"{value} pt", out _fontSlider, out _fontValue);
            _fontSlider.ValueChanged += (_, _) =>
            {
                _fontValue.Text = $"{_fontSlider.Value} pt";
                ApplyLive();
            };

            appearanceCard.Controls.Add(MakeLabel("Font", Ui(16), Ui(190)));
            _fontFamilyBox = MakeComboBox(new Point(Ui(132), Ui(186)), new Size(appearanceCard.Width - Ui(148), Ui(28)));
            foreach (var fontName in GetAvailableFontChoices(_config.FontFamily))
            {
                _fontFamilyBox.Items.Add(fontName);
            }
            _fontFamilyBox.SelectedItem = _fontFamilyBox.Items.Cast<object>().FirstOrDefault(item =>
                string.Equals(item.ToString(), _config.FontFamily, StringComparison.OrdinalIgnoreCase))
                ?? _fontFamilyBox.Items.Cast<object>().FirstOrDefault(item =>
                    string.Equals(item.ToString(), "Segoe UI", StringComparison.OrdinalIgnoreCase));
            _fontFamilyBox.SelectedIndexChanged += (_, _) => ApplyLive();
            appearanceCard.Controls.Add(_fontFamilyBox);

            _shadowCheck = MakeCheckBox("Enable text shadow", Ui(16), Ui(232), _config.ShowTextShadow);
            _shadowCheck.CheckedChanged += (_, _) => ApplyLive();
            appearanceCard.Controls.Add(_shadowCheck);

            _borderCheck = MakeCheckBox("Show panel border", Ui(190), Ui(232), _config.ShowBorder);
            _borderCheck.CheckedChanged += (_, _) => ApplyLive();
            appearanceCard.Controls.Add(_borderCheck);

            _outlineCheck = MakeCheckBox("Enable text outline", Ui(16), Ui(264), _config.ShowTextOutline);
            _outlineCheck.CheckedChanged += (_, _) =>
            {
                UpdateAppearanceState();
                ApplyLive();
            };
            appearanceCard.Controls.Add(_outlineCheck);

            AddSliderRow(
                appearanceCard,
                "Outline thickness",
                Ui(292),
                1,
                6,
                Math.Clamp(_config.TextOutlineThickness, 1, 6),
                value => $"{value}px",
                out _outlineThicknessSlider,
                out _outlineThicknessValue);
            _outlineThicknessSlider.ValueChanged += (_, _) =>
            {
                _outlineThicknessValue.Text = $"{_outlineThicknessSlider.Value}px";
                ApplyLive();
            };

            var ramCard = MakeCard(content, "RAM Display", ref y, Ui(96), cardWidth, marginX);
            ramCard.Controls.Add(MakeLabel("Show RAM as", Ui(16), Ui(42)));
            _ramDisplayModeBox = MakeComboBox(new Point(Ui(132), Ui(38)), new Size(ramCard.Width - Ui(148), Ui(28)));
            _ramDisplayModeBox.Items.AddRange(new object[] { "Used / Total GB", "Percentage" });
            _ramDisplayModeBox.SelectedIndex = _config.ShowRamAsPercentage() ? 1 : 0;
            _ramDisplayModeBox.SelectedIndexChanged += (_, _) => ApplyLive();
            ramCard.Controls.Add(_ramDisplayModeBox);

            int metricRowHeight = Ui(30);
            int metricsHeight = Ui(40) + (_config.Metrics.Count * metricRowHeight) + Ui(10);
            var metricsCard = MakeCard(content, "Metrics", ref y, metricsHeight, cardWidth, marginX);
            _metricChecks = new CheckBox[_config.Metrics.Count];

            for (int i = 0; i < _config.Metrics.Count; i++)
            {
                var metric = _config.Metrics[i];
                var check = MakeCheckBox(metric.Label, Ui(16), Ui(40) + (i * metricRowHeight), metric.Enabled);
                check.Width = metricsCard.Width - Ui(32);
                check.CheckedChanged += (_, _) => ApplyLive();
                _metricChecks[i] = check;
                metricsCard.Controls.Add(check);
            }

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Ui(62),
                BackColor = Color.FromArgb(16, 16, 20)
            };
            bottom.Paint += (_, e) =>
            {
                using var pen = new Pen(Border);
                e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };

            var saveButton = MakeButton("Save", Ui(24), Ui(14), Ui(104), Ui(34), AccentGreen, Color.FromArgb(10, 10, 10), FontStyle.Bold);
            saveButton.Click += (_, _) =>
            {
                CommitConfig();
                _onSave(_config);
            };

            var closeButton = MakeButton("Close", Ui(136), Ui(14), Ui(84), Ui(34), Color.FromArgb(50, 52, 60), FgPrimary, FontStyle.Regular);
            closeButton.Click += (_, _) => Close();

            var liveLabel = new Label
            {
                Text = "Live preview",
                AutoSize = true,
                ForeColor = AccentGreen,
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(Ui(286), Ui(21)),
                BackColor = Color.Transparent
            };

            bottom.Controls.Add(saveButton);
            bottom.Controls.Add(closeButton);
            bottom.Controls.Add(liveLabel);

            Controls.Add(_scrollContainer);
            Controls.Add(bottom);
        }

        private void PaintCornerPicker(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int width = _cornerPicker.Width;
            int height = _cornerPicker.Height;
            int boxWidth = Ui(32);
            int boxHeight = Ui(24);
            int gap = Ui(6);

            using (var pen = new Pen(Color.FromArgb(60, 65, 75), Math.Max(1, Ui(1))))
            {
                g.DrawRoundedRectangle(pen, 1, 1, width - 3, height - 3, Ui(4));
            }

            var positions = new[]
            {
                new Point(gap, gap),
                new Point(width - boxWidth - gap, gap),
                new Point(gap, height - boxHeight - gap),
                new Point(width - boxWidth - gap, height - boxHeight - gap)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                var rect = new Rectangle(positions[i].X, positions[i].Y, boxWidth, boxHeight);
                using var brush = new SolidBrush(i == _selectedCorner ? Accent : Color.FromArgb(48, 50, 58));
                g.FillRoundedRectangle(brush, rect, Ui(4));
            }
        }

        private void OnCornerPickerClick(object? sender, MouseEventArgs e)
        {
            bool left = e.X < _cornerPicker.Width / 2;
            bool top = e.Y < _cornerPicker.Height / 2;

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

        private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_isCapturing)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            {
                return;
            }

            int mods = 0;
            string display = "";
            if (e.Control) { mods |= 0x0002; display += "Ctrl+"; }
            if (e.Alt) { mods |= 0x0001; display += "Alt+"; }
            if (e.Shift) { mods |= 0x0004; display += "Shift+"; }

            if (mods == 0)
            {
                _hotkeyBox.Text = "Need a modifier key";
                return;
            }

            display += e.KeyCode;
            _capturedMods = mods;
            _capturedVk = (int)e.KeyCode;
            _capturedDisplay = display;
            _hotkeyBox.Text = display;
            _hotkeyBox.ForeColor = AccentGreen;
            _isCapturing = false;
        }

        private void ApplyLive()
        {
            CommitConfig();
            _onSave(_config);
        }

        private void CommitConfig()
        {
            _config.HotkeyDisplay = string.IsNullOrWhiteSpace(_capturedDisplay) ? _config.HotkeyDisplay : _capturedDisplay;
            _config.HotkeyModifiers = _capturedMods == 0 ? _config.HotkeyModifiers : _capturedMods;
            _config.HotkeyVk = _capturedVk == 0 ? _config.HotkeyVk : _capturedVk;

            _config.Position = _selectedCorner switch
            {
                0 => "TopLeft",
                1 => "TopRight",
                2 => "BottomLeft",
                3 => "BottomRight",
                _ => "TopRight"
            };

            int margin = _marginSlider.Value;
            _config.OffsetX = margin;
            _config.OffsetY = margin;
            _config.Opacity = _opacitySlider.Value / 100f;
            _config.FontSize = _fontSlider.Value;
            _config.FontFamily = _fontFamilyBox.SelectedItem?.ToString() ?? _config.FontFamily;
            _config.BackgroundMode = _backgroundModeBox.SelectedIndex == 1
                ? OverlayConfig.BackgroundNone
                : OverlayConfig.BackgroundSolid;
            _config.ShowTextShadow = _shadowCheck.Checked;
            _config.ShowBorder = _borderCheck.Checked;
            _config.ShowTextOutline = _outlineCheck.Checked;
            _config.TextOutlineThickness = _outlineThicknessSlider.Value;
            _config.RamDisplayMode = _ramDisplayModeBox.SelectedIndex == 1
                ? OverlayConfig.RamDisplayPercentage
                : OverlayConfig.RamDisplayUsedAndTotal;

            for (int i = 0; i < _metricChecks.Length && i < _config.Metrics.Count; i++)
            {
                _config.Metrics[i].Enabled = _metricChecks[i].Checked;
            }
        }

        private void UpdateAppearanceState()
        {
            bool hasBackground = _backgroundModeBox.SelectedIndex != 1;
            _borderCheck.Enabled = hasBackground;
            _borderCheck.ForeColor = hasBackground ? FgSecondary : Color.FromArgb(95, 100, 110);

            bool outlineEnabled = _outlineCheck.Checked;
            _outlineThicknessSlider.Enabled = outlineEnabled;
            _outlineThicknessValue.ForeColor = outlineEnabled ? FgPrimary : Color.FromArgb(95, 100, 110);
        }

        private Panel MakeCard(Control parent, string title, ref int y, int height, int width, int marginX)
        {
            var card = new Panel
            {
                Location = new Point(marginX, y),
                Size = new Size(width, height),
                BackColor = BgCard
            };
            card.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                using var pen = new Pen(Border, Math.Max(1, Ui(1)));
                g.DrawRoundedRectangle(pen, 0, 0, card.Width - 1, card.Height - 1, Ui(6));

                using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                using var titleBrush = new SolidBrush(Accent);
                g.DrawString(title.ToUpperInvariant(), titleFont, titleBrush, Ui(14), Ui(10));
            };
            card.Region = CreateRoundedRegion(card.Width, card.Height, Ui(6));
            parent.Controls.Add(card);
            y += height + Ui(16);
            return card;
        }

        private Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = FgSecondary,
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.Transparent
            };
        }

        private Label MakeValueLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(Ui(54), Ui(20)),
                ForeColor = FgPrimary,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
        }

        private ComboBox MakeComboBox(Point location, Size size)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Location = location,
                Size = size,
                BackColor = BgInput,
                ForeColor = FgPrimary,
                Font = new Font("Segoe UI", 9.5f)
            };
        }

        private CheckBox MakeCheckBox(string text, int x, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Checked = isChecked,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = FgSecondary,
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
        }

        private void AddSliderRow(
            Panel card,
            string label,
            int y,
            int min,
            int max,
            int value,
            Func<int, string> format,
            out TrackBar slider,
            out Label valueLabel)
        {
            card.Controls.Add(MakeLabel(label, Ui(16), y + Ui(2)));

            valueLabel = MakeValueLabel(format(value), card.Width - Ui(64), y + Ui(2));
            card.Controls.Add(valueLabel);

            slider = MakeSlider(card, Ui(16), y + Ui(22), card.Width - Ui(36), min, max, value);
        }

        private TrackBar MakeSlider(Control parent, int x, int y, int width, int min, int max, int value)
        {
            var slider = new TrackBar
            {
                Location = new Point(x, y),
                AutoSize = false,
                Size = new Size(width, Ui(22)),
                Minimum = min,
                Maximum = max,
                Value = Math.Clamp(value, min, max),
                TickStyle = TickStyle.None,
                BackColor = BgCard
            };
            parent.Controls.Add(slider);
            return slider;
        }

        private Button MakeButton(string text, int x, int y, int width, int height, Color bg, Color fg, FontStyle style)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Font = new Font("Segoe UI", 10f, style),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                Math.Min(bg.R + 20, 255),
                Math.Min(bg.G + 20, 255),
                Math.Min(bg.B + 20, 255));
            return button;
        }

        private IEnumerable<string> GetAvailableFontChoices(string currentFont)
        {
            string[] preferredFonts =
            {
                "Segoe UI",
                "Bahnschrift",
                "Consolas",
                "Verdana",
                "Tahoma",
                "Trebuchet MS",
                "Arial"
            };

            using var fonts = new InstalledFontCollection();
            var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = preferredFonts.Where(installed.Contains).ToList();

            if (!string.IsNullOrWhiteSpace(currentFont) && installed.Contains(currentFont) && !result.Contains(currentFont, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(currentFont);
            }

            if (result.Count == 0)
            {
                result.Add(string.IsNullOrWhiteSpace(currentFont) ? "Segoe UI" : currentFont);
            }

            return result;
        }

        private void RestoreWindowBounds()
        {
            int fixedWidth = FixedWindowWidth();
            if (!_config.HasSavedSettingsWindowBounds())
            {
                Width = fixedWidth;
                return;
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(
                _config.SettingsWindowX,
                _config.SettingsWindowY,
                fixedWidth,
                _config.SettingsWindowHeight);

            bool isVisibleOnAnyScreen = Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(Bounds));
            if (!isVisibleOnAnyScreen)
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        private void PersistWindowBounds()
        {
            if (WindowState != FormWindowState.Normal)
            {
                return;
            }

            _config.SettingsWindowX = Bounds.X;
            _config.SettingsWindowY = Bounds.Y;
            _config.SettingsWindowWidth = FixedWindowWidth();
            _config.SettingsWindowHeight = Bounds.Height;
            _onSave(_config);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _scrollContainer?.RefreshScrollMetrics();
        }

        private int Ui(int logicalPixels)
        {
            float dpi = DeviceDpi > 0 ? DeviceDpi : 96f;
            return Math.Max(1, (int)Math.Round(logicalPixels * (dpi / 96f)));
        }

        private int FixedWindowWidth()
        {
            return Ui(420);
        }

        private static Region CreateRoundedRegion(int width, int height, int radius)
        {
            using var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(width - diameter, 0, diameter, diameter, 270, 90);
            path.AddArc(width - diameter, height - diameter, diameter, diameter, 0, 90);
            path.AddArc(0, height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }
    }

    internal static class GraphicsExtensions
    {
        public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float width, float height, float radius)
        {
            using var path = RoundedRectPath(x, y, width, height, radius);
            g.DrawPath(pen, path);
        }

        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, float radius)
        {
            using var path = RoundedRectPath(rect.X, rect.Y, rect.Width, rect.Height, radius);
            g.FillPath(brush, path);
        }

        private static GraphicsPath RoundedRectPath(float x, float y, float width, float height, float radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ModernScrollContainer : UserControl
    {
        private readonly Panel _content;
        private readonly Color _trackColor;
        private readonly Color _thumbColor;
        private readonly Color _thumbHoverColor;
        private readonly Color _thumbActiveColor;
        private int _scrollOffset;
        private int _maxScroll;
        private bool _draggingThumb;
        private bool _hoveringThumb;
        private int _dragMouseOffset;

        public ModernScrollContainer(Color backColor, Color trackColor, Color thumbColor)
        {
            DoubleBuffered = true;
            BackColor = backColor;
            _trackColor = Color.FromArgb(42, trackColor);
            _thumbColor = thumbColor;
            _thumbHoverColor = ControlPaint.Light(thumbColor, 0.15f);
            _thumbActiveColor = ControlPaint.Light(thumbColor, 0.3f);

            _content = new Panel
            {
                BackColor = backColor,
                Location = Point.Empty
            };

            Controls.Add(_content);

            _content.ControlAdded += (_, e) =>
            {
                if (e.Control != null)
                {
                    HookChild(e.Control);
                }
                RefreshScrollMetrics();
            };
            _content.ControlRemoved += (_, _) => RefreshScrollMetrics();
            Resize += (_, _) => RefreshScrollMetrics();
            MouseWheel += HandleMouseWheel;
            _content.MouseWheel += HandleMouseWheel;
        }

        public Panel Content => _content;

        public void RefreshScrollMetrics()
        {
            int viewportWidth = Math.Max(0, Width - ScrollbarReserveWidth());
            int contentHeight = GetContentHeight();

            _content.Width = viewportWidth;
            _content.Height = Math.Max(Height, contentHeight);

            _maxScroll = Math.Max(0, contentHeight - Height);
            _scrollOffset = Math.Clamp(_scrollOffset, 0, _maxScroll);
            _content.Location = new Point(0, -_scrollOffset);

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_maxScroll <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle trackRect = GetTrackRect();
            using (var trackBrush = new SolidBrush(_trackColor))
            {
                e.Graphics.FillRoundedRectangle(trackBrush, trackRect, Scale(6));
            }

            Rectangle thumbRect = GetThumbRect();
            Color thumbColor = _draggingThumb
                ? _thumbActiveColor
                : _hoveringThumb ? _thumbHoverColor : _thumbColor;

            using var thumbBrush = new SolidBrush(thumbColor);
            e.Graphics.FillRoundedRectangle(thumbBrush, thumbRect, Scale(6));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (_maxScroll <= 0)
            {
                return;
            }

            Rectangle thumbRect = GetThumbRect();
            if (thumbRect.Contains(e.Location))
            {
                _draggingThumb = true;
                _dragMouseOffset = e.Y - thumbRect.Y;
                Capture = true;
                Invalidate();
                return;
            }

            Rectangle trackRect = GetTrackRect();
            if (trackRect.Contains(e.Location))
            {
                int direction = e.Y < thumbRect.Y ? -1 : 1;
                ScrollBy(direction * Math.Max(Height - Scale(40), Scale(80)));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool hover = _maxScroll > 0 && GetThumbRect().Contains(e.Location);
            if (hover != _hoveringThumb)
            {
                _hoveringThumb = hover;
                Invalidate();
            }

            if (!_draggingThumb)
            {
                return;
            }

            Rectangle trackRect = GetTrackRect();
            Rectangle thumbRect = GetThumbRect();
            int scrollRange = Math.Max(1, trackRect.Height - thumbRect.Height);
            int thumbTop = Math.Clamp(e.Y - _dragMouseOffset, trackRect.Top, trackRect.Bottom - thumbRect.Height);
            double ratio = (double)(thumbTop - trackRect.Top) / scrollRange;
            _scrollOffset = (int)Math.Round(ratio * _maxScroll);
            _content.Location = new Point(0, -_scrollOffset);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_draggingThumb)
            {
                _draggingThumb = false;
                Capture = false;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_draggingThumb && _hoveringThumb)
            {
                _hoveringThumb = false;
                Invalidate();
            }
        }

        private void HandleMouseWheel(object? sender, MouseEventArgs e)
        {
            ScrollBy(-Math.Sign(e.Delta) * Scale(48));
        }

        private void ScrollBy(int delta)
        {
            if (_maxScroll <= 0)
            {
                return;
            }

            _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, _maxScroll);
            _content.Location = new Point(0, -_scrollOffset);
            Invalidate();
        }

        private void HookChild(Control control)
        {
            control.MouseWheel += HandleMouseWheel;
            control.LocationChanged += (_, _) => RefreshScrollMetrics();
            control.SizeChanged += (_, _) => RefreshScrollMetrics();

            foreach (Control child in control.Controls)
            {
                HookChild(child);
            }

            control.ControlAdded += (_, e) =>
            {
                if (e.Control != null)
                {
                    HookChild(e.Control);
                }
            };
            control.ControlRemoved += (_, _) => RefreshScrollMetrics();
        }

        private int GetContentHeight()
        {
            int bottom = 0;
            foreach (Control control in _content.Controls)
            {
                bottom = Math.Max(bottom, control.Bottom);
            }

            return bottom + Scale(16);
        }

        private Rectangle GetTrackRect()
        {
            int width = Scale(7);
            int margin = Scale(6);
            return new Rectangle(
                Math.Max(0, Width - width - margin),
                margin,
                width,
                Math.Max(0, Height - (margin * 2)));
        }

        private Rectangle GetThumbRect()
        {
            Rectangle trackRect = GetTrackRect();
            if (_maxScroll <= 0 || trackRect.Height <= 0)
            {
                return Rectangle.Empty;
            }

            int contentHeight = Math.Max(1, _content.Height);
            int thumbHeight = Math.Max(Scale(44), (int)Math.Round((double)trackRect.Height * Height / contentHeight));
            thumbHeight = Math.Min(trackRect.Height, thumbHeight);
            int travel = Math.Max(0, trackRect.Height - thumbHeight);
            int thumbTop = trackRect.Top + (_maxScroll == 0 ? 0 : (int)Math.Round((double)_scrollOffset / _maxScroll * travel));

            return new Rectangle(trackRect.X, thumbTop, trackRect.Width, thumbHeight);
        }

        private int ScrollbarReserveWidth()
        {
            return Scale(18);
        }

        private int Scale(int logicalPixels)
        {
            float dpi = DeviceDpi > 0 ? DeviceDpi : 96f;
            return Math.Max(1, (int)Math.Round(logicalPixels * (dpi / 96f)));
        }
    }
}
