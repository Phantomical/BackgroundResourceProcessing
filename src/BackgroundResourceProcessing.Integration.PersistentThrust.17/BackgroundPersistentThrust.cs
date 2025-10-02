using System;
using BackgroundResourceProcessing.Utils;
using PersistentThrust;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.PersistentThrust;

[KSPScenario(
    ScenarioCreationOptions.AddToAllGames,
    [GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.FLIGHT]
)]
public sealed class BackgroundPersistentThrust : VesselModule
{
    [KSPField(isPersistant = true)]
    public VesselAutopilot.AutopilotMode AutopilotMode;

    [KSPField(isPersistant = true)]
    public double Thrust = 0.0;

    [KSPField(isPersistant = true)]
    public double DryMass = 0.0;

    [KSPField(isPersistant = true)]
    public double LastUpdate = 0.0;

    [KSPField(isPersistant = true)]
    public Guid TargetVesselId = Guid.Empty;

    [KSPField(isPersistant = true)]
    public string TargetBodyName = "";

    BackgroundResourceProcessor processor;
    Orbit target;

    void FixedUpdate()
    {
        if (vessel.loaded || Thrust == 0.0)
        {
            // Avoid running unnecessary fixed updates on vessels that are not
            // being modified by PersistentThrust
            enabled = false;
            return;
        }

        processor ??= Vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();

        // In case of processing being delayed we only update up to the next changepoint.
        // Later updates will catch up once processing is done.
        DoUpdate(Math.Min(Planetarium.GetUniversalTime(), processor.NextChangepoint));
    }

    private void DoUpdate(double time)
    {
        var dt = time - LastUpdate;
        if (dt <= 0.0)
            return;

        var thrustVector = GetThrustVector(AutopilotMode);
        if (thrustVector == Vector3d.zero)
        {
            enabled = false;

            foreach (var converter in processor.Converters)
            {
                if (converter.Behaviour is not PersistentEngineBehaviour behaviour)
                    continue;

                behaviour.Enabled = false;
                converter.NextChangepoint = time;
            }

            processor.MarkDirty();
            return;
        }
        thrustVector.Normalize();

        var orbit = vessel.orbit;

        var mass = processor.GetWetMass();
        mass.amount += DryMass;

        double deltaV;
        if (Math.Abs(mass.rate) < 1e-9)
        {
            deltaV = Thrust / mass.amount * dt;
        }
        else
        {
            double m1 = mass.amount + mass.rate * (LastUpdate - processor.LastChangepoint);
            double m2 = mass.amount + mass.rate * (time - processor.LastChangepoint);
            deltaV = Thrust / mass.rate * Math.Log(m2 / m1);
        }

        LastUpdate = time;

        if (!MathUtil.IsFinite(deltaV))
        {
            LogUtil.Error("[PersistentThrust] Computed deltaV was NaN or infinite");
            return;
        }

        orbit.Perturb(thrustVector * deltaV, time);
    }

    internal void OnVesselRecord(BackgroundResourceProcessor processor)
    {
        this.processor = processor;
        foreach (var converter in processor.Converters)
        {
            if (converter.Behaviour is PersistentEngineBehaviour)
                goto RECORD;
        }

        Thrust = 0.0;
        DryMass = 0.0;
        return;

        RECORD:
        AutopilotMode = Vessel.Autopilot.Mode;
        DryMass = Vessel.GetDryMass();
        LastUpdate = Planetarium.GetUniversalTime();

        TargetVesselId = Guid.Empty;
        TargetBodyName = "";

        var target = Vessel.targetObject?.GetOrbitDriver();
        this.target = target?.orbit;
        if (target?.vessel is not null)
            TargetVesselId = target.vessel.id;
        else if (target?.celestialBody is not null)
            TargetBodyName = target.celestialBody.bodyName;
        else
            this.target = null;
    }

    internal void OnStateUpdate(BackgroundResourceProcessor processor, ChangepointEvent evt)
    {
        this.processor = processor;

        if (Thrust <= 0.0)
            return;

        // We explicitly do a state update here (if needed) because we cannot
        // accurately determine delta-V across a changepoint.
        DoUpdate(evt.CurrentChangepoint);
    }

    internal void OnVesselChangepoint(BackgroundResourceProcessor processor)
    {
        Thrust = 0.0;
        foreach (var converter in processor.Converters)
        {
            if (converter.Behaviour is not PersistentEngineBehaviour behaviour)
                continue;

            Thrust += behaviour.Thrust * converter.Rate;
        }

        enabled = Thrust > 0.0;
    }

    public override void OnUnloadVessel()
    {
        if (Thrust > 0.0)
            enabled = true;
    }

    private Vector3d GetThrustVector(VesselAutopilot.AutopilotMode autopilotMode)
    {
        double UT = Planetarium.GetUniversalTime();
        var orbit = Vessel.GetOrbit();
        var orbitalVelocityAtUt = orbit.getOrbitalVelocityAtUT(UT);

        Vector3d thrustVector = Vector3d.zero;
        switch (autopilotMode)
        {
            case VesselAutopilot.AutopilotMode.Prograde:
                thrustVector = orbitalVelocityAtUt;
                break;
            case VesselAutopilot.AutopilotMode.Retrograde:
                thrustVector = -orbitalVelocityAtUt;
                break;
            case VesselAutopilot.AutopilotMode.Normal:
                thrustVector = Vector3.Cross(
                    orbitalVelocityAtUt,
                    orbit.getPositionAtUT(UT) - Vessel.mainBody.getPositionAtUT(UT)
                );
                break;
            case VesselAutopilot.AutopilotMode.Antinormal:
                thrustVector = -Vector3.Cross(
                    orbitalVelocityAtUt,
                    orbit.getPositionAtUT(UT) - Vessel.mainBody.getPositionAtUT(UT)
                );
                break;
            case VesselAutopilot.AutopilotMode.RadialIn:
                thrustVector = -Vector3.Cross(
                    orbitalVelocityAtUt,
                    Vector3.Cross(
                        orbitalVelocityAtUt,
                        Vessel.orbit.getPositionAtUT(UT) - orbit.referenceBody.position
                    )
                );
                break;
            case VesselAutopilot.AutopilotMode.RadialOut:
                thrustVector = Vector3.Cross(
                    orbitalVelocityAtUt,
                    Vector3.Cross(
                        orbitalVelocityAtUt,
                        Vessel.orbit.getPositionAtUT(UT) - orbit.referenceBody.position
                    )
                );
                break;
            case VesselAutopilot.AutopilotMode.Target:
            case VesselAutopilot.AutopilotMode.AntiTarget:
                var target = GetVesselTarget();
                thrustVector = target == null ? Vector3d.zero : GetThrustVector(target, UT);

                if (autopilotMode == VesselAutopilot.AutopilotMode.AntiTarget)
                    thrustVector = -thrustVector;
                break;
            // case VesselAutopilot.AutopilotMode.Maneuver:
            //    thrustVector = orbit.GetThrustVectorToManeuver(moduleSnapshot);
            //    break;
        }

        return thrustVector;
    }

    private Vector3d GetThrustVector(Orbit orbit, double UT) =>
        orbit.getPositionAtUT(UT) - vessel.orbit.getPositionAtUT(UT);

    private Orbit GetVesselTarget()
    {
        if (target != null)
            return target;

        if (TargetVesselId != Guid.Empty)
            target = FlightGlobals.FindVessel(TargetVesselId)?.orbit;
        else if (!string.IsNullOrEmpty(TargetBodyName))
            target = FlightGlobals.Bodies.Find(body => body.bodyName == TargetBodyName)?.orbit;

        return target;
    }
}
