using BackgroundResourceProcessing.Mathematics;
using KSP.Testing;
using BRPOrbit = BackgroundResourceProcessing.Mathematics.Orbit;

namespace BackgroundResourceProcessing.Test.Mathematics;

public sealed class OrbitShadowTests : BRPTestBase
{
    /// <summary>
    /// A vessel orbiting a star directly (ParentBodyIndex == starIndex) is never
    /// shadowed by it, so ComputeOrbitTerminator must report it lit with an
    /// infinite terminator.
    /// </summary>
    ///
    /// <remarks>
    /// GetOrbitShadowState / ScheduleOrbitShadowState rely on this rather than
    /// short-circuiting the star loop, so that an earlier (brighter) lit star is
    /// never discarded. Regression test for the async path dropping such a star.
    /// </remarks>
    [TestInfo("OrbitShadowTests_VesselOrbitingStarIsAlwaysLit")]
    public void VesselOrbitingStarIsAlwaysLit()
    {
        var savedBurst = DebugSettings.Instance.EnableBurst;
        // Exercise the managed math path directly (no Burst compilation needed).
        DebugSettings.Instance.EnableBurst = false;
        try
        {
            var bodies = new SystemBody[1];
            var vessel = new BRPOrbit { ParentBodyIndex = 0 };

            var term = OrbitShadow.ComputeOrbitTerminator(bodies, vessel, starIndex: 0, UT: 1000.0);

            Assert.IsFalse(term.InShadow);
            Assert.IsTrue(double.IsPositiveInfinity(term.UT));
        }
        finally
        {
            DebugSettings.Instance.EnableBurst = savedBurst;
        }
    }
}
