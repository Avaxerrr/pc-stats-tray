using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
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
        private readonly Computer _computer;
        private readonly List<FanSensorOption> _fanSensorOptions;

        private TextBox _toggleAllHotkeyBox = null!;
        private TextBox _toggleDesktopHotkeyBox = null!;
        private TextBox _toggleRtssHotkeyBox = null!;
        private TextBox _settingsHotkeyBox = null!;
        private HotkeyBinding _capturedToggleAllHotkey;
        private HotkeyBinding _capturedToggleDesktopHotkey;
        private HotkeyBinding _capturedToggleRtssHotkey;
        private HotkeyBinding _capturedSettingsHotkey;
        private TextBox? _activeHotkeyBox;
        private HotkeyCaptureTarget _activeHotkeyTarget;
        private bool _isCapturing;

        private bool _alignRight;
        private bool _alignBottom;
        private CheckBox _enabledCheck = null!;
        private CheckBox _desktopOverlayCheck = null!;
        private CheckBox _rtssOverlayCheck = null!;
        private Button _copyRtssDebugButton = null!;
        private RichTextBox _outputStatusBox = null!;
        private Button _horizontalLeftButton = null!;
        private Button _horizontalRightButton = null!;
        private Button _verticalTopButton = null!;
        private Button _verticalBottomButton = null!;
        private TrackBar _horizontalMarginSlider = null!;
        private Label _horizontalMarginValue = null!;
        private TrackBar _verticalMarginSlider = null!;
        private Label _verticalMarginValue = null!;
        private TrackBar _opacitySlider = null!;
        private Label _opacityValue = null!;
        private TrackBar _fontSlider = null!;
        private Label _fontValue = null!;
        private ComboBox _backgroundModeBox = null!;
        private ComboBox _fontFamilyBox = null!;
        private ComboBox _ramDisplayModeBox = null!;
        private ComboBox _vramDisplayModeBox = null!;
        private ComboBox _cpuFanSensorBox = null!;
        private ComboBox _gpuFanSensorBox = null!;
        private ComboBox _caseFanSensorBox = null!;
        private CheckBox _shadowCheck = null!;
        private CheckBox _borderCheck = null!;
        private CheckBox _outlineCheck = null!;
        private TrackBar _outlineThicknessSlider = null!;
        private Label _outlineThicknessValue = null!;
        private CheckBox[] _metricChecks = Array.Empty<CheckBox>();
        private ModernScrollContainer? _scrollContainer;
        private readonly System.Windows.Forms.Timer _statusTimer;

        public OverlaySettingsForm(Computer computer, OverlayConfig config, Action<OverlayConfig> onSave)
        {
            _computer = computer;
            _config = config;
            _onSave = onSave;
            _computer.Accept(new UpdateVisitor());
            _fanSensorOptions = SensorIdentity.GetFanSensorOptions(computer);
            _capturedToggleAllHotkey = HotkeyBinding.FromStored(config.HotkeyModifiers, config.HotkeyVk, config.HotkeyDisplay);
            _capturedToggleDesktopHotkey = HotkeyBinding.FromStored(config.DesktopHotkeyModifiers, config.DesktopHotkeyVk, config.DesktopHotkeyDisplay);
            _capturedToggleRtssHotkey = HotkeyBinding.FromStored(config.RtssHotkeyModifiers, config.RtssHotkeyVk, config.RtssHotkeyDisplay);
            _capturedSettingsHotkey = HotkeyBinding.FromStored(config.SettingsHotkeyModifiers, config.SettingsHotkeyVk, config.SettingsHotkeyDisplay);
            (_alignRight, _alignBottom) = config.Position switch
            {
                "TopLeft" => (false, false),
                "TopRight" => (true, false),
                "BottomLeft" => (false, true),
                "BottomRight" => (true, true),
                _ => (true, false)
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
            Icon = AppIconProvider.GetAppIcon();
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(FixedWindowWidth(), 640);
            MaximumSize = new Size(FixedWindowWidth(), 2000);
            _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _statusTimer.Tick += (_, _) => UpdateOutputState();

            BuildUI();
            UpdateAppearanceState();
            UpdateOutputState();
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

            var hotkeyCard = MakeCard(content, "Hotkeys", ref y, Ui(246), cardWidth, marginX);
            hotkeyCard.Controls.Add(MakeLabel("Toggle all OSD", Ui(16), Ui(42)));
            _toggleAllHotkeyBox = MakeHotkeyBox(_capturedToggleAllHotkey.Display, new Point(Ui(132), Ui(38)), new Size(hotkeyCard.Width - Ui(148), Ui(34)));
            WireHotkeyCapture(_toggleAllHotkeyBox, HotkeyCaptureTarget.ToggleAllOsd, () => _capturedToggleAllHotkey.IsEmpty ? _config.HotkeyDisplay : _capturedToggleAllHotkey.Display);
            hotkeyCard.Controls.Add(_toggleAllHotkeyBox);

            hotkeyCard.Controls.Add(MakeLabel("Desktop OSD", Ui(16), Ui(92)));
            _toggleDesktopHotkeyBox = MakeHotkeyBox(_capturedToggleDesktopHotkey.Display, new Point(Ui(132), Ui(88)), new Size(hotkeyCard.Width - Ui(148), Ui(34)));
            WireHotkeyCapture(_toggleDesktopHotkeyBox, HotkeyCaptureTarget.ToggleDesktopOsd, () => _capturedToggleDesktopHotkey.IsEmpty ? _config.DesktopHotkeyDisplay : _capturedToggleDesktopHotkey.Display);
            hotkeyCard.Controls.Add(_toggleDesktopHotkeyBox);

            hotkeyCard.Controls.Add(MakeLabel("RTSS OSD", Ui(16), Ui(142)));
            _toggleRtssHotkeyBox = MakeHotkeyBox(_capturedToggleRtssHotkey.Display, new Point(Ui(132), Ui(138)), new Size(hotkeyCard.Width - Ui(148), Ui(34)));
            WireHotkeyCapture(_toggleRtssHotkeyBox, HotkeyCaptureTarget.ToggleRtssOsd, () => _capturedToggleRtssHotkey.IsEmpty ? _config.RtssHotkeyDisplay : _capturedToggleRtssHotkey.Display);
            hotkeyCard.Controls.Add(_toggleRtssHotkeyBox);

            hotkeyCard.Controls.Add(MakeLabel("Open settings", Ui(16), Ui(192)));
            _settingsHotkeyBox = MakeHotkeyBox(_capturedSettingsHotkey.Display, new Point(Ui(132), Ui(188)), new Size(hotkeyCard.Width - Ui(148), Ui(34)));
            WireHotkeyCapture(_settingsHotkeyBox, HotkeyCaptureTarget.OpenSettings, () => _capturedSettingsHotkey.IsEmpty ? _config.SettingsHotkeyDisplay : _capturedSettingsHotkey.Display);
            hotkeyCard.Controls.Add(_settingsHotkeyBox);

            var outputCard = MakeCard(content, "Output", ref y, Ui(296), cardWidth, marginX);
            _enabledCheck = MakeCheckBox("Enable OSD", Ui(16), Ui(42), _config.Enabled);
            _enabledCheck.CheckedChanged += (_, _) =>
            {
                UpdateOutputState();
                ApplyLive();
            };
            outputCard.Controls.Add(_enabledCheck);

            _desktopOverlayCheck = MakeCheckBox("Desktop OSD", Ui(16), Ui(72), _config.DesktopOverlayEnabled);
            _desktopOverlayCheck.CheckedChanged += (_, _) =>
            {
                UpdateOutputState();
                ApplyLive();
            };
            outputCard.Controls.Add(_desktopOverlayCheck);

            _rtssOverlayCheck = MakeCheckBox("RTSS OSD (Games)", Ui(16), Ui(102), _config.RtssOverlayEnabled);
            _rtssOverlayCheck.CheckedChanged += (_, _) =>
            {
                UpdateOutputState();
                ApplyLive();
            };
            outputCard.Controls.Add(_rtssOverlayCheck);
            outputCard.Controls.Add(MakeCaption("RTSS output requires RivaTuner Statistics Server to be running.", Ui(16), Ui(132), outputCard.Width - Ui(32)));
            outputCard.Controls.Add(MakeReadOnlyDebugBox(new Point(Ui(16), Ui(156)), new Size(outputCard.Width - Ui(32), Ui(98))));
            _copyRtssDebugButton = MakeButton("Copy RTSS Debug", Ui(16), Ui(262), Ui(122), Ui(28), Color.FromArgb(50, 52, 60), FgPrimary, FontStyle.Regular);
            _copyRtssDebugButton.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            _copyRtssDebugButton.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(RtssOverlayClient.BuildDebugReport());
                }
                catch
                {
                }
            };
            outputCard.Controls.Add(_copyRtssDebugButton);

            var posCard = MakeCard(content, "Position", ref y, Ui(240), cardWidth, marginX);
            posCard.Controls.Add(MakeLabel("Horizontal", Ui(16), Ui(42)));
            _horizontalLeftButton = MakeSegmentButton("Left", new Point(Ui(132), Ui(38)), new Size(Ui(70), Ui(30)));
            _horizontalRightButton = MakeSegmentButton("Right", new Point(Ui(204), Ui(38)), new Size(Ui(70), Ui(30)));
            _horizontalLeftButton.Click += (_, _) =>
            {
                _alignRight = false;
                UpdatePositionButtons();
                ApplyLive();
            };
            _horizontalRightButton.Click += (_, _) =>
            {
                _alignRight = true;
                UpdatePositionButtons();
                ApplyLive();
            };
            posCard.Controls.Add(_horizontalLeftButton);
            posCard.Controls.Add(_horizontalRightButton);

            posCard.Controls.Add(MakeLabel("Vertical", Ui(16), Ui(82)));
            _verticalTopButton = MakeSegmentButton("Top", new Point(Ui(132), Ui(78)), new Size(Ui(70), Ui(30)));
            _verticalBottomButton = MakeSegmentButton("Bottom", new Point(Ui(204), Ui(78)), new Size(Ui(70), Ui(30)));
            _verticalTopButton.Click += (_, _) =>
            {
                _alignBottom = false;
                UpdatePositionButtons();
                ApplyLive();
            };
            _verticalBottomButton.Click += (_, _) =>
            {
                _alignBottom = true;
                UpdatePositionButtons();
                ApplyLive();
            };
            posCard.Controls.Add(_verticalTopButton);
            posCard.Controls.Add(_verticalBottomButton);

            posCard.Controls.Add(MakeLabel("Horizontal margin", Ui(16), Ui(122)));

            _horizontalMarginValue = MakeValueLabel($"{_config.OffsetX} px", posCard.Width - Ui(68), Ui(122));
            posCard.Controls.Add(_horizontalMarginValue);
            _horizontalMarginSlider = MakeSlider(posCard, Ui(16), Ui(142), posCard.Width - Ui(36), 5, 160, _config.OffsetX);
            _horizontalMarginSlider.ValueChanged += (_, _) =>
            {
                _horizontalMarginValue.Text = $"{_horizontalMarginSlider.Value} px";
                ApplyLive();
            };

            posCard.Controls.Add(MakeLabel("Vertical margin", Ui(16), Ui(170)));

            _verticalMarginValue = MakeValueLabel($"{_config.OffsetY} px", posCard.Width - Ui(68), Ui(170));
            posCard.Controls.Add(_verticalMarginValue);
            _verticalMarginSlider = MakeSlider(posCard, Ui(16), Ui(190), posCard.Width - Ui(36), 5, 160, _config.OffsetY);
            _verticalMarginSlider.ValueChanged += (_, _) =>
            {
                _verticalMarginValue.Text = $"{_verticalMarginSlider.Value} px";
                ApplyLive();
            };
            UpdatePositionButtons();

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

            var ramCard = MakeCard(content, "Memory Display", ref y, Ui(132), cardWidth, marginX);
            ramCard.Controls.Add(MakeLabel("Show RAM as", Ui(16), Ui(42)));
            _ramDisplayModeBox = MakeComboBox(new Point(Ui(132), Ui(38)), new Size(ramCard.Width - Ui(148), Ui(28)));
            _ramDisplayModeBox.Items.AddRange(new object[] { "Used / Total GB", "Percentage" });
            _ramDisplayModeBox.SelectedIndex = _config.ShowRamAsPercentage() ? 1 : 0;
            _ramDisplayModeBox.SelectedIndexChanged += (_, _) => ApplyLive();
            ramCard.Controls.Add(_ramDisplayModeBox);

            ramCard.Controls.Add(MakeLabel("Show VRAM as", Ui(16), Ui(78)));
            _vramDisplayModeBox = MakeComboBox(new Point(Ui(132), Ui(74)), new Size(ramCard.Width - Ui(148), Ui(28)));
            _vramDisplayModeBox.Items.AddRange(new object[] { "Used / Total GB", "Percentage" });
            _vramDisplayModeBox.SelectedIndex = _config.ShowVramAsPercentage() ? 1 : 0;
            _vramDisplayModeBox.SelectedIndexChanged += (_, _) => ApplyLive();
            ramCard.Controls.Add(_vramDisplayModeBox);

            var fanSensorsCard = MakeCard(content, "Fan Sensors", ref y, Ui(152), cardWidth, marginX);
            fanSensorsCard.Controls.Add(MakeLabel("CPU Fan", Ui(16), Ui(42)));
            _cpuFanSensorBox = MakeFanSensorComboBox(new Point(Ui(132), Ui(38)), new Size(fanSensorsCard.Width - Ui(148), Ui(28)));
            fanSensorsCard.Controls.Add(_cpuFanSensorBox);

            fanSensorsCard.Controls.Add(MakeLabel("GPU Fan", Ui(16), Ui(78)));
            _gpuFanSensorBox = MakeFanSensorComboBox(new Point(Ui(132), Ui(74)), new Size(fanSensorsCard.Width - Ui(148), Ui(28)));
            fanSensorsCard.Controls.Add(_gpuFanSensorBox);

            fanSensorsCard.Controls.Add(MakeLabel("Case Fan", Ui(16), Ui(114)));
            _caseFanSensorBox = MakeFanSensorComboBox(new Point(Ui(132), Ui(110)), new Size(fanSensorsCard.Width - Ui(148), Ui(28)));
            fanSensorsCard.Controls.Add(_caseFanSensorBox);

            PopulateFanSensorChoices();

            int metricRowHeight = Ui(30);
            int metricGroupHeaderHeight = Ui(22);
            int metricsHeight = Ui(40) + Ui(10);
            string? previousMetricGroup = null;
            foreach (var metric in _config.Metrics)
            {
                string group = OverlaySettingsOptionHelper.GetMetricGroupLabel(metric.Key);
                if (!string.Equals(group, previousMetricGroup, StringComparison.Ordinal))
                {
                    metricsHeight += metricGroupHeaderHeight;
                    previousMetricGroup = group;
                }

                metricsHeight += metricRowHeight;
            }
            var metricsCard = MakeCard(content, "Metrics", ref y, metricsHeight, cardWidth, marginX);
            _metricChecks = new CheckBox[_config.Metrics.Count];

            int metricY = Ui(40);
            previousMetricGroup = null;
            for (int i = 0; i < _config.Metrics.Count; i++)
            {
                var metric = _config.Metrics[i];
                string group = OverlaySettingsOptionHelper.GetMetricGroupLabel(metric.Key);
                if (!string.Equals(group, previousMetricGroup, StringComparison.Ordinal))
                {
                    metricsCard.Controls.Add(MakeSectionLabel(group, Ui(16), metricY));
                    metricY += metricGroupHeaderHeight;
                    previousMetricGroup = group;
                }

                var check = MakeCheckBox(metric.Label, Ui(16), metricY, metric.Enabled);
                check.Width = metricsCard.Width - Ui(32);
                check.CheckedChanged += (_, _) => ApplyLive();
                _metricChecks[i] = check;
                metricsCard.Controls.Add(check);
                metricY += metricRowHeight;
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

        private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_isCapturing || _activeHotkeyBox == null)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;

            if (!HotkeyCaptureHelper.TryBuild(e.KeyCode, e.Control, e.Alt, e.Shift, out var binding, out var message))
            {
                if (message == "Need a modifier key")
                {
                    _activeHotkeyBox.Text = message;
                }

                return;
            }

            switch (_activeHotkeyTarget)
            {
                case HotkeyCaptureTarget.ToggleAllOsd:
                    _capturedToggleAllHotkey = binding;
                    break;
                case HotkeyCaptureTarget.ToggleDesktopOsd:
                    _capturedToggleDesktopHotkey = binding;
                    break;
                case HotkeyCaptureTarget.ToggleRtssOsd:
                    _capturedToggleRtssHotkey = binding;
                    break;
                default:
                    _capturedSettingsHotkey = binding;
                    break;
            }

            _activeHotkeyBox.Text = binding.Display;
            _activeHotkeyBox.ForeColor = AccentGreen;
            _isCapturing = false;
            _activeHotkeyBox = null;
        }

        private void ApplyLive()
        {
            CommitConfig();
            _onSave(_config);
        }

        private void CommitConfig()
        {
            var state = new OverlaySettingsState
            {
                ToggleAllHotkey = _capturedToggleAllHotkey,
                ToggleDesktopHotkey = _capturedToggleDesktopHotkey,
                ToggleRtssHotkey = _capturedToggleRtssHotkey,
                SettingsHotkey = _capturedSettingsHotkey,
                Enabled = _enabledCheck.Checked,
                DesktopOverlayEnabled = _desktopOverlayCheck.Checked,
                RtssOverlayEnabled = _rtssOverlayCheck.Checked,
                AlignRight = _alignRight,
                AlignBottom = _alignBottom,
                OffsetX = _horizontalMarginSlider.Value,
                OffsetY = _verticalMarginSlider.Value,
                OpacityPercent = _opacitySlider.Value,
                FontSize = _fontSlider.Value,
                FontFamily = _fontFamilyBox.SelectedItem?.ToString() ?? _config.FontFamily,
                HasBackground = _backgroundModeBox.SelectedIndex != 1,
                ShowTextShadow = _shadowCheck.Checked,
                ShowBorder = _borderCheck.Checked,
                ShowTextOutline = _outlineCheck.Checked,
                TextOutlineThickness = _outlineThicknessSlider.Value,
                ShowRamAsPercentage = _ramDisplayModeBox.SelectedIndex == 1,
                ShowVramAsPercentage = _vramDisplayModeBox.SelectedIndex == 1,
                CpuFanSensorKey = OverlaySettingsOptionHelper.GetSelectedFanSensorKey(_cpuFanSensorBox.SelectedItem),
                GpuFanSensorKey = OverlaySettingsOptionHelper.GetSelectedFanSensorKey(_gpuFanSensorBox.SelectedItem),
                CaseFanSensorKey = OverlaySettingsOptionHelper.GetSelectedFanSensorKey(_caseFanSensorBox.SelectedItem),
                MetricEnabledStates = _metricChecks.Select(check => check.Checked).ToArray()
            };

            OverlaySettingsConfigMapper.Apply(_config, state);
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

        private void UpdateOutputState()
        {
            bool enabled = _enabledCheck.Checked;
            _desktopOverlayCheck.Enabled = enabled;
            _rtssOverlayCheck.Enabled = enabled;
            _copyRtssDebugButton.Enabled = enabled && _rtssOverlayCheck.Checked;
            var snapshot = enabled && _rtssOverlayCheck.Checked ? RtssOverlayClient.GetStatusSnapshot() : null;
            var state = OverlaySettingsOutputStateBuilder.Build(enabled, _desktopOverlayCheck.Checked, _rtssOverlayCheck.Checked, snapshot);
            _outputStatusBox.Text = state.Text;
            _outputStatusBox.ForeColor = state.Tone == OverlaySettingsOutputTone.Success
                ? AccentGreen
                : Color.FromArgb(255, 195, 70);
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

        private Label MakeSectionLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text.ToUpperInvariant(),
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = Accent,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
        }

        private Label MakeCaption(string text, int x, int y, int width)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, Ui(28)),
                ForeColor = Color.FromArgb(110, 116, 130),
                Font = new Font("Segoe UI", 8.25f),
                BackColor = Color.Transparent
            };
        }

        private Panel MakeReadOnlyDebugBox(Point location, Size size)
        {
            var host = new Panel
            {
                Location = location,
                Size = size,
                BackColor = BgInput,
                Padding = new Padding(Ui(6), Ui(5), Ui(4), Ui(4))
            };

            host.Paint += (_, e) =>
            {
                using var pen = new Pen(Border);
                e.Graphics.DrawRectangle(pen, 0, 0, host.Width - 1, host.Height - 1);
            };

            _outputStatusBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = BgInput,
                ForeColor = FgSecondary,
                Font = new Font("Consolas", 8.75f),
                DetectUrls = false,
                WordWrap = true,
                TabStop = false,
                Margin = Padding.Empty
            };

            host.Controls.Add(_outputStatusBox);
            return host;
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

        private TextBox MakeHotkeyBox(string text, Point location, Size size)
        {
            return new TextBox
            {
                Text = text,
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Center,
                Location = location,
                Size = size,
                BackColor = BgInput,
                ForeColor = Accent,
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                BorderStyle = BorderStyle.FixedSingle
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

        private ComboBox MakeFanSensorComboBox(Point location, Size size)
        {
            var comboBox = MakeComboBox(location, size);
            comboBox.SelectedIndexChanged += (_, _) => ApplyLive();
            return comboBox;
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

        private Button MakeSegmentButton(string text, Point location, Size size)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = BgInput,
                ForeColor = FgSecondary,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 52, 62);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 60, 72);
            return button;
        }

        private void UpdatePositionButtons()
        {
            ApplySegmentState(_horizontalLeftButton, !_alignRight);
            ApplySegmentState(_horizontalRightButton, _alignRight);
            ApplySegmentState(_verticalTopButton, !_alignBottom);
            ApplySegmentState(_verticalBottomButton, _alignBottom);
        }

        private void ApplySegmentState(Button button, bool isSelected)
        {
            button.BackColor = isSelected ? Accent : BgInput;
            button.ForeColor = isSelected ? Color.White : FgSecondary;
            button.FlatAppearance.BorderColor = isSelected ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = isSelected
                ? Color.FromArgb(95, 165, 255)
                : Color.FromArgb(48, 52, 62);
            button.FlatAppearance.MouseDownBackColor = isSelected
                ? Color.FromArgb(70, 135, 230)
                : Color.FromArgb(55, 60, 72);
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

        private void WireHotkeyCapture(TextBox box, HotkeyCaptureTarget target, Func<string> currentDisplayProvider)
        {
            box.GotFocus += (_, _) =>
            {
                _isCapturing = true;
                _activeHotkeyBox = box;
                _activeHotkeyTarget = target;
                box.Text = "Press shortcut...";
                box.ForeColor = Color.FromArgb(255, 195, 70);
            };

            box.LostFocus += (_, _) =>
            {
                if (_activeHotkeyBox == box)
                {
                    _activeHotkeyBox = null;
                }

                _isCapturing = false;
                box.ForeColor = Accent;
                box.Text = currentDisplayProvider();
            };

            box.KeyDown += OnHotkeyKeyDown;
        }

        private void PopulateFanSensorChoices()
        {
            PopulateFanSensorChoice(_cpuFanSensorBox, _config.CpuFanSensorKey);
            PopulateFanSensorChoice(_gpuFanSensorBox, _config.GpuFanSensorKey);
            PopulateFanSensorChoice(_caseFanSensorBox, _config.CaseFanSensorKey);
        }

        private void PopulateFanSensorChoice(ComboBox comboBox, string selectedKey)
        {
            comboBox.Items.Clear();
            foreach (var option in OverlaySettingsOptionHelper.BuildFanSensorItems(_fanSensorOptions, selectedKey))
            {
                comboBox.Items.Add(option);
            }

            comboBox.SelectedItem = comboBox.Items
                .OfType<FanSensorOption>()
                .FirstOrDefault(option => string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                ?? comboBox.Items[0];

            comboBox.Enabled = comboBox.Items.Count > 0;
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
            _statusTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _statusTimer.Stop();
            _statusTimer.Dispose();
            base.OnFormClosed(e);
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

        private enum HotkeyCaptureTarget
        {
            ToggleAllOsd,
            ToggleDesktopOsd,
            ToggleRtssOsd,
            OpenSettings
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
