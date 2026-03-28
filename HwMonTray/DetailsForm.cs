using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace HwMonTray
{
    public class DetailsForm : Form
    {
        private readonly Computer _computer;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private TreeView _tree = null!;
        private DataGridView _grid = null!;
        private HashSet<string> _hiddenSensors;
        private IHardware? _selectedHardware;
        private static string ConfigPath => Program.ConfigPath;

        // Dark theme colors
        private static readonly Color BgDark = Color.FromArgb(20, 20, 20);
        private static readonly Color BgSidebar = Color.FromArgb(26, 26, 26);
        private static readonly Color BgRow = Color.FromArgb(30, 30, 30);
        private static readonly Color BgRowAlt = Color.FromArgb(36, 36, 36);
        private static readonly Color BgHeader = Color.FromArgb(18, 18, 18);
        private static readonly Color FgText = Color.FromArgb(210, 210, 210);
        private static readonly Color FgDim = Color.FromArgb(120, 120, 120);
        private static readonly Color AccentBlue = Color.FromArgb(55, 130, 220);
        private static readonly Color BorderColor = Color.FromArgb(50, 50, 50);

        public DetailsForm(Computer computer)
        {
            _computer = computer;
            _hiddenSensors = LoadConfig();

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
            Text = $"Hardware Monitor — Sensor Details (v{version})";
            Size = new Size(820, 560);
            MinimumSize = new Size(600, 350);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ForeColor = FgText;
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            TopMost = true;

            BuildUI();

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _refreshTimer.Tick += (s, e) => RefreshGrid();
            _refreshTimer.Start();

            PopulateTree();
        }

        private void BuildUI()
        {
            // --- Sidebar (TreeView) ---
            var sidePanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 200,
                BackColor = BgSidebar,
                Padding = new Padding(0)
            };

            var sideLabel = new Label
            {
                Text = " HARDWARE",
                Dock = DockStyle.Top,
                Height = 32,
                ForeColor = FgDim,
                BackColor = BgHeader,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = BgSidebar,
                ForeColor = FgText,
                Font = new Font("Segoe UI", 9.5f),
                BorderStyle = BorderStyle.None,
                FullRowSelect = true,
                ShowLines = false,
                ShowPlusMinus = true,
                ShowRootLines = false,
                ItemHeight = 26,
                Indent = 16
            };
            _tree.AfterSelect += (s, e) =>
            {
                _selectedHardware = e.Node?.Tag as IHardware;
                RefreshGrid();
            };

            sidePanel.Controls.Add(_tree);
            sidePanel.Controls.Add(sideLabel);

            // Splitter
            var splitter = new Splitter
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = BorderColor
            };

            // --- Top bar ---
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = BgHeader,
                Padding = new Padding(8, 5, 8, 5)
            };

            var titleLabel = new Label
            {
                Text = $"SENSOR DATA  —  v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}",
                ForeColor = FgDim,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(10, 11)
            };

            var configBtn = new Button
            {
                Text = "⚙ Filter",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 42, 42),
                ForeColor = FgText,
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(80, 26),
                Cursor = Cursors.Hand
            };
            configBtn.FlatAppearance.BorderColor = BorderColor;
            configBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 58, 58);
            configBtn.Click += (s, e) => OpenFilterDialog();
            configBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            topPanel.Controls.Add(titleLabel);
            topPanel.Controls.Add(configBtn);
            topPanel.Resize += (s, e) =>
            {
                configBtn.Location = new Point(topPanel.Width - configBtn.Width - 8, 6);
            };

            // --- DataGridView ---
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = BgDark,
                GridColor = Color.FromArgb(38, 38, 38),
                EnableHeadersVisualStyles = false,
                MultiSelect = false
            };

            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgHeader,
                ForeColor = FgDim,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                SelectionBackColor = BgHeader,
                SelectionForeColor = FgDim,
                Padding = new Padding(4, 4, 4, 4)
            };
            _grid.ColumnHeadersHeight = 30;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            _grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgRow,
                ForeColor = FgText,
                SelectionBackColor = Color.FromArgb(40, 60, 90),
                SelectionForeColor = Color.White,
                Font = new Font("Segoe UI", 9.25f),
                Padding = new Padding(4, 2, 4, 2)
            };
            _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgRowAlt,
                ForeColor = FgText,
                SelectionBackColor = Color.FromArgb(40, 60, 90),
                SelectionForeColor = Color.White
            };
            _grid.RowTemplate.Height = 26;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Sensor", HeaderText = "Sensor", FillWeight = 32 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", FillWeight = 16 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 16 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Min", HeaderText = "Min", FillWeight = 16 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Max", HeaderText = "Max", FillWeight = 16 });

            // Main content panel (right side of splitter)
            var contentPanel = new Panel { Dock = DockStyle.Fill };
            contentPanel.Controls.Add(_grid);
            contentPanel.Controls.Add(topPanel);

            // Add controls in order
            Controls.Add(contentPanel);
            Controls.Add(splitter);
            Controls.Add(sidePanel);
        }

        private void PopulateTree()
        {
            _computer.Accept(new UpdateVisitor());
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            // Group hardware by type
            var hwGroups = _computer.Hardware
                .GroupBy(h => HardwareCategory(h.HardwareType))
                .OrderBy(g => g.Key);

            foreach (var group in hwGroups)
            {
                var categoryNode = new TreeNode(group.Key)
                {
                    ForeColor = AccentBlue,
                    NodeFont = new Font("Segoe UI", 9f, FontStyle.Bold)
                };

                foreach (var hw in group)
                {
                    var hwNode = new TreeNode(hw.Name) { Tag = hw };
                    foreach (var subHw in hw.SubHardware)
                    {
                        hwNode.Nodes.Add(new TreeNode(subHw.Name) { Tag = subHw });
                    }
                    categoryNode.Nodes.Add(hwNode);
                }
                _tree.Nodes.Add(categoryNode);
            }

            _tree.ExpandAll();
            _tree.EndUpdate();

            // Select first actual hardware node
            if (_tree.Nodes.Count > 0 && _tree.Nodes[0].Nodes.Count > 0)
            {
                _tree.SelectedNode = _tree.Nodes[0].Nodes[0];
            }
        }

        private static string HardwareCategory(HardwareType type) => type switch
        {
            HardwareType.Cpu => "🖥  Processor",
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "🎮  Graphics",
            HardwareType.Memory => "💾  Memory",
            HardwareType.Motherboard or HardwareType.SuperIO => "🔧  Motherboard",
            HardwareType.Storage => "💿  Storage",
            HardwareType.Network => "🌐  Network",
            HardwareType.Battery => "🔋  Battery",
            HardwareType.Psu => "⚡  PSU",
            HardwareType.Cooler => "❄  Cooling",
            _ => "📦  Other"
        };

        private void RefreshGrid()
        {
            if (_selectedHardware == null) return;

            _computer.Accept(new UpdateVisitor());

            int scrollIndex = _grid.FirstDisplayedScrollingRowIndex;
            _grid.Rows.Clear();

            // Group sensors by type
            var groups = _selectedHardware.Sensors
                .Where(s => !_hiddenSensors.Contains(SensorKey(s)))
                .GroupBy(s => s.SensorType)
                .OrderBy(g => g.Key.ToString());

            foreach (var group in groups)
            {
                // Group header row
                int hdrIdx = _grid.Rows.Add($"━━  {group.Key}  ━━", "", "", "", "");
                var hdrRow = _grid.Rows[hdrIdx];
                hdrRow.DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(24, 34, 46),
                    ForeColor = AccentBlue,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(24, 34, 46),
                    SelectionForeColor = AccentBlue
                };
                hdrRow.Height = 24;

                foreach (var sensor in group.OrderBy(s => s.Name))
                {
                    string val = FormatValue(sensor.Value, sensor.SensorType);
                    string min = FormatValue(sensor.Min, sensor.SensorType);
                    string max = FormatValue(sensor.Max, sensor.SensorType);
                    int rowIdx = _grid.Rows.Add(sensor.Name, sensor.SensorType.ToString(), val, min, max);

                    if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                    {
                        Color c = TempTextColor(sensor.Value.Value);
                        _grid.Rows[rowIdx].Cells["Value"].Style.ForeColor = c;
                        _grid.Rows[rowIdx].Cells["Value"].Style.SelectionForeColor = c;
                    }
                }
            }

            if (_grid.RowCount == 0)
            {
                _grid.Rows.Add("No visible sensors", "", "", "", "");
                _grid.Rows[0].DefaultCellStyle.ForeColor = FgDim;
            }

            if (scrollIndex >= 0 && scrollIndex < _grid.RowCount)
                _grid.FirstDisplayedScrollingRowIndex = scrollIndex;
        }

        private static string SensorKey(ISensor sensor) =>
            $"{sensor.Hardware.Identifier}/{sensor.SensorType}/{sensor.Name}";

        private static string FormatValue(float? value, SensorType type)
        {
            if (!value.HasValue) return "—";
            string v = value.Value.ToString("0.#");
            return type switch
            {
                SensorType.Temperature => v + " °C",
                SensorType.Load => v + " %",
                SensorType.Clock => v + " MHz",
                SensorType.Fan => v + " RPM",
                SensorType.Power => v + " W",
                SensorType.Voltage => v + " V",
                SensorType.Current => v + " A",
                SensorType.Data => v + " GB",
                SensorType.SmallData => v + " MB",
                SensorType.Throughput => v + " B/s",
                SensorType.Frequency => v + " Hz",
                SensorType.Level or SensorType.Control or SensorType.Humidity => v + " %",
                SensorType.Energy => v + " mWh",
                SensorType.Noise => v + " dBA",
                _ => v
            };
        }

        private static Color TempTextColor(float temp)
        {
            if (temp >= 85) return Color.FromArgb(255, 80, 70);
            if (temp >= 70) return Color.FromArgb(255, 165, 40);
            if (temp >= 55) return Color.FromArgb(230, 210, 50);
            return Color.FromArgb(60, 210, 120);
        }

        // --- Filter Dialog ---

        private void OpenFilterDialog()
        {
            if (_selectedHardware == null)
            {
                MessageBox.Show("Select a hardware item first.", "Filter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sensors = _selectedHardware.Sensors
                .OrderBy(s => s.SensorType.ToString())
                .ThenBy(s => s.Name)
                .ToList();

            using var dlg = new Form
            {
                Text = $"Filter — {_selectedHardware.Name}",
                Size = new Size(480, 440),
                MinimumSize = new Size(350, 250),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BgDark,
                ForeColor = FgText,
                Font = new Font("Segoe UI", 9.5f),
                TopMost = true
            };

            var infoLabel = new Label
            {
                Text = "Uncheck sensors to hide them:",
                ForeColor = FgDim,
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(8, 6, 0, 0)
            };

            var list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = FgText,
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
                Font = new Font("Segoe UI", 9f)
            };

            foreach (var sensor in sensors)
            {
                string key = SensorKey(sensor);
                string display = $"[{sensor.SensorType}]  {sensor.Name}";
                bool visible = !_hiddenSensors.Contains(key);
                list.Items.Add(new SensorFilterItem(key, display), visible);
            }

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = BgHeader };
            var btnSave = new Button
            {
                Text = "Save & Apply",
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Size = new Size(105, 28),
                Cursor = Cursors.Hand,
                Location = new Point(8, 6)
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                // Keep hidden sensors from other hardware, only update this hardware's sensors
                foreach (var sensor in sensors)
                    _hiddenSensors.Remove(SensorKey(sensor));

                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (!list.GetItemChecked(i))
                        _hiddenSensors.Add(((SensorFilterItem)list.Items[i]).Key);
                }
                SaveConfig(_hiddenSensors);
                RefreshGrid();
                dlg.Close();
            };

            var btnAll = new Button
            {
                Text = "All",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(48, 48, 48),
                ForeColor = FgText,
                Size = new Size(50, 28),
                Location = new Point(122, 6)
            };
            btnAll.FlatAppearance.BorderColor = BorderColor;
            btnAll.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };

            var btnNone = new Button
            {
                Text = "None",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(48, 48, 48),
                ForeColor = FgText,
                Size = new Size(55, 28),
                Location = new Point(180, 6)
            };
            btnNone.FlatAppearance.BorderColor = BorderColor;
            btnNone.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };

            btnPanel.Controls.AddRange(new Control[] { btnSave, btnAll, btnNone });
            dlg.Controls.Add(list);
            dlg.Controls.Add(infoLabel);
            dlg.Controls.Add(btnPanel);
            dlg.ShowDialog(this);
        }

        // --- Config persistence ---

        private static HashSet<string> LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<Program.AppConfigFull>(json);
                    return cfg?.HiddenSensors != null
                        ? new HashSet<string>(cfg.HiddenSensors)
                        : new HashSet<string>();
                }
            }
            catch { }
            return new HashSet<string>();
        }

        private static void SaveConfig(HashSet<string> hidden)
        {
            try
            {
                Program.AppConfigFull cfg;
                if (File.Exists(ConfigPath))
                {
                    string existing = File.ReadAllText(ConfigPath);
                    cfg = JsonSerializer.Deserialize<Program.AppConfigFull>(existing) ?? new Program.AppConfigFull();
                }
                else
                {
                    cfg = new Program.AppConfigFull();
                }
                cfg.HiddenSensors = hidden.ToList();
                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _refreshTimer?.Dispose();
            base.Dispose(disposing);
        }

        private class SensorFilterItem
        {
            public string Key { get; }
            public string Display { get; }
            public SensorFilterItem(string key, string display) { Key = key; Display = display; }
            public override string ToString() => Display;
        }
    }
}
