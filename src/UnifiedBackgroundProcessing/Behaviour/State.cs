using KSP;

namespace UnifiedBackgroundProcessing.Behaviour
{
    public class VesselState
    {
        /// <summary>
        /// The vessel that this module belongs to.
        /// </summary>
        ///
        /// <remarks>
        /// Expect this vessel to be unloaded. The only times that it will not
        /// be are when switching to/away from a vessel.
        /// </remarks>
        public Vessel Vessel;

        /// <summary>
        /// The time at which we are getting the rate.
        /// </summary>
        ///
        /// <remarks>
        /// Note that this may not correspond with the current game time, though
        /// it should usually be close enough that using other properties
        /// associated with the vessel (e.g. orbit parameters) should be fine.
        /// </remarks>
        public double CurrentTime;
    }
}
