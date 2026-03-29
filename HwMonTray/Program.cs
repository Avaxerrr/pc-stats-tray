using System;
using System.Drawing;
using System.Linq;
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
        private static OverlaySettingsForm? settingsForm;

        // OSD Overlay
        private static OverlayForm? overlayForm;
        private static OverlayConfig overlayConfig = null!;
        private static GlobalHotkeyService? hotkeyService;
        private static RtssOverlayClient? rtssOverlayClient;
        private static ToolStripMenuItem? rtssStatusItem;

        // Temperature tracking for tooltip stats
        private static float cpuMinTemp = float.MaxValue;
        private static float cpuMaxTemp = float.MinValue;
        private static double cpuSumTemp = 0;
        private static long cpuTempCount = 0;

        private static float gpuMinTemp = float.MaxValue;
        private static float gpuMaxTemp = float.MinValue;
        private static double gpuSumTemp = 0;
        private static long gpuTempCount = 0;

        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
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
            overlayConfig = AppConfigStore.LoadOverlayConfig(AppConfigStore.DefaultPath);

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
            rtssOverlayClient = new RtssOverlayClient();

            // Register global hotkeys
            hotkeyService = new GlobalHotkeyService();
            hotkeyService.HotkeyPressed += OnHotkeyPressed;
            hotkeyService.ApplyConfig(overlayConfig);

            UpdateData();
            ApplyOverlayOutputs();

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

            contextMenu.Items.Add($"OSD Settings…  ({overlayConfig.SettingsHotkeyDisplay})", null, (s, e) => OpenOverlaySettings());

            rtssStatusItem = new ToolStripMenuItem("RTSS: checking…")
            {
                Enabled = false
            };
            contextMenu.Items.Add(rtssStatusItem);
            UpdateRtssStatusMenu();

            contextMenu.Items.Add(new ToolStripSeparator());

            var startupItem = new ToolStripMenuItem("Run at Startup");
            startupItem.Checked = StartupTaskService.IsConfigured();
            startupItem.Click += (s, e) => 
            {
                ToggleStartup();
                startupItem.Checked = StartupTaskService.IsConfigured();
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

            overlayConfig.Enabled = !overlayConfig.Enabled;

            ApplyOverlayOutputs();
            AppConfigStore.SaveOverlayConfig(AppConfigStore.DefaultPath, overlayConfig);
        }

        private static void OnHotkeyPressed(int hotkeyId)
        {
            switch (hotkeyId)
            {
                case 1:
                    ToggleOverlay();
                    break;
                case 2:
                    OpenOverlaySettings();
                    break;
            }
        }

        private static void OnOverlayConfigSaved(OverlayConfig config)
        {
            overlayConfig = config;
            hotkeyService?.ApplyConfig(config);

            // Update overlay
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.UpdateConfig(config);
            }

            ApplyOverlayOutputs();
            AppConfigStore.SaveOverlayConfig(AppConfigStore.DefaultPath, config);

            // Refresh context menu to show new hotkey
            PopulateInitialMenu();
        }


        // ── Overlay Config Persistence ───────────────────────────────

        // ── Hotkey Window ────────────────────────────────────────────

        private static void ToggleStartup()
        {
            try
            {
                StartupTaskService.Toggle();
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
            SyncRtssOverlay();
            UpdateRtssStatusMenu();
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
            hotkeyService?.Dispose();

            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.Close();
                overlayForm.Dispose();
            }

            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.Close();
                settingsForm.Dispose();
            }

             rtssOverlayClient?.Release();
             rtssOverlayClient?.Dispose();

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

        private static void ApplyOverlayOutputs()
        {
            if (overlayForm == null || overlayForm.IsDisposed)
            {
                overlayForm = new OverlayForm(computer, overlayConfig);
            }

            if (rtssOverlayClient == null)
            {
                rtssOverlayClient = new RtssOverlayClient();
            }

            if (overlayConfig.Enabled && overlayConfig.DesktopOverlayEnabled)
            {
                if (!overlayForm.Visible)
                {
                    overlayForm.Show();
                }

                overlayForm.RefreshData();
            }
            else if (overlayForm.Visible)
            {
                overlayForm.Hide();
            }

            if (overlayConfig.Enabled && overlayConfig.RtssOverlayEnabled)
            {
                SyncRtssOverlay();
            }
            else
            {
                rtssOverlayClient.Release();
            }
        }

        private static void SyncRtssOverlay()
        {
            if (rtssOverlayClient == null || !overlayConfig.Enabled || !overlayConfig.RtssOverlayEnabled)
            {
                return;
            }

            if (overlayForm == null || overlayForm.IsDisposed)
            {
                return;
            }

            string text = overlayForm.BuildOsdText();
            if (string.IsNullOrWhiteSpace(text))
            {
                rtssOverlayClient.Release();
            }
            else
            {
                rtssOverlayClient.Update(text);
            }
        }

        private static void OpenOverlaySettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new OverlaySettingsForm(computer, overlayConfig, OnOverlayConfigSaved);
            }

            settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized)
            {
                settingsForm.WindowState = FormWindowState.Normal;
            }

            settingsForm.BringToFront();
            settingsForm.Activate();
        }

        private static void UpdateRtssStatusMenu()
        {
            if (rtssStatusItem == null)
            {
                return;
            }

            if (!overlayConfig.RtssOverlayEnabled)
            {
                rtssStatusItem.Text = "RTSS: disabled";
                return;
            }

            var snapshot = RtssOverlayClient.GetStatusSnapshot();
            if (!snapshot.IsProcessRunning)
            {
                rtssStatusItem.Text = "RTSS: not running";
                return;
            }

            if (!snapshot.HasSharedMemory)
            {
                rtssStatusItem.Text = "RTSS: shared memory unavailable";
                return;
            }

            if (!snapshot.IsSlotOwned)
            {
                rtssStatusItem.Text = "RTSS: waiting for slot";
                return;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.LastForegroundAppName))
            {
                string appName = Path.GetFileName(snapshot.LastForegroundAppName);
                rtssStatusItem.Text = $"RTSS: {appName} ({snapshot.LastForegroundApi})";
                return;
            }

            rtssStatusItem.Text = "RTSS: ready";
        }
    }
}
