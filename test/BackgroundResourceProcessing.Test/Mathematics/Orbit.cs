using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Mathematics;
using BRPOrbit = BackgroundResourceProcessing.Mathematics.Orbit;

namespace BackgroundResourceProcessing.Test.Mathematics;

[TestClass]
public sealed class OrbitTests
{
    [TestCleanup]
    public void Cleanup() => TestAllocator.Cleanup();

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

    [TestMethod]
    public void TestGetUTAtKnownTA()
    {
        var tA = 1.65069858643016;
        var UT = circular.GetUTAtTrueAnomaly(tA, CurrentUT);

        Assert.AreEqual(2895.60450391238, UT, 1e-6);
    }

    [TestMethod]
    public void TestGetObTAtKnownTA()
    {
        var tA = 1.65069858643016;
        var ObT = circular.GetObTAtTrueAnomaly(tA);

        Assert.AreEqual(878.379163815299, ObT, 1e-6);
    }

    [TestMethod]
    public void TestGetEccAnomalyAtKnownTA()
    {
        var tA = 1.65069858643016;
        var E = circular.GetEccentricAnomaly(tA);

        Assert.AreEqual(1.65069858631473, E, 1e-6);
    }
}
