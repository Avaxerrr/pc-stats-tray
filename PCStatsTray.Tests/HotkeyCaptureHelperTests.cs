using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests
{
    [TestClass]
    public class HotkeyCaptureHelperTests
    {
        [TestMethod]
        public void TryBuild_ReturnsBinding_WhenModifierExists()
        {
            bool success = HotkeyCaptureHelper.TryBuild(Keys.S, control: true, alt: false, shift: true, out var binding, out var message);

            Assert.IsTrue(success);
            Assert.AreEqual("Ctrl+Shift+S", binding.Display);
            Assert.AreEqual((int)Keys.S, binding.VirtualKey);
            Assert.AreEqual("Ctrl+Shift+S", message);
        }

        [TestMethod]
        public void TryBuild_ReturnsModifierWarning_WhenNoModifierExists()
        {
            bool success = HotkeyCaptureHelper.TryBuild(Keys.S, control: false, alt: false, shift: false, out _, out var message);

            Assert.IsFalse(success);
            Assert.AreEqual("Need a modifier key", message);
        }
    }
}
