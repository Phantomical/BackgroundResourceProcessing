namespace BackgroundResourceProcessing.Test.Solver.V2
{
    [TestClass]
    public sealed class Isru2Tests
    {
        [TestMethod]
        public void TestIteratedSolving()
        {
            var processor = TestUtil.LoadVessel("comet-retriever.cfg");
            processor.lastUpdate = 0.0;
            var currentTime = 0.0;

            processor.ValidateReferencedInventories();

            for (int i = 0; ; i++)
            {
                processor.ComputeRates();
                currentTime = processor.UpdateNextChangepoint(currentTime);
                TestUtil.DumpProcessor(processor, $"ship-{i}.cfg");

                if (double.IsNaN(currentTime) || double.IsInfinity(currentTime))
                    break;

                processor.UpdateInventories(currentTime);
                processor.lastUpdate = currentTime;
            }
        }
    }
}
