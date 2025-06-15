using BackgroundResourceProcessing.Solver;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class LinearConstraintTests
    {
        [TestMethod]
        public void SubstituteWithNegativeCoefficient()
        {
            LinearProblem problem = new();

            var x0 = problem.CreateVariable();
            var x1 = problem.CreateVariable();
            var x2 = problem.CreateVariable();

            LinearConstraint c = x0 - x1 == 1.0;
            c.Substitute(1, new LinearEquation(-x2), 1.0);

            Assert.AreEqual(2.0, c.constant);
            Assert.AreEqual(1.0, c.variables[2].Coef);
        }
    }
}
