using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Utils
{
    public sealed class MathUtilTest : BRPTestBase
    {
        [TestInfo("MathUtilTest_TestCountTrailingZeros")]
        public void TestCountTrailingZeros()
        {
            Assert.AreEqual(8, MathUtil.TrailingZeroCount(0x100));
            Assert.AreEqual(0, MathUtil.TrailingZeroCount(0x101));
            Assert.AreEqual(64, MathUtil.TrailingZeroCount(0));
            Assert.AreEqual(63, MathUtil.TrailingZeroCount(1ul << 63));
        }
    }
}
