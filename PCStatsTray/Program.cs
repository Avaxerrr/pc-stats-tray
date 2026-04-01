using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace PCStatsTray
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
        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;
        private const int DefaultRefreshIntervalMs = 1000;

        private static NotifyIcon cpuTrayIcon = null!;
        private static NotifyIcon gpuTrayIcon = null!;
        private static Computer computer = null!;
        private static ContextMenuStrip contextMenu = null!;
        private static System.Windows.Forms.Timer refreshTimer = null!;
        private static DetailsForm? detailsForm;
        private static OverlaySettingsForm? settingsForm;

        // OSD Overlay
        private static OverlayForm? overlayForm;
        private static OverlayConfig overlayConfig = null!;
        private static GlobalHotkeyService? hotkeyService;
        private static RtssOverlayClient? rtssOverlayClient;
        private static ToolStripMenuItem? rtssStatusItem;
        private static ToolStripMenuItem? cpuSensorSetupItem;
        private static ToolStripMenuItem? dashboardStatusItem;
        private static LanDashboardServer? lanDashboardServer;
        private static CpuSensorSetupStatus cpuSensorSetupStatus = new();

        // Temperature tracking for tooltip stats
        private static float cpuMinTemp = float.MaxValue;
        private static float cpuMaxTemp = float.MinValue;
        private static double cpuSumTemp = 0;
        private static long cpuTempCount = 0;

        private static float gpuMinTemp = float.MaxValue;
        private static float gpuMaxTemp = float.MinValue;
        private static double gpuSumTemp = 0;
        private static long gpuTempCount = 0;
        private static float latestCpuTemp = 0;
        private static float latestGpuTemp = 0;

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

            lanDashboardServer = new LanDashboardServer();
            lanDashboardServer.ApplyConfig(overlayConfig);

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

            RefreshAllData();
            MaybeShowPawnIoPrompt();

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = DefaultRefreshIntervalMs;
            refreshTimer.Tick += (sender, e) => { RefreshAllData(); };
            refreshTimer.Start();

            Application.ApplicationExit += (s, e) => { CleanUp(); };

            Application.Run();
        }

        private static void PopulateInitialMenu()
        {
            contextMenu.Items.Clear();

            string selectedRefreshRate = GetRefreshRateMenuText();
            var refreshRateItem = new ToolStripMenuItem("Metrics Refresh Rate");
            var rate1s = new ToolStripMenuItem("1s", null, (s, e) => { refreshTimer.Interval = 1000; CheckRefreshRateMenu(refreshRateItem, "1s"); RefreshAllData(); })
            {
                Checked = selectedRefreshRate == "1s"
            };
            var rate2s = new ToolStripMenuItem("2s", null, (s, e) => { refreshTimer.Interval = 2000; CheckRefreshRateMenu(refreshRateItem, "2s"); RefreshAllData(); })
            {
                Checked = selectedRefreshRate == "2s"
            };
            var rate5s = new ToolStripMenuItem("5s", null, (s, e) => { refreshTimer.Interval = 5000; CheckRefreshRateMenu(refreshRateItem, "5s"); RefreshAllData(); })
            {
                Checked = selectedRefreshRate == "5s"
            };
            refreshRateItem.DropDownItems.AddRange(new ToolStripItem[] { rate1s, rate2s, rate5s });
            
            contextMenu.Items.Add(refreshRateItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add("Show All Sensors", null, (s, e) =>
            {
                if (detailsForm == null || detailsForm.IsDisposed)
                    detailsForm = new DetailsForm(computer);
                detailsForm.Show();
                detailsForm.BringToFront();
                detailsForm.RefreshFromCurrentSnapshot();
            });

            cpuSensorSetupItem = new ToolStripMenuItem(BuildCpuSensorSetupMenuText())
            {
                Available = ShouldShowCpuSensorSetupItem()
            };
            cpuSensorSetupItem.Click += (s, e) => OpenCpuSensorSetup();
            contextMenu.Items.Add(cpuSensorSetupItem);

            // OSD overlay menu items
            contextMenu.Items.Add(new ToolStripSeparator());

            var toggleOsdItem = new ToolStripMenuItem($"Toggle OSD  ({overlayConfig.HotkeyDisplay})");
            toggleOsdItem.Click += (s, e) => ToggleOverlay();
            contextMenu.Items.Add(toggleOsdItem);

            var toggleDesktopOsdItem = new ToolStripMenuItem($"Toggle Desktop OSD  ({overlayConfig.DesktopHotkeyDisplay})")
            {
                Checked = overlayConfig.DesktopOverlayEnabled
            };
            toggleDesktopOsdItem.Click += (s, e) => ToggleDesktopOverlay();
            contextMenu.Items.Add(toggleDesktopOsdItem);

            var toggleRtssOsdItem = new ToolStripMenuItem($"Toggle RTSS OSD  ({overlayConfig.RtssHotkeyDisplay})")
            {
                Checked = overlayConfig.RtssOverlayEnabled
            };
            toggleRtssOsdItem.Click += (s, e) => ToggleRtssOverlay();
            contextMenu.Items.Add(toggleRtssOsdItem);

            contextMenu.Items.Add($"OSD Settings…  ({overlayConfig.SettingsHotkeyDisplay})", null, (s, e) => OpenOverlaySettings());

            rtssStatusItem = new ToolStripMenuItem("RTSS: checking…")
            {
                Enabled = false
            };
            contextMenu.Items.Add(rtssStatusItem);
            UpdateRtssStatusMenu();

            contextMenu.Items.Add(new ToolStripSeparator());

            var togglePhoneDashboardItem = new ToolStripMenuItem("Enable LAN Dashboard")
            {
                Checked = overlayConfig.PhoneDashboardEnabled
            };
            togglePhoneDashboardItem.Click += (s, e) => TogglePhoneDashboard();
            contextMenu.Items.Add(togglePhoneDashboardItem);

            var openPhoneDashboardItem = new ToolStripMenuItem("Open LAN Dashboard")
            {
                Enabled = overlayConfig.PhoneDashboardEnabled && lanDashboardServer?.IsRunning == true
            };
            openPhoneDashboardItem.Click += (s, e) => OpenPhoneDashboard();
            contextMenu.Items.Add(openPhoneDashboardItem);

            var copyPhoneDashboardUrlItem = new ToolStripMenuItem("Copy Dashboard URL")
            {
                Enabled = overlayConfig.PhoneDashboardEnabled && lanDashboardServer?.IsRunning == true
            };
            copyPhoneDashboardUrlItem.Click += (s, e) => CopyPhoneDashboardUrl();
            contextMenu.Items.Add(copyPhoneDashboardUrlItem);

            dashboardStatusItem = new ToolStripMenuItem("Dashboard: checking...")
            {
                Enabled = false
            };
            contextMenu.Items.Add(dashboardStatusItem);
            UpdatePhoneDashboardStatusMenu();

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
            PopulateInitialMenu();
        }

        private static void ToggleDesktopOverlay()
        {
            overlayConfig.DesktopOverlayEnabled = !overlayConfig.DesktopOverlayEnabled;
            ApplyOverlayOutputs();
            AppConfigStore.SaveOverlayConfig(AppConfigStore.DefaultPath, overlayConfig);
            PopulateInitialMenu();
        }

        private static void ToggleRtssOverlay()
        {
            overlayConfig.RtssOverlayEnabled = !overlayConfig.RtssOverlayEnabled;
            ApplyOverlayOutputs();
            AppConfigStore.SaveOverlayConfig(AppConfigStore.DefaultPath, overlayConfig);
            PopulateInitialMenu();
        }

        private static void TogglePhoneDashboard()
        {
            overlayConfig.PhoneDashboardEnabled = !overlayConfig.PhoneDashboardEnabled;
            lanDashboardServer?.ApplyConfig(overlayConfig);
            AppConfigStore.SaveOverlayConfig(AppConfigStore.DefaultPath, overlayConfig);
            PopulateInitialMenu();
        }

        private static void OpenPhoneDashboard()
        {
            string url = lanDashboardServer?.GetLocalUrl() ?? $"http://localhost:{overlayConfig.PhoneDashboardPort}/";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open the LAN dashboard: " + ex.Message, "LAN Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void CopyPhoneDashboardUrl()
        {
            string? url = lanDashboardServer?.GetLanUrl() ?? lanDashboardServer?.GetLocalUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Clipboard.SetText(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to copy the dashboard URL: " + ex.Message, "LAN Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void OnHotkeyPressed(int hotkeyId)
        {
            switch (hotkeyId)
            {
                case GlobalHotkeyService.ToggleAllHotkeyId:
                    ToggleOverlay();
                    break;
                case GlobalHotkeyService.ToggleDesktopHotkeyId:
                    ToggleDesktopOverlay();
                    break;
                case GlobalHotkeyService.ToggleRtssHotkeyId:
                    ToggleRtssOverlay();
                    break;
                case GlobalHotkeyService.OpenSettingsHotkeyId:
                    OpenOverlaySettings();
                    break;
            }
        }

        private static void OnOverlayConfigSaved(OverlayConfig config)
        {
            overlayConfig = config;
            hotkeyService?.ApplyConfig(config);
            lanDashboardServer?.ApplyConfig(config);
            lanDashboardServer?.UpdateSnapshot(DashboardSnapshotBuilder.Build(computer, overlayConfig, refreshTimer?.Interval ?? DefaultRefreshIntervalMs));

            // Update overlay
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.UpdateConfig(config);
            }

            ApplyOverlayOutputs();
            AppConfigStore.SaveOverlayConfig(AppConfigStore.DefaultPath, config);

            // Refresh context menu to show new hotkey
            PopulateInitialMenu();
            RefreshAllData();
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

        private static string GetRefreshRateMenuText()
        {
            int intervalMs = refreshTimer?.Interval ?? DefaultRefreshIntervalMs;
            return intervalMs switch
            {
                1000 => "1s",
                5000 => "5s",
                _ => "2s"
            };
        }

        private static void RefreshAllData()
        {
            computer.Accept(new UpdateVisitor());
            cpuSensorSetupStatus = CpuSensorSetupAdvisor.Evaluate(computer);
            CaptureLatestTemperatures();
            lanDashboardServer?.UpdateSnapshot(DashboardSnapshotBuilder.Build(computer, overlayConfig, refreshTimer?.Interval ?? DefaultRefreshIntervalMs));
            UpdateCpuSensorSetupMenu();
            UpdateIcon();

            // Refresh all consumers from the same central cadence.
            detailsForm?.RefreshFromCurrentSnapshot();
            overlayForm?.RefreshData();
            SyncRtssOverlay();
            UpdateRtssStatusMenu();
            UpdatePhoneDashboardStatusMenu();
        }

        private static void MaybeShowPawnIoPrompt()
        {
            if (!cpuSensorSetupStatus.ShouldRecommendPawnIo)
            {
                return;
            }

            if (AppConfigStore.LoadSuppressPawnIoPrompt(AppConfigStore.DefaultPath))
            {
                return;
            }

            var result = MessageBox.Show(
                CpuSensorSetupAdvisor.BuildPromptMessage(cpuSensorSetupStatus, allowSuppress: true),
                "CPU Sensor Setup",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                OpenPawnIoWebsite();
                return;
            }

            if (result == DialogResult.Cancel)
            {
                AppConfigStore.SaveSuppressPawnIoPrompt(AppConfigStore.DefaultPath, suppress: true);
            }
        }

        private static void OpenCpuSensorSetup()
        {
            computer.Accept(new UpdateVisitor());
            cpuSensorSetupStatus = CpuSensorSetupAdvisor.Evaluate(computer);
            UpdateCpuSensorSetupMenu();

            if (cpuSensorSetupStatus.ShouldRecommendPawnIo)
            {
                var result = MessageBox.Show(
                    CpuSensorSetupAdvisor.BuildPromptMessage(cpuSensorSetupStatus, allowSuppress: false),
                    "CPU Sensor Setup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    OpenPawnIoWebsite();
                }

                return;
            }

            var manualResult = MessageBox.Show(
                CpuSensorSetupAdvisor.BuildManualHelpMessage(cpuSensorSetupStatus),
                "CPU Sensor Setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (manualResult == DialogResult.Yes)
            {
                OpenPawnIoWebsite();
            }
        }

        private static void OpenPawnIoWebsite()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = CpuSensorSetupAdvisor.OfficialUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open the official PawnIO site: " + ex.Message, "CPU Sensor Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool ShouldShowCpuSensorSetupItem()
        {
            return !cpuSensorSetupStatus.IsPawnIoInstalled;
        }

        private static string BuildCpuSensorSetupMenuText()
        {
            if (cpuSensorSetupStatus.ShouldRecommendPawnIo)
            {
                return "CPU Sensor Setup... (Recommended)";
            }

            if (cpuSensorSetupStatus.IsPawnIoInstalled)
            {
                return "CPU Sensor Setup... (Installed)";
            }

            return "CPU Sensor Setup...";
        }

        private static void UpdateCpuSensorSetupMenu()
        {
            if (cpuSensorSetupItem == null)
            {
                return;
            }

            cpuSensorSetupItem.Text = BuildCpuSensorSetupMenuText();
            cpuSensorSetupItem.Available = ShouldShowCpuSensorSetupItem();
        }

        private static void UpdateIcon()
        {
            float maxCpuTemp = latestCpuTemp;
            float maxGpuTemp = latestGpuTemp;

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

            float cpuAvg = cpuTempCount > 0 ? (float)(cpuSumTemp / cpuTempCount) : 0;
            float gpuAvg = gpuTempCount > 0 ? (float)(gpuSumTemp / gpuTempCount) : 0;

            string cpuTrayText = $"CPU: {maxCpuTemp:0}°C | Min: {(cpuMinTemp == float.MaxValue ? 0 : cpuMinTemp):0}° | Max: {(cpuMaxTemp == float.MinValue ? 0 : cpuMaxTemp):0}° | Avg: {cpuAvg:0}°";
            if (cpuTrayText.Length >= 64) cpuTrayText = cpuTrayText.Substring(0, 63);
            cpuTrayIcon.Text = cpuTrayText;

            string gpuTrayText = $"GPU: {maxGpuTemp:0}°C | Min: {(gpuMinTemp == float.MaxValue ? 0 : gpuMinTemp):0}° | Max: {(gpuMaxTemp == float.MinValue ? 0 : gpuMaxTemp):0}° | Avg: {gpuAvg:0}°";
            if (gpuTrayText.Length >= 64) gpuTrayText = gpuTrayText.Substring(0, 63);
            gpuTrayIcon.Text = gpuTrayText;

            SetSmoothedTrayIcon(cpuTrayIcon, maxCpuTemp);
            SetSmoothedTrayIcon(gpuTrayIcon, maxGpuTemp);
        }

        private static void CaptureLatestTemperatures()
        {
            float sampledCpuTemp = 0;
            float sampledGpuTemp = 0;

            foreach (var hw in computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    var cpuTempSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && sampledCpuTemp < 0.1f && s.Name.Contains("Core Max"))
                                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && sampledCpuTemp < 0.1f && s.Name.Contains("Package"))
                                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (cpuTempSensor?.Value.HasValue == true)
                    {
                        sampledCpuTemp = Math.Max(sampledCpuTemp, cpuTempSensor.Value.Value);
                    }
                }
                else if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel)
                {
                    var gpuTempSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && sampledGpuTemp < 0.1f && s.Name.Contains("Core"))
                                     ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (gpuTempSensor?.Value.HasValue == true)
                    {
                        sampledGpuTemp = Math.Max(sampledGpuTemp, gpuTempSensor.Value.Value);
                    }
                }
            }

            latestCpuTemp = sampledCpuTemp;
            latestGpuTemp = sampledGpuTemp;

            if (sampledCpuTemp > 0)
            {
                if (sampledCpuTemp < cpuMinTemp) cpuMinTemp = sampledCpuTemp;
                if (sampledCpuTemp > cpuMaxTemp) cpuMaxTemp = sampledCpuTemp;
                cpuSumTemp += sampledCpuTemp;
                cpuTempCount++;
            }

            if (sampledGpuTemp > 0)
            {
                if (sampledGpuTemp < gpuMinTemp) gpuMinTemp = sampledGpuTemp;
                if (sampledGpuTemp > gpuMaxTemp) gpuMaxTemp = sampledGpuTemp;
                gpuSumTemp += sampledGpuTemp;
                gpuTempCount++;
            }
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

        private static void SetSmoothedTrayIcon(NotifyIcon icon, float temp)
        {
            int iconSize = GetTrayIconSize();
            using Bitmap bmp = RenderTrayIconBitmap(temp, iconSize);

            IntPtr hIcon = bmp.GetHicon();
            Icon newIcon = Icon.FromHandle(hIcon);
            var oldIcon = icon.Icon;
            icon.Icon = newIcon;

            if (oldIcon != null)
            {
                DestroyIcon(oldIcon.Handle);
                oldIcon.Dispose();
            }
        }

        private static Bitmap RenderTrayIconBitmap(float temp, int iconSize)
        {
            const int supersampleScale = 4;
            int canvasSize = iconSize * supersampleScale;

            using var largeBmp = new Bitmap(canvasSize, canvasSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(largeBmp))
            {
                ConfigureTrayGraphics(g);

                string tempText = temp > 0 ? $"{temp:0}" : "--";
                using var textPath = CreateMaximizedTrayTextPath(tempText, canvasSize * 0.92f, canvasSize);
                using var backgroundPath = CreateTrayBackgroundPath(textPath, canvasSize);
                using var backgroundBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
                using var textBrush = new SolidBrush(TextColor(temp));
                g.FillPath(backgroundBrush, backgroundPath);
                g.FillPath(textBrush, textPath);
            }

            var finalBmp = new Bitmap(iconSize, iconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(finalBmp))
            {
                ConfigureTrayGraphics(g);
                g.DrawImage(largeBmp, new Rectangle(0, 0, iconSize, iconSize), new Rectangle(0, 0, canvasSize, canvasSize), GraphicsUnit.Pixel);
            }

            return finalBmp;
        }

        private static void ConfigureTrayGraphics(Graphics graphics)
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateMaximizedTrayTextPath(string text, float targetSize, int canvasSize)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            using var fontFamily = new FontFamily("Segoe UI");
            using var format = StringFormat.GenericTypographic;
            path.AddString(text, fontFamily, (int)FontStyle.Bold, canvasSize, Point.Empty, format);

            RectangleF bounds = path.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }

            float scale = Math.Min(targetSize / bounds.Width, targetSize / bounds.Height);
            float finalWidth = bounds.Width * scale;
            float finalHeight = bounds.Height * scale;
            float offsetX = (canvasSize - finalWidth) / 2f;
            float offsetY = (canvasSize - finalHeight) / 2f;

            using var transform = new System.Drawing.Drawing2D.Matrix();
            transform.Translate(-bounds.X, -bounds.Y, System.Drawing.Drawing2D.MatrixOrder.Append);
            transform.Scale(scale, scale, System.Drawing.Drawing2D.MatrixOrder.Append);
            transform.Translate(offsetX, offsetY, System.Drawing.Drawing2D.MatrixOrder.Append);
            path.Transform(transform);
            return path;
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateTrayBackgroundPath(System.Drawing.Drawing2D.GraphicsPath textPath, int canvasSize)
        {
            RectangleF textBounds = textPath.GetBounds();
            float padX = canvasSize * 0.04f;
            float padY = canvasSize * 0.07f;
            float x = Math.Max(0, textBounds.X - padX);
            float y = Math.Max(0, textBounds.Y - padY);
            float width = Math.Min(canvasSize - x, textBounds.Width + (padX * 2f));
            float height = Math.Min(canvasSize - y, textBounds.Height + (padY * 2f));
            float radius = Math.Max(canvasSize * 0.14f, 6f);

            return RoundedRect(Rectangle.Round(new RectangleF(x, y, width, height)), (int)Math.Round(radius));
        }

        private static int GetTrayIconSize()
        {
            int width = GetSystemMetrics(SM_CXSMICON);
            int height = GetSystemMetrics(SM_CYSMICON);
            int size = Math.Max(Math.Max(width, height), 16);
            return Math.Min(size, 64);
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

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static void CleanUp()
        {
            hotkeyService?.Dispose();
            refreshTimer?.Stop();
            refreshTimer?.Dispose();

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
             lanDashboardServer?.Dispose();

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

            string text = overlayForm.BuildOsdText(OverlayDisplayTarget.Rtss);
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
                settingsForm = new OverlaySettingsForm(computer, overlayConfig, OnOverlayConfigSaved,
                    () => overlayForm != null && !overlayForm.IsDisposed ? overlayForm.Size : Size.Empty,
                    () => overlayForm != null && !overlayForm.IsDisposed ? overlayForm.CurrentDpiScale : 1f);
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

        private static void UpdatePhoneDashboardStatusMenu()
        {
            if (dashboardStatusItem == null)
            {
                return;
            }

            if (!overlayConfig.PhoneDashboardEnabled)
            {
                dashboardStatusItem.Text = "Dashboard: disabled";
                return;
            }

            if (lanDashboardServer == null)
            {
                dashboardStatusItem.Text = "Dashboard: unavailable";
                return;
            }

            if (!lanDashboardServer.IsRunning)
            {
                dashboardStatusItem.Text = string.IsNullOrWhiteSpace(lanDashboardServer.LastError)
                    ? "Dashboard: stopped"
                    : $"Dashboard: {lanDashboardServer.LastError}";
                return;
            }

            string lanUrl = lanDashboardServer.GetLanUrl() ?? lanDashboardServer.GetLocalUrl();
            dashboardStatusItem.Text = $"Dashboard: {lanUrl}";
        }
    }
}





