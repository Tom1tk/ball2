using NUnit.Framework;
using Ball2.Core;

namespace Ball2.Tests.EditMode
{
    public class CanaryTests
    {
        [Test]
        public void Core_Is_Reachable()
        {
            Assert.AreEqual("Ball2.Core", CoreInfo.AssemblyName);
        }
    }
}
