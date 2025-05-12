using UnifiedBackgroundProcessing.VesselModules;

namespace UnifiedBackgroundProcessingTest
{
    [TestClass]
    public sealed class SolverTest
    {
        [TestMethod]
        public void SolveSolarPanelOnly()
        {
            var module = TestUtil.LoadVessel("solar-panels-only.cfg");
        }

        [TestMethod]
        public void TestMethod1() { }
    }
}
