using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HwMonTray.Tests
{
    [TestClass]
    public class AppConfigStoreTests
    {
        [TestMethod]
        public void SaveOverlayConfig_PreservesHiddenSensors()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

            try
            {
                AppConfigStore.SaveHiddenSensors(path, new[] { "cpu/temp", "gpu/temp" });

                var overlay = new OverlayConfig
                {
                    Enabled = false,
                    FontFamily = "Bahnschrift"
                };

                AppConfigStore.SaveOverlayConfig(path, overlay);

                var hiddenSensors = AppConfigStore.LoadHiddenSensors(path);
                var reloadedOverlay = AppConfigStore.LoadOverlayConfig(path);

                CollectionAssert.AreEquivalent(new[] { "cpu/temp", "gpu/temp" }, hiddenSensors.ToArray());
                Assert.IsFalse(reloadedOverlay.Enabled);
                Assert.AreEqual("Bahnschrift", reloadedOverlay.FontFamily);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void LoadOverlayConfig_ReturnsNormalizedDefaults_WhenFileIsMissing()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

            var overlay = AppConfigStore.LoadOverlayConfig(path);

            Assert.IsTrue(overlay.Enabled);
            Assert.IsTrue(overlay.Metrics.Count > 0);
            Assert.AreEqual("CpuTemp", overlay.Metrics[0].Key);
        }
    }
}
