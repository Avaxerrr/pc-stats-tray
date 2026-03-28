using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace HwMonTray
{
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) { computer.Traverse(this); }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    static class Program
    {
        private static NotifyIcon cpuTrayIcon = null!;
        private static NotifyIcon gpuTrayIcon = null!;
        private static Computer computer = null!;
        private static ContextMenuStrip contextMenu = null!;
        private static System.Windows.Forms.Timer timer = null!;
        private static DetailsForm? detailsForm;

        // OSD Overlay
        private static OverlayForm? overlayForm;
        private static OverlayConfig overlayConfig = null!;
        private static HotkeyWindow? hotkeyWindow;

        // Temperature tracking for tooltip stats
        private static float cpuMinTemp = float.MaxValue;
        private static float cpuMaxTemp = float.MinValue;
        private static double cpuSumTemp = 0;
        private static long cpuTempCount = 0;

        private static float gpuMinTemp = float.MaxValue;
        private static float gpuMaxTemp = float.MinValue;
        private static double gpuSumTemp = 0;
        private static long gpuTempCount = 0;

        // Config path (shared with DetailsForm)
        internal static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "hwmon_config.json");

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try 
            {
                computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsNetworkEnabled = true,
                    IsStorageEnabled = true,
                    IsBatteryEnabled = true,
                    IsPsuEnabled = true
                };
                computer.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to initialize LibreHardwareMonitor: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Load overlay config
            overlayConfig = LoadOverlayConfig();

            contextMenu = new ContextMenuStrip();
            PopulateInitialMenu();

            cpuTrayIcon = new NotifyIcon
            {
                Text = "CPU Monitor",
                ContextMenuStrip = contextMenu,
                Visible = true
            };

            gpuTrayIcon = new NotifyIcon
            {
                Text = "GPU Monitor",
                ContextMenuStrip = contextMenu,
                Visible = true
            };

            // Create overlay (hidden by default)
            overlayForm = new OverlayForm(computer, overlayConfig);

            // Register global hotkey
            hotkeyWindow = new HotkeyWindow();
            hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
            RegisterCurrentHotkey();

            UpdateData();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2000;
            timer.Tick += (sender, e) => { UpdateData(); };
            timer.Start();

            Application.ApplicationExit += (s, e) => { CleanUp(); };

            Application.Run();
        }

        private static void PopulateInitialMenu()
        {
            contextMenu.Items.Clear();

            var refreshRateItem = new ToolStripMenuItem("Refresh Rate");
            var rate1s = new ToolStripMenuItem("1s", null, (s, e) => { timer.Interval = 1000; CheckRefreshRateMenu(refreshRateItem, "1s"); });
            var rate2s = new ToolStripMenuItem("2s", null, (s, e) => { timer.Interval = 2000; CheckRefreshRateMenu(refreshRateItem, "2s"); }) { Checked = true };
            var rate5s = new ToolStripMenuItem("5s", null, (s, e) => { timer.Interval = 5000; CheckRefreshRateMenu(refreshRateItem, "5s"); });
            refreshRateItem.DropDownItems.AddRange(new ToolStripItem[] { rate1s, rate2s, rate5s });
            
            contextMenu.Items.Add(refreshRateItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add("Show Details", null, (s, e) =>
            {
                if (detailsForm == null || detailsForm.IsDisposed)
                    detailsForm = new DetailsForm(computer);
                detailsForm.Show();
                detailsForm.BringToFront();
            });

            // OSD overlay menu items
            contextMenu.Items.Add(new ToolStripSeparator());

            var toggleOsdItem = new ToolStripMenuItem($"Toggle OSD  ({overlayConfig.HotkeyDisplay})");
            toggleOsdItem.Click += (s, e) => ToggleOverlay();
            contextMenu.Items.Add(toggleOsdItem);

            contextMenu.Items.Add("OSD Settings…", null, (s, e) =>
            {
                var settingsForm = new OverlaySettingsForm(overlayConfig, OnOverlayConfigSaved);
                settingsForm.ShowDialog();
            });

            contextMenu.Items.Add(new ToolStripSeparator());

            var startupItem = new ToolStripMenuItem("Run at Startup");
            startupItem.Checked = IsStartupTaskConfigured();
            startupItem.Click += (s, e) => 
            {
                ToggleStartup();
                startupItem.Checked = IsStartupTaskConfigured();
            };
            contextMenu.Items.Add(startupItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());
        }

        // ── OSD Overlay ──────────────────────────────────────────────

        private static void ToggleOverlay()
        {
            if (overlayForm == null || overlayForm.IsDisposed)
            {
                overlayForm = new OverlayForm(computer, overlayConfig);
            }

            if (overlayForm.Visible)
            {
                overlayForm.Hide();
                overlayConfig.Enabled = false;
            }
            else
            {
                overlayForm.Show();
                overlayForm.RefreshData();
                overlayConfig.Enabled = true;
            }

            SaveOverlayConfig(overlayConfig);
        }

        private static void OnHotkeyPressed()
        {
            ToggleOverlay();
        }

        private static void OnOverlayConfigSaved(OverlayConfig config)
        {
            overlayConfig = config;

            // Re-register hotkey with new combo
            UnregisterCurrentHotkey();
            RegisterCurrentHotkey();

            // Update overlay
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.UpdateConfig(config);
            }

            SaveOverlayConfig(config);

            // Refresh context menu to show new hotkey
            PopulateInitialMenu();
        }

        private static void RegisterCurrentHotkey()
        {
            if (hotkeyWindow != null && overlayConfig.HotkeyVk != 0)
            {
                HotkeyWindow.RegisterHotKey(hotkeyWindow.Handle, 1,
                    (uint)overlayConfig.HotkeyModifiers, (uint)overlayConfig.HotkeyVk);
            }
        }

        private static void UnregisterCurrentHotkey()
        {
            if (hotkeyWindow != null)
            {
                HotkeyWindow.UnregisterHotKey(hotkeyWindow.Handle, 1);
            }
        }

        // ── Overlay Config Persistence ───────────────────────────────

        private static OverlayConfig LoadOverlayConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfigFull>(json);
                    if (cfg?.Overlay != null)
                        return cfg.Overlay;
                }
            }
            catch { }
            return new OverlayConfig();
        }

        internal static void SaveOverlayConfig(OverlayConfig overlay)
        {
            try
            {
                AppConfigFull cfg;
                if (File.Exists(ConfigPath))
                {
                    string existing = File.ReadAllText(ConfigPath);
                    cfg = JsonSerializer.Deserialize<AppConfigFull>(existing) ?? new AppConfigFull();
                }
                else
                {
                    cfg = new AppConfigFull();
                }

                cfg.Overlay = overlay;
                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        /// <summary>
        /// Full config model that includes both sensor filter settings and overlay settings.
        /// </summary>
        internal class AppConfigFull
        {
            public List<string> HiddenSensors { get; set; } = new();
            public OverlayConfig? Overlay { get; set; }
        }

        // ── Hotkey Window ────────────────────────────────────────────

        private const string TaskName = "HwMonTray_Startup";

        private static bool IsStartupTaskConfigured()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service")!;
                dynamic ts = Activator.CreateInstance(type)!;
                ts.Connect();
                dynamic folder = ts.GetFolder("\\");
                var task = folder.GetTask(TaskName);
                return task != null;
            }
            catch { return false; }
        }

        private static void ToggleStartup()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service")!;
                dynamic ts = Activator.CreateInstance(type)!;
                ts.Connect();
                dynamic folder = ts.GetFolder("\\");

                if (IsStartupTaskConfigured())
                {
                    folder.DeleteTask(TaskName, 0);
                }
                else
                {
                    dynamic taskDef = ts.NewTask(0);
                    taskDef.RegistrationInfo.Description = "Hardware Monitor Tray Icon Startup";
                    
                    // Trigger: Logon (type 9)
                    dynamic trigger = taskDef.Triggers.Create(9);
                    
                    // Action: Run exe (type 0)
                    dynamic action = taskDef.Actions.Create(0);
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
                    action.Path = exePath;
                    action.WorkingDirectory = Path.GetDirectoryName(exePath);

                    // Run with highest privileges (bypasses UAC on boot)
                    taskDef.Principal.RunLevel = 1;

                    // Critical for laptops: allow running on battery
                    taskDef.Settings.DisallowStartIfOnBatteries = false;
                    taskDef.Settings.StopIfGoingOnBatteries = false;
                    taskDef.Settings.ExecutionTimeLimit = "PT0S"; // No time limit

                    // Register: CreateOrUpdate (6), InteractiveToken (3)
                    folder.RegisterTaskDefinition(TaskName, taskDef, 6, null, null, 3, null);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to modify startup task: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void CheckRefreshRateMenu(ToolStripMenuItem parent, string checkedText)
        {
            foreach (ToolStripMenuItem item in parent.DropDownItems)
            {
                item.Checked = item.Text == checkedText;
            }
        }

        private static void UpdateData()
        {
            computer.Accept(new UpdateVisitor());
            UpdateIcon();

            // Refresh overlay if visible
            overlayForm?.RefreshData();
        }

        private static void UpdateIcon()
        {
            float maxCpuTemp = 0;
            float maxGpuTemp = 0;

            foreach (var hw in computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    var cpuTempSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && Math.Abs(maxCpuTemp) < 0.1 && s.Name.Contains("Core Max"))
                                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && Math.Abs(maxCpuTemp) < 0.1 && s.Name.Contains("Package"))
                                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (cpuTempSensor?.Value.HasValue == true)
                        maxCpuTemp = Math.Max(maxCpuTemp, cpuTempSensor.Value.Value);
                }
                else if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel)
                {
                    var gpuTempSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && Math.Abs(maxGpuTemp) < 0.1 && s.Name.Contains("Core"))
                                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (gpuTempSensor?.Value.HasValue == true)
                        maxGpuTemp = Math.Max(maxGpuTemp, gpuTempSensor.Value.Value);
                }
            }

            if (maxCpuTemp > 0)
            {
                if (maxCpuTemp < cpuMinTemp) cpuMinTemp = maxCpuTemp;
                if (maxCpuTemp > cpuMaxTemp) cpuMaxTemp = maxCpuTemp;
                cpuSumTemp += maxCpuTemp;
                cpuTempCount++;
            }

            if (maxGpuTemp > 0)
            {
                if (maxGpuTemp < gpuMinTemp) gpuMinTemp = maxGpuTemp;
                if (maxGpuTemp > gpuMaxTemp) gpuMaxTemp = maxGpuTemp;
                gpuSumTemp += maxGpuTemp;
                gpuTempCount++;
            }

            float cpuAvg = cpuTempCount > 0 ? (float)(cpuSumTemp / cpuTempCount) : 0;
            float gpuAvg = gpuTempCount > 0 ? (float)(gpuSumTemp / gpuTempCount) : 0;

            string cpuTrayText = $"CPU: {maxCpuTemp:0}°C | Min: {(cpuMinTemp == float.MaxValue ? 0 : cpuMinTemp):0}° | Max: {(cpuMaxTemp == float.MinValue ? 0 : cpuMaxTemp):0}° | Avg: {cpuAvg:0}°";
            if (cpuTrayText.Length >= 64) cpuTrayText = cpuTrayText.Substring(0, 63);
            cpuTrayIcon.Text = cpuTrayText;

            string gpuTrayText = $"GPU: {maxGpuTemp:0}°C | Min: {(gpuMinTemp == float.MaxValue ? 0 : gpuMinTemp):0}° | Max: {(gpuMaxTemp == float.MinValue ? 0 : gpuMaxTemp):0}° | Avg: {gpuAvg:0}°";
            if (gpuTrayText.Length >= 64) gpuTrayText = gpuTrayText.Substring(0, 63);
            gpuTrayIcon.Text = gpuTrayText;

            SetTrayIcon(cpuTrayIcon, maxCpuTemp, "CPU");
            SetTrayIcon(gpuTrayIcon, maxGpuTemp, "GPU");
        }

        private static Color TextColor(float temp)
        {
            if (temp >= 85) return Color.FromArgb(255, 80, 70);   // hot red
            if (temp >= 70) return Color.FromArgb(255, 165, 40);  // warm orange
            if (temp >= 55) return Color.FromArgb(230, 210, 50);  // mild yellow
            return Color.FromArgb(60, 210, 120);                   // cool green
        }

        private static void SetTrayIcon(NotifyIcon icon, float temp, string label)
        {
            const int size = 128;
            const int radius = 14;

            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Dark charcoal background
                var rect = new Rectangle(0, 0, size - 1, size - 1);
                using var path = RoundedRect(rect, radius);
                using (var bgBrush = new SolidBrush(Color.FromArgb(30, 30, 30)))
                    g.FillPath(bgBrush, path);
                using (var pen = new Pen(Color.FromArgb(55, 55, 55), 1.5f))
                    g.DrawPath(pen, path);

                // GenericTypographic removes GDI+ hidden padding — tight measure + smooth render
                string tempText = temp > 0 ? $"{temp:0}" : "--";
                StringFormat fmt = StringFormat.GenericTypographic;
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;

                Font bestFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                for (float fs = 130f; fs >= 10f; fs -= 1f)
                {
                    var testFont = new Font("Segoe UI", fs, FontStyle.Bold);
                    SizeF measured = g.MeasureString(tempText, testFont, PointF.Empty, fmt);
                    if (measured.Width <= size && measured.Height <= size)
                    {
                        bestFont = testFont;
                        break;
                    }
                    testFont.Dispose();
                }

                using (bestFont)
                using (var textBrush = new SolidBrush(TextColor(temp)))
                {
                    g.DrawString(tempText, bestFont, textBrush, new RectangleF(0, 0, size, size), fmt);
                }
            }

            IntPtr hIcon = bmp.GetHicon();
            Icon newIcon = Icon.FromHandle(hIcon);
            var oldIcon = icon.Icon;
            icon.Icon = newIcon;

            if (oldIcon != null)
            {
                DestroyIcon(oldIcon.Handle);
                oldIcon.Dispose();
            }
            bmp.Dispose();
        }


        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        private static void CleanUp()
        {
            UnregisterCurrentHotkey();
            hotkeyWindow?.DestroyHandle();

            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.Close();
                overlayForm.Dispose();
            }

            if (cpuTrayIcon != null)
            {
                cpuTrayIcon.Visible = false;
                cpuTrayIcon.Dispose();
            }
            if (gpuTrayIcon != null)
            {
                gpuTrayIcon.Visible = false;
                gpuTrayIcon.Dispose();
            }
            if (computer != null)
            {
                computer.Close();
            }
        }
    }

    /// <summary>
    /// Invisible NativeWindow that listens for WM_HOTKEY messages.
    /// </summary>
    internal class HotkeyWindow : NativeWindow
    {
        private const int WM_HOTKEY = 0x0312;

        public event Action? HotkeyPressed;

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
                HotkeyPressed?.Invoke();
            }
            base.WndProc(ref m);
        }
    }
}
