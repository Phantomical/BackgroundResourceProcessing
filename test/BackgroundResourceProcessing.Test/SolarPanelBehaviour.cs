using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test;

/// <summary>
/// Tests for how <see cref="SolarPanelBehaviour"/> picks the star a panel is
/// pointed at when it knows its exposure to multiple stars.
/// </summary>
///
/// <remarks>
/// Selection only looks at body names, so ordinary bodies stand in for stars
/// here. That keeps the tests working in a stock single-star install.
/// </remarks>
public sealed class SolarPanelBehaviourTests : BRPTestBase
{
    static CelestialBody Body(int index) => FlightGlobals.Bodies[index];

    static SolarPanelBehaviour.StarTracking Entry(CelestialBody tracked, params double[] factors)
    {
        var entry = new SolarPanelBehaviour.StarTracking { StarName = tracked.bodyName };
        for (int i = 0; i < factors.Length; ++i)
            entry.Exposures.Add(new() { StarName = Body(i).bodyName, Factor = factors[i] });
        return entry;
    }

    static VesselState Lit(params CelestialBody[] stars) =>
        new(0.0)
        {
            ShadowState = new ShadowState(double.PositiveInfinity, false, stars[0])
            {
                LitStars = [.. stars],
            },
        };

    static SolarPanelBehaviour TwoStarPanel() =>
        new() { Tracking = [Entry(Body(0), 1.0, 0.25), Entry(Body(1), 0.5, 1.0)] };

    /// <summary>
    /// While the brightest star is shadowed the panel points at the next one,
    /// and it switches back once the brightest star is lit again.
    /// </summary>
    [TestInfo("SolarPanelBehaviourTests_TracksBrightestLitStar")]
    public void TracksBrightestLitStar()
    {
        var panel = TwoStarPanel();

        Assert.AreEqual(Body(1).bodyName, panel.SelectTracking(Lit(Body(1))).StarName);
        Assert.AreEqual(Body(0).bodyName, panel.SelectTracking(Lit(Body(0), Body(1))).StarName);
        Assert.AreEqual(Body(1).bodyName, panel.SelectTracking(Lit(Body(1), Body(0))).StarName);
    }

    /// <summary>
    /// A panel with a single entry (Kopernicus' manual tracking) stays pointed
    /// at that star even when it is not lit.
    /// </summary>
    [TestInfo("SolarPanelBehaviourTests_ManualTrackingKeepsTarget")]
    public void ManualTrackingKeepsTarget()
    {
        var panel = new SolarPanelBehaviour { Tracking = [Entry(Body(0), 1.0, 0.25)] };
        var tracking = panel.SelectTracking(Lit(Body(1)));

        Assert.AreEqual(Body(0).bodyName, tracking.StarName);
        Assert.AreEqual(0.25, tracking.GetExposure(Body(1)), 0.0);
    }

    [TestInfo("SolarPanelBehaviourTests_TrackingRoundTrips")]
    public void TrackingRoundTrips()
    {
        var panel = TwoStarPanel();
        panel.ResourceName = "ElectricCharge";

        var node = new ConfigNode();
        panel.Save(node);

        var loaded = (SolarPanelBehaviour)ConverterBehaviour.Load(node);

        Assert.AreEqual(2, loaded.Tracking.Count);
        for (int i = 0; i < 2; ++i)
        {
            Assert.AreEqual(panel.Tracking[i].StarName, loaded.Tracking[i].StarName);
            Assert.AreEqual(2, loaded.Tracking[i].Exposures.Count);
            for (int j = 0; j < 2; ++j)
            {
                Assert.AreEqual(
                    panel.Tracking[i].Exposures[j].Factor,
                    loaded.Tracking[i].GetExposure(Body(j)),
                    0.0
                );
            }
        }
    }

    [TestInfo("SolarPanelBehaviourTests_NoTrackingLoadsAsNull")]
    public void NoTrackingLoadsAsNull()
    {
        var panel = new SolarPanelBehaviour { ResourceName = "ElectricCharge" };

        var node = new ConfigNode();
        panel.Save(node);

        var loaded = (SolarPanelBehaviour)ConverterBehaviour.Load(node);
        Assert.IsNull(loaded.Tracking);
    }
}
