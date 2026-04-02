using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class DashboardSnapshotBuilderTests
    {
        [TestMethod]
        public void Build_UsesDashboardCatalogInsteadOfOverlaySelection()
        {
            var config = new OverlayConfig
            {
                Metrics = new List<OverlayMetric>
                {
                    new("CpuTemp", "CPU Temp", true),
                    new("GpuLoad", "GPU Load", false)
                }
            };

            var snapshot = DashboardSnapshotBuilder.Build(
                config,
                new Dictionary<string, string>
                {
                    ["CpuTemp"] = "72\u00B0C",
                    ["GpuLoad"] = "91%",
                    ["CpuClock"] = "5200 MHz",
                    ["CpuClockEffectiveAvg"] = "4100 MHz"
                },
                1000);

            Assert.AreEqual(4, snapshot.Metrics.Count);
            Assert.AreEqual(1000, snapshot.RefreshIntervalMs);

            var cpuTemp = snapshot.Metrics.Single(metric => metric.Key == "CpuTemp");
            Assert.AreEqual("CPU", cpuTemp.Group);
            Assert.AreEqual("72\u00B0C", cpuTemp.Value);
            Assert.AreEqual(string.Empty, cpuTemp.SourceName);
            Assert.IsTrue(cpuTemp.Available);
            Assert.IsTrue(cpuTemp.DefaultVisible);

            var gpuLoad = snapshot.Metrics.Single(metric => metric.Key == "GpuLoad");
            Assert.AreEqual("GPU", gpuLoad.Group);
            Assert.AreEqual("91%", gpuLoad.Value);
            Assert.IsTrue(gpuLoad.Available);
            Assert.IsTrue(gpuLoad.DefaultVisible);

            var cpuClock = snapshot.Metrics.Single(metric => metric.Key == "CpuClock");
            Assert.IsFalse(cpuClock.DefaultVisible);

            var effectiveClock = snapshot.Metrics.Single(metric => metric.Key == "CpuClockEffectiveAvg");
            Assert.AreEqual("CPU", effectiveClock.Group);
            Assert.AreEqual("4100 MHz", effectiveClock.Value);
            Assert.IsFalse(effectiveClock.DefaultVisible);
        }

        [TestMethod]
        public void Build_PreservesSourceNamesForDeviceSpecificCards()
        {
            var snapshot = DashboardSnapshotBuilder.Build(
                new[]
                {
                    new DashboardMetricValue
                    {
                        Key = "StorageTemp::drive0",
                        Label = "Storage Temp",
                        Group = "Storage",
                        SourceName = "Samsung SSD 990 PRO",
                        Value = "56\u00B0C",
                        DefaultVisible = true
                    },
                    new DashboardMetricValue
                    {
                        Key = "StorageTemp::drive1",
                        Label = "Storage Temp",
                        Group = "Storage",
                        SourceName = "Samsung SSD 990 PRO #2",
                        Value = "49\u00B0C",
                        DefaultVisible = true
                    }
                },
                1000);

            Assert.AreEqual(2, snapshot.Metrics.Count);

            var firstDrive = snapshot.Metrics.Single(metric => metric.Key == "StorageTemp::drive0");
            Assert.AreEqual("Storage", firstDrive.Group);
            Assert.AreEqual("Samsung SSD 990 PRO", firstDrive.SourceName);
            Assert.AreEqual("56\u00B0C", firstDrive.Value);
            Assert.IsTrue(firstDrive.DefaultVisible);

            var secondDrive = snapshot.Metrics.Single(metric => metric.Key == "StorageTemp::drive1");
            Assert.AreEqual("Samsung SSD 990 PRO #2", secondDrive.SourceName);
        }

        [TestMethod]
        public void Build_PreservesSourceNamesForNetworkCards()
        {
            var snapshot = DashboardSnapshotBuilder.Build(
                new[]
                {
                    new DashboardMetricValue
                    {
                        Key = "NetworkDownload::wifi0",
                        Label = "Network Down",
                        Group = "Network",
                        SourceName = "Intel Wi-Fi 6 AX201",
                        Value = "12.4 MB/s",
                        DefaultVisible = true
                    },
                    new DashboardMetricValue
                    {
                        Key = "NetworkUpload::lan0",
                        Label = "Network Up",
                        Group = "Network",
                        SourceName = "Realtek Gaming 2.5GbE",
                        Value = "522 KB/s",
                        DefaultVisible = true
                    }
                },
                1000);

            Assert.AreEqual(2, snapshot.Metrics.Count);

            var wifiCard = snapshot.Metrics.Single(metric => metric.Key == "NetworkDownload::wifi0");
            Assert.AreEqual("Network", wifiCard.Group);
            Assert.AreEqual("Intel Wi-Fi 6 AX201", wifiCard.SourceName);
            Assert.AreEqual("12.4 MB/s", wifiCard.Value);

            var lanCard = snapshot.Metrics.Single(metric => metric.Key == "NetworkUpload::lan0");
            Assert.AreEqual("Realtek Gaming 2.5GbE", lanCard.SourceName);
            Assert.AreEqual("522 KB/s", lanCard.Value);
        }

        [TestMethod]
        public void Build_DeviceSpecificNetworkCardsCanBeHiddenByDefaultForNoisyAdapters()
        {
            var snapshot = DashboardSnapshotBuilder.Build(
                new[]
                {
                    new DashboardMetricValue
                    {
                        Key = "NetworkDownload::wifi0",
                        Label = "Network Down",
                        Group = "Network",
                        SourceName = "Wi-Fi",
                        Value = "12.4 MB/s",
                        DefaultVisible = true
                    },
                    new DashboardMetricValue
                    {
                        Key = "NetworkDownload::filter0",
                        Label = "Network Down",
                        Group = "Network",
                        SourceName = "Wi-Fi-WFP Native MAC Layer LightWeight Filter-0000",
                        Value = "12.4 MB/s",
                        DefaultVisible = false
                    }
                },
                1000);

            var wifiCard = snapshot.Metrics.Single(metric => metric.Key == "NetworkDownload::wifi0");
            Assert.IsTrue(wifiCard.DefaultVisible);

            var noisyCard = snapshot.Metrics.Single(metric => metric.Key == "NetworkDownload::filter0");
            Assert.IsFalse(noisyCard.DefaultVisible);
        }

        [TestMethod]
        public void Build_PreservesSourceNamesForCpuGpuAndBatteryCards()
        {
            var snapshot = DashboardSnapshotBuilder.Build(
                new[]
                {
                    new DashboardMetricValue
                    {
                        Key = "CpuTemp",
                        Label = "CPU Temp",
                        Group = "CPU",
                        SourceName = "AMD Ryzen 9 7940HS",
                        Value = "74\u00B0C",
                        DefaultVisible = true
                    },
                    new DashboardMetricValue
                    {
                        Key = "GpuTemp",
                        Label = "GPU Temp",
                        Group = "GPU",
                        SourceName = "NVIDIA GeForce RTX 4060 Laptop GPU",
                        Value = "61\u00B0C",
                        DefaultVisible = true
                    },
                    new DashboardMetricValue
                    {
                        Key = "BatteryLevel",
                        Label = "Battery Level",
                        Group = "Power",
                        SourceName = "Internal Battery",
                        Value = "55%",
                        DefaultVisible = true
                    }
                },
                1000);

            Assert.AreEqual("AMD Ryzen 9 7940HS", snapshot.Metrics.Single(metric => metric.Key == "CpuTemp").SourceName);
            Assert.AreEqual("NVIDIA GeForce RTX 4060 Laptop GPU", snapshot.Metrics.Single(metric => metric.Key == "GpuTemp").SourceName);
            Assert.AreEqual("Internal Battery", snapshot.Metrics.Single(metric => metric.Key == "BatteryLevel").SourceName);
        }

        [TestMethod]
        public void BuildMemorySourceName_UsesActualModulePartNumbers()
        {
            string sourceName = DashboardSnapshotBuilder.BuildMemorySourceName(
                new[]
                {
                    new PhysicalMemoryModuleInfo
                    {
                        PartNumber = "LD4AS016G-3200ST",
                        CapacityBytes = 17179869184
                    },
                    new PhysicalMemoryModuleInfo
                    {
                        PartNumber = "CT16G4SFD832A.16FE1",
                        CapacityBytes = 17179869184
                    }
                });

            Assert.AreEqual("32 GB (LD4AS016G-3200ST + CT16G4SFD832A.16FE1)", sourceName);
        }

        [TestMethod]
        public void BuildMemorySourceName_CollapsesIdenticalModulesIntoCount()
        {
            string sourceName = DashboardSnapshotBuilder.BuildMemorySourceName(
                new[]
                {
                    new PhysicalMemoryModuleInfo
                    {
                        PartNumber = "CT16G4SFD832A.16FE1",
                        CapacityBytes = 17179869184
                    },
                    new PhysicalMemoryModuleInfo
                    {
                        PartNumber = "CT16G4SFD832A.16FE1",
                        CapacityBytes = 17179869184
                    }
                });

            Assert.AreEqual("32 GB (2x CT16G4SFD832A.16FE1)", sourceName);
        }
    }
}
