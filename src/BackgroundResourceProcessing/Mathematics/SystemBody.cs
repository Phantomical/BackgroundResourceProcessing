using System.Diagnostics;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Maths;

namespace BackgroundResourceProcessing.Mathematics;

[DebuggerDisplay("{Name}")]
internal struct SystemBody
{
    public bool HasOrbit;
    public Orbit Orbit;
    public Vector3d Position;
    public int Index;

    public double Radius;

    public Vector3d RotationAxis;
    public double AngularVelocity;
    public bool Rotates;
    public bool TidallyLocked;
    public double RotationPeriod;
    public double SolarDayLength;

    public readonly string Name
    {
        get
        {
            var bodies = FlightGlobals.Bodies;
            if (Index < 0 || Index >= bodies.Count)
                return null;

            return bodies[Index].bodyName;
        }
    }

    public SystemBody(CelestialBody body, int? index = null)
    {
        if (body.orbit is not null)
        {
            HasOrbit = true;
            Orbit = new(body.orbit);
        }
        else
        {
            HasOrbit = false;
            Position = body.position;
        }

        this.Index = index ?? FlightGlobals.GetBodyIndex(body);
        this.Radius = body.Radius;

        // body.RotationAxis is expressed in Unity world space, but every other
        // vector recorded here (orbital positions, the landed surface normal)
        // lives in KSP's internal (Z-up) orbit frame. Convert the rotation axis
        // into that frame so LandedShadow rotates the surface normal in the
        // correct direction (issue #26).
        this.RotationAxis = Planetarium.Zup.LocalToWorld(body.RotationAxis.xzy);
        this.AngularVelocity = body.angularV;
        this.Rotates = body.rotates;
        this.TidallyLocked = body.tidallyLocked;
        this.RotationPeriod = body.rotationPeriod;
        this.SolarDayLength = body.solarDayLength;
    }

    public readonly Vector3d GetRelativePositionAtUT(double UT)
    {
        if (HasOrbit)
            return Orbit.GetRelativePositionAtUT(UT);
        else
            return Position;
    }

    public readonly DualVector3 GetRelativePositionAtUT(Dual UT)
    {
        if (HasOrbit)
            return Orbit.GetRelativePositionAtUT(UT);
        else
            return new(Position);
    }

    public readonly Dual2Vector3 GetRelativePositionAtUT(Dual2 UT)
    {
        if (HasOrbit)
            return Orbit.GetRelativePositionAtUT(UT);
        else
            return new(Position);
    }
}
