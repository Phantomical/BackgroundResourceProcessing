using System;
using BackgroundResourceProcessing.Maths;

namespace BackgroundResourceProcessing.Mathematics;

internal static class OrbitShadow
{
    /// <summary>
    /// Computes the universal time (UT) at which the vessel whose orbit
    /// is defined by <paramref name="vessel"/> will next enter into the
    /// planet's shadow casted by the celestial body defined by
    /// <paramref name="sunIndex"/>.
    /// </summary>
    /// <param name="celestialBodies"></param>
    /// <param name="vessel"></param>
    /// <param name="sunIndex"></param>
    /// <param name="UT"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    ///
    /// <remarks>
    /// This method assumes that <paramref name="vessel"/> is not
    /// currently within the shadow of the planet it is orbiting.
    /// </remarks>
    public static double ComputeOrbitTerminatorEntranceUT(
        Orbit[] celestialBodies,
        Orbit vessel,
        int sunIndex,
        double UT
    )
    {
        Orbit sun = celestialBodies[sunIndex];

        throw new NotImplementedException();
    }

    public static Vector3d GetPositionAtUT(Orbit[] celestialBodies, Orbit orbit, double UT)
    {
        var position = orbit.GetRelativePositionAtUT(UT);

        int? parent = orbit.ParentBodyIndex;
        while (parent != null)
        {
            ref var parentBody = ref celestialBodies[parent.Value];
            position += parentBody.GetRelativePositionAtUT(UT);
            parent = parentBody.ParentBodyIndex;
        }

        return position;
    }

    public static DualVector3 GetPositionAtUT(Orbit[] celestialBodies, Orbit orbit, Dual UT)
    {
        var position = orbit.GetRelativePositionAtUT(UT);

        int? parent = orbit.ParentBodyIndex;
        while (parent != null)
        {
            ref var parentBody = ref celestialBodies[parent.Value];
            position += parentBody.GetRelativePositionAtUT(UT);
            parent = parentBody.ParentBodyIndex;
        }

        return position;
    }

    public static Dual2Vector3 GetPositionAtUT(Orbit[] celestialBodies, Orbit orbit, Dual2 UT)
    {
        var position = orbit.GetRelativePositionAtUT(UT);

        int? parent = orbit.ParentBodyIndex;
        while (parent != null)
        {
            ref var parentBody = ref celestialBodies[parent.Value];
            position += parentBody.GetRelativePositionAtUT(UT);
            parent = parentBody.ParentBodyIndex;
        }

        return position;
    }
}
