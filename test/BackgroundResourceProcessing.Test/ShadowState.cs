using System.Collections.Generic;
using KSP.Testing;
using Term = BackgroundResourceProcessing.Mathematics.OrbitShadow.Terminator;

namespace BackgroundResourceProcessing.Test;

/// <summary>
/// Unit tests for the shared shadow-state aggregation and validation helpers used
/// by both the synchronous and asynchronous shadow paths.
/// </summary>
public sealed class ShadowStateTests : BRPTestBase
{
    static List<CelestialBody> NullStars(int n)
    {
        var list = new List<CelestialBody>(n);
        for (int i = 0; i < n; i++)
            list.Add(null);
        return list;
    }

    #region AggregateShadowState

    [TestInfo("ShadowStateTests_AggregateSingleLitStar")]
    public void AggregateSingleLitStar()
    {
        Term[] terms = [Term.Sun(500.0)];
        var state = ShadowState.AggregateShadowState(terms, NullStars(1));

        Assert.IsFalse(state.InShadow);
        Assert.AreEqual(500.0, state.NextTerminatorEstimate, 0.0);
    }

    [TestInfo("ShadowStateTests_AggregateSingleShadowedStar")]
    public void AggregateSingleShadowedStar()
    {
        Term[] terms = [Term.Shadow(500.0)];
        var state = ShadowState.AggregateShadowState(terms, NullStars(1));

        Assert.IsTrue(state.InShadow);
        Assert.AreEqual(500.0, state.NextTerminatorEstimate, 0.0);
    }

    [TestInfo("ShadowStateTests_AggregateAllShadowedReportsNearestSunrise")]
    public void AggregateAllShadowedReportsNearestSunrise()
    {
        Term[] terms = [Term.Shadow(800.0), Term.Shadow(300.0)];
        var state = ShadowState.AggregateShadowState(terms, NullStars(2));

        Assert.IsTrue(state.InShadow);
        Assert.AreEqual(300.0, state.NextTerminatorEstimate, 0.0);
    }

    /// <summary>
    /// Locks in the multi-star changepoint behaviour: when the vessel is lit by a
    /// weaker star but still inside a stronger (earlier-listed) star's umbra,
    /// emerging from that umbra raises the available power and is therefore a
    /// genuine changepoint. The reported terminator must be the earlier of the lit
    /// star's sunset and the stronger star's sunrise — NOT just the lit star's own
    /// sunset.
    /// </summary>
    [TestInfo("ShadowStateTests_AggregateStrongerShadowedSunriseWins")]
    public void AggregateStrongerShadowedSunriseWins()
    {
        // Index 0: stronger star, currently shadowed, rises at 200.
        // Index 1: weaker star, currently lighting the vessel, sets at 900.
        Term[] terms = [Term.Shadow(200.0), Term.Sun(900.0)];
        var state = ShadowState.AggregateShadowState(terms, NullStars(2));

        Assert.IsFalse(state.InShadow);
        Assert.AreEqual(200.0, state.NextTerminatorEstimate, 0.0);
    }

    /// <summary>
    /// The strongest currently-lit star wins outright; a later (weaker) star —
    /// shadowed or not — does not pull the terminator earlier.
    /// </summary>
    [TestInfo("ShadowStateTests_AggregateFirstLitStarWins")]
    public void AggregateFirstLitStarWins()
    {
        Term[] terms = [Term.Sun(900.0), Term.Shadow(200.0)];
        var state = ShadowState.AggregateShadowState(terms, NullStars(2));

        Assert.IsFalse(state.InShadow);
        Assert.AreEqual(900.0, state.NextTerminatorEstimate, 0.0);
    }

    [TestInfo("ShadowStateTests_AggregateMaxValueMapsToInfinity")]
    public void AggregateMaxValueMapsToInfinity()
    {
        Term[] terms = [Term.Sun(double.MaxValue)];
        var state = ShadowState.AggregateShadowState(terms, NullStars(1));

        Assert.IsFalse(state.InShadow);
        Assert.IsTrue(double.IsPositiveInfinity(state.NextTerminatorEstimate));
    }

    #endregion

    #region ValidateShadowState

    // The default test log sink throws on any error, so swap in one that counts
    // errors instead. ValidateShadowState logs (an expected) error before falling
    // back, and we want to assert on both the log and the returned fallback.
    sealed class CountingSink : LogUtil.ILogSink
    {
        public int Errors;

        public void Error(string message) => Errors++;

        public void Log(string message) { }

        public void Warn(string message) { }
    }

    static (ShadowState Result, int Errors) Validate(ShadowState input, double currentUT)
    {
        var saved = LogUtil.Sink;
        var sink = new CountingSink();
        LogUtil.Sink = sink;
        try
        {
            return (ShadowState.ValidateShadowState(input, currentUT), sink.Errors);
        }
        finally
        {
            LogUtil.Sink = saved;
        }
    }

    [TestInfo("ShadowStateTests_ValidateRejectsNaN")]
    public void ValidateRejectsNaN()
    {
        var (result, errors) = Validate(new ShadowState(double.NaN, false), 100.0);

        Assert.AreEqual(1, errors); // NaN was detected and logged
        // Both fallbacks (AlwaysInSun / AlwaysInShadow) carry an infinite estimate.
        Assert.IsFalse(double.IsNaN(result.NextTerminatorEstimate));
        Assert.IsTrue(double.IsPositiveInfinity(result.NextTerminatorEstimate));
    }

    [TestInfo("ShadowStateTests_ValidateRejectsPastTerminator")]
    public void ValidateRejectsPastTerminator()
    {
        var (result, errors) = Validate(new ShadowState(50.0, false), 100.0);

        Assert.AreEqual(1, errors); // past terminator was detected and logged
        Assert.IsTrue(double.IsPositiveInfinity(result.NextTerminatorEstimate));
    }

    [TestInfo("ShadowStateTests_ValidatePassesFutureTerminator")]
    public void ValidatePassesFutureTerminator()
    {
        var (result, errors) = Validate(new ShadowState(500.0, true), 100.0);

        Assert.AreEqual(0, errors); // a valid future terminator logs nothing
        Assert.IsTrue(result.InShadow);
        Assert.AreEqual(500.0, result.NextTerminatorEstimate, 0.0);
    }

    #endregion
}
