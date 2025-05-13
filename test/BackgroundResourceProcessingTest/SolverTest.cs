using BackgroundResourceProcessing.VesselModules;

namespace BackgroundResourceProcessingTest
{
    [TestClass]
    public sealed class SolverTest
    {
        [TestMethod]
        public void SolveSolarPanelOnly()
        {
            var module = TestUtil.LoadVessel("solar-panels-only.cfg");
        }
    }
}
