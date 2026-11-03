using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Mathematics;
using KSP.Testing;
using BRPOrbit = BackgroundResourceProcessing.Mathematics.Orbit;

namespace BackgroundResourceProcessing.Test.Mathematics;

public sealed class OrbitTests : BRPTestBase
{
    static readonly BRPOrbit circular = new()
    {
        ArgumentOfPeriapsis = 240.076778361441,
        Eccentricity = 0.0,
        Epoch = 1223.86227020244,
        GravParameter = 3531600000000,
        Inclination = 0.0,
        LAN = 333.829905388086,
        MeanAnomalyAtEpoch = 4.79225365514737,
        MeanMotion = 0.00187925517157008,
        ObTAtEpoch = 2550.08139801661,
        Period = 3343.44446791125,
        SemiMajorAxis = 1000000.000012,
        SemiLatusRectum = 1000000.000012,
        OrbitFrame = new()
        {
            X = new(-0.829947216564932, -0.55784192896924, -2.64009358808185E-13),
            Y = new(0.55784192896924, -0.829947216564932, -1.51954529647069E-13),
            Z = new(-1.34347224556011E-13, -2.73389728908509E-13, 1),
        },
    };

    static readonly double CurrentUT = 1223.88227020244;

    [TestInfo("OrbitTests_TestGetUTAtKnownTA")]
    public void TestGetUTAtKnownTA()
    {
        var tA = 1.65069858643016;
        var UT = circular.GetUTAtTrueAnomaly(tA, CurrentUT);

        Assert.AreEqual(2895.60450391238, UT, 1e-6);
    }

    /// <summary>
    /// GetUTAtTrueAnomaly must return the <em>first</em> time at or after UT
    /// at which the orbit reaches the true anomaly, even when ObTAtEpoch is
    /// negative and the epoch is recent. It used to only ever add periods, so
    /// it could return a time a whole orbit too late.
    /// </summary>
    [TestInfo("OrbitTests_TestGetUTAtTrueAnomalyIsNextOccurrence")]
    public void TestGetUTAtTrueAnomalyIsNextOccurrence()
    {
        var orbit = circular;
        orbit.Eccentricity = 0.9;
        orbit.SemiLatusRectum = orbit.SemiMajorAxis * (1.0 - 0.9 * 0.9);
        orbit.MeanAnomalyAtEpoch = -2.5;
        orbit.ObTAtEpoch = orbit.MeanAnomalyAtEpoch / orbit.MeanMotion;
        orbit.Epoch = CurrentUT;

        for (int i = 0; i < 64; ++i)
        {
            var UT = CurrentUT + i * orbit.Period / 37.0;
            for (int j = 0; j < 16; ++j)
            {
                var tA = -Math.PI + j * (2.0 * Math.PI / 16.0);
                var result = orbit.GetUTAtTrueAnomaly(tA, UT);

                Assert.IsTrue(
                    result >= UT - 1e-6 && result < UT + orbit.Period,
                    $"GetUTAtTrueAnomaly({tA}, {UT}) returned {result}, which is not within one period after UT"
                );

                var actual = orbit.GetTrueAnomalyAtUT(result);
                var diff = Math.IEEERemainder(actual - tA, 2.0 * Math.PI);
                Assert.AreEqual(0.0, diff, 1e-6);
            }
        }
    }

    [TestInfo("OrbitTests_TestGetObTAtKnownTA")]
    public void TestGetObTAtKnownTA()
    {
        var tA = 1.65069858643016;
        var ObT = circular.GetObTAtTrueAnomaly(tA);

        Assert.AreEqual(878.379163815299, ObT, 1e-6);
    }

    [TestInfo("OrbitTests_TestGetEccAnomalyAtKnownTA")]
    public void TestGetEccAnomalyAtKnownTA()
    {
        var tA = 1.65069858643016;
        var E = circular.GetEccentricAnomaly(tA);

        Assert.AreEqual(1.65069858631473, E, 1e-6);
    }
}
