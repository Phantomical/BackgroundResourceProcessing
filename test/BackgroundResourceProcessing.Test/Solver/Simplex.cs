using System.Security.Cryptography.X509Certificates;
using BackgroundResourceProcessing.Solver.Simplex;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class SimplexSolverTest
    {
        // This tests the textbook example from [0]. Which includes the
        // solution so we can easily verify that things are working as
        // expected (along with individual steps).
        //
        // [0]: https://math.libretexts.org/Bookshelves/Applied_Mathematics/Applied_Finite_Mathematics_(Sekhon_and_Bloom)/04%3A_Linear_Programming_The_Simplex_Method/4.02%3A_Maximization_By_The_Simplex_Method
        [TestMethod]
        public void TestTextbookExample()
        {
            // Maximize Z = 40*x1 + 30*x2
            LinearProblem problem = new([40.0, 30.0]);
            // subject to
            // x1 + x2 <= 12
            problem.AddLEqualConstraint([1.0, 1.0], 12.0);
            // 2*x1 + x2 <= 16
            problem.AddLEqualConstraint([2.0, 1.0], 16.0);

            var soln = problem.Solve();

            Assert.AreEqual(4.0, soln[0]);
            Assert.AreEqual(8.0, soln[1]);
        }

        // This tests the example from
        // https://optimization.cbe.cornell.edu/index.php?title=Simplex_algorithm
        [TestMethod]
        public void TestCornellExample()
        {
            // Maximize Z = 4*x1 + x2 + 4*x3
            LinearProblem problem = new([4.0, 1.0, 4.0]);
            // subject to
            // 2*x1 + x2 + x3 <= 2
            problem.AddLEqualConstraint([2.0, 1.0, 1.0], 2.0);
            // x1 + 2*x2 + 3*x3 <= 4
            problem.AddLEqualConstraint([1.0, 2.0, 3.0], 4.0);
            // 2*x1 + 2*x2 + x3 <= 8
            problem.AddLEqualConstraint([2.0, 2.0, 1.0], 8.0);

            var soln = problem.Solve();

            Assert.AreEqual(0.4, soln[0], 1e-6);
            Assert.AreEqual(0.0, soln[1], 1e-6);
            Assert.AreEqual(1.2, soln[2], 1e-6);
        }

        // This tests the example from
        // https://en.wikipedia.org/wiki/Simplex_algorithm#Implementation
        //
        // It has been tweaked so as to maximize the objective function instead
        // minimize.
        [TestMethod]
        public void TestWikipediaExample()
        {
            // Maximize Z = 2*x + 3*y + 4*z
            LinearProblem problem = new([2.0, 3.0, 4.0]);
            // subject to
            // 3*x + 2*y + z <= 10
            problem.AddLEqualConstraint([3, 2, 1], 10);
            // 2*x + 5*y + 3*z <= 15
            problem.AddLEqualConstraint([2, 5, 3], 15);

            var soln = problem.Solve();

            Assert.AreEqual(0.0, soln[0]);
            Assert.AreEqual(0.0, soln[0]);
            Assert.AreEqual(5.0, soln[2], 1e-6);
        }

        // This is derived from an actual test case that ended up returning NaNs
        // when optimizing.
        [TestMethod]
        public void RegressionNan1()
        {
            LinearProblem problem = new([1.0, 7.96, 3, 1]);

            problem.AddLEqualConstraint([1, 0, 0, 0], 1);
            problem.AddLEqualConstraint([0, 1, 0, 0], 1);
            problem.AddLEqualConstraint([0, 0, 1, 0], 1);
            problem.AddLEqualConstraint([0, 0, 0, 1], 1);

            problem.AddLEqualConstraint([-37.46, 0, 54, -9.375], 0.0);

            LogUtil.Log(problem);

            var soln = problem.Solve();

            Assert.AreEqual(1, soln[0], 1e-6);
            Assert.AreEqual(1, soln[1], 1e-6);
            Assert.AreEqual(9367.0 / 10800.0, soln[2], 1e-6);
            Assert.AreEqual(1, soln[3], 1e-6);
        }
    }
}
