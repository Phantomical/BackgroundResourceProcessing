using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Maths;

namespace BackgroundResourceProcessing.Mathematics;

internal readonly struct SolarSystem(MemorySpan<SystemBody> bodies)
{
    public readonly MemorySpan<SystemBody> bodies = bodies;

    public int Count => bodies.Length;
    public ref SystemBody this[int index] => ref bodies[index];

    public SolarSystem(RawList<SystemBody> bodies)
        : this(bodies.Span) { }

    public Vector3d GetPositionAtUT(in Orbit orbit, double UT)
    {
        Vector3d pos = orbit.GetRelativePositionAtUT(UT);
        int? index = orbit.ParentBodyIndex;

        while (index is not null)
        {
            ref var parent = ref this[(int)index];
            pos += parent.GetRelativePositionAtUT(UT);
            if (parent.HasOrbit)
                index = parent.Orbit.ParentBodyIndex;
            else
                index = null;
        }

        return pos;
    }

    public DualVector3 GetPositionAtUT(in Orbit orbit, Dual UT)
    {
        DualVector3 pos = orbit.GetRelativePositionAtUT(UT);
        int? index = orbit.ParentBodyIndex;

        while (index is not null)
        {
            ref var parent = ref this[(int)index];
            pos += parent.GetRelativePositionAtUT(UT);
            if (parent.HasOrbit)
                index = parent.Orbit.ParentBodyIndex;
            else
                index = null;
        }

        return pos;
    }

    public Dual2Vector3 GetPositionAtUT(in Orbit orbit, Dual2 UT)
    {
        Dual2Vector3 pos = orbit.GetRelativePositionAtUT(UT);
        int? index = orbit.ParentBodyIndex;

        while (index is not null)
        {
            ref var parent = ref this[(int)index];
            pos += parent.GetRelativePositionAtUT(UT);
            if (parent.HasOrbit)
                index = parent.Orbit.ParentBodyIndex;
            else
                index = null;
        }

        return pos;
    }

    public Vector3d GetPositionAtUT(in SystemBody body, double UT)
    {
        if (body.HasOrbit)
            return GetPositionAtUT(in body.Orbit, UT);
        return body.Position;
    }

    public DualVector3 GetPositionAtUT(in SystemBody body, Dual UT)
    {
        if (body.HasOrbit)
            return GetPositionAtUT(in body.Orbit, UT);
        return new(body.Position);
    }

    public Dual2Vector3 GetPositionAtUT(in SystemBody body, Dual2 UT)
    {
        if (body.HasOrbit)
            return GetPositionAtUT(in body.Orbit, UT);
        return new(body.Position);
    }

    public static SystemBody[] Record()
    {
        var celestialBodies = FlightGlobals.Bodies;
        var bodies = new SystemBody[celestialBodies.Count];

        for (int i = 0; i < bodies.Length; ++i)
            bodies[i] = new SystemBody(celestialBodies[i]);

        return bodies;
    }

    public static int? GetBodyIndex(CelestialBody body)
    {
        if (body is null)
            return null;

        int index = 0;

        foreach (var cb in FlightGlobals.Bodies)
        {
            if (ReferenceEquals(cb, body))
                return index;
            index += 1;
        }

        return null;
    }
}
