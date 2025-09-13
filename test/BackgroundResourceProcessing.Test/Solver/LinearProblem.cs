using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class LinearProblemTest
    {
        [TestCleanup]
        public void Cleanup()
        {
            TestAllocator.Cleanup();
        }

        // This tests the textbook example from [0]. Which includes the
        // solution so we can easily verify that things are working as
        // expected (along with individual steps).
        //
        // [0]: https://math.libretexts.org/Bookshelves/Applied_Mathematics/Applied_Finite_Mathematics_(Sekhon_and_Bloom)/04%3A_Linear_Programming_The_Simplex_Method/4.02%3A_Maximization_By_The_Simplex_Method
        [TestMethod]
        public void TestTextbookExample()
        {
            LinearProblem problem = new();
            var x1 = problem.CreateVariable();
            var x2 = problem.CreateVariable();

            problem.AddConstraint(new LinearEquation() { x1, x2 } <= 12.0).Unwrap();
            problem.AddConstraint(new LinearEquation() { 2.0 * x1, x2 } <= 16.0).Unwrap();

            var soln = problem.Maximize([40.0 * x1, 30.0 * x2]).Unwrap();

            Assert.AreEqual(4.0, soln[x1]);
            Assert.AreEqual(8.0, soln[x2]);
        }

        // This tests the example from
        // https://optimization.cbe.cornell.edu/index.php?title=Simplex_algorithm
        [TestMethod]
        public void TestCornellExample()
        {
            LinearProblem problem = new();
            var x1 = problem.CreateVariable();
            var x2 = problem.CreateVariable();
            var x3 = problem.CreateVariable();

            problem.AddConstraint(new LinearEquation() { 2.0 * x1, x2, x3 } <= 2.0).Unwrap();
            problem.AddConstraint(new LinearEquation() { x1, 2.0 * x2, 3.0 * x3 } <= 4.0).Unwrap();
            problem.AddConstraint(new LinearEquation() { 2.0 * x1, 2.0 * x2, x3 } <= 8.0).Unwrap();

            var soln = problem.Maximize([4.0 * x1, x2, 4.0 * x3]).Unwrap();

            Assert.AreEqual(0.4, soln[x1], 1e-6);
            Assert.AreEqual(0.0, soln[x2], 1e-6);
            Assert.AreEqual(1.2, soln[x3], 1e-6);
        }

        // This tests the example from
        // https://en.wikipedia.org/wiki/Simplex_algorithm#Implementation
        //
        // It has been tweaked so as to maximize the objective function instead
        // minimize.
        [TestMethod]
        public void TestWikipediaExample()
        {
            LinearProblem problem = new();
            var x = problem.CreateVariable();
            var y = problem.CreateVariable();
            var z = problem.CreateVariable();

            problem.AddConstraint(new LinearEquation() { 3 * x, 2 * y, 1 * z } <= 10).Unwrap();
            problem.AddConstraint(new LinearEquation() { 2 * x, 5 * y, 3 * z } <= 15).Unwrap();

            var soln = problem.Maximize([2 * x, 3 * y, 4 * z]).Unwrap();

            Assert.AreEqual(0.0, soln[x]);
            Assert.AreEqual(0.0, soln[y]);
            Assert.AreEqual(5.0, soln[z], 1e-6);
        }

        // This is derived from an actual test case that ended up returning NaNs
        // when optimizing.
        [TestMethod]
        public void RegressionNan1()
        {
            LinearProblem problem = new();
            var x1 = problem.CreateVariable();
            var x2 = problem.CreateVariable();
            var x3 = problem.CreateVariable();
            var x4 = problem.CreateVariable();

            problem.AddConstraint(x1 <= 1).Unwrap();
            problem.AddConstraint(x2 <= 1).Unwrap();
            problem.AddConstraint(x3 <= 1).Unwrap();
            problem.AddConstraint(x4 <= 1).Unwrap();

            problem
                .AddConstraint(new LinearEquation() { -37.46 * x1, 54 * x3, -9.375 * x4 } <= 0.0)
                .Unwrap();

            var soln = problem.Maximize([x1, 7.96 * x2, 3 * x3, x4]).Unwrap();

            Assert.AreEqual(1, soln[x1], 1e-6);
            Assert.AreEqual(1, soln[x2], 1e-6);
            Assert.AreEqual(9367.0 / 10800.0, soln[x3], 1e-6);
            Assert.AreEqual(1, soln[x4], 1e-6);
        }

        [TestMethod]
        public void OrConstraint1()
        {
            LinearProblem problem = new();
            var x0 = problem.CreateVariable();
            var x1 = problem.CreateVariable();
            var x2 = problem.CreateVariable();

            problem.AddConstraint(x0 <= 1).Unwrap();
            problem.AddConstraint(x1 <= 1).Unwrap();
            problem.AddConstraint(x2 <= 1).Unwrap();

            problem
                .AddOrConstraint(
                    new LinearEquation() { x2, x0 } <= 0.2,
                    new LinearEquation() { x2, x1 } >= 0.8
                )
                .Unwrap();

            var soln = problem.Maximize([x0, x1, -x2]);

            LogUtil.Log($"{soln}");
        }
    }
}
