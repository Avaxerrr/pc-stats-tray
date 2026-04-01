using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class DashboardAssetEmbeddingTests
    {
        [TestMethod]
        public void DashboardAssets_AreEmbeddedInAppAssembly()
        {
            var resourceNames = typeof(OverlayConfig).Assembly.GetManifestResourceNames();

            CollectionAssert.Contains(resourceNames, "PCStatsTray.Web.dashboard.html");
            CollectionAssert.Contains(resourceNames, "PCStatsTray.Web.dashboard.css");
            CollectionAssert.Contains(resourceNames, "PCStatsTray.Web.dashboard.js");
            CollectionAssert.Contains(resourceNames, "PCStatsTray.Web.fonts.FiraCode-Regular.woff2");
        }
    }
}
