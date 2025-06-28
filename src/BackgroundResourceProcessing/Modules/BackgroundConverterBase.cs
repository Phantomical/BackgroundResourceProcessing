using System.Collections.Generic;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// Interface mixin for part modules that want to run behaviours right
    /// after inventories are restored.
    /// </summary>
    ///
    /// <remarks>
    /// Since vessel inventory restore happens before the OnStart callback for
    /// a part, it cannot be a regular event as there would be no way for the
    /// part to register itself to receive said event.
    /// </remarks>
    public interface IBackgroundVesselRestoreHandler
    {
        /// <summary>
        /// Called after the vessel is loaded in just after the inventories
        /// have been updated by the background resource processor.
        /// </summary>
        ///
        /// <remarks>
        /// Note that this is called BEFORE the OnStart callback for part
        /// modules, so other part modules may not be fully initialized.
        /// </remarks>
        public void OnVesselRestore();
    }

    /// <summary>
    /// A resource converter that works in the background.
    /// </summary>
    ///
    /// <remarks>
    /// This is the core module class that every background processing module.
    /// Its role is to inspect the current part and summarize its behaviour
    /// into a <see cref="ConverterBehaviour"/> that can persist beyond when
    /// the current vessel is unloaded.
    /// </remarks>
    public abstract class BackgroundConverter : PartModule
    {
        /// <summary>
        /// The priority with which this converter will consume produced resources.
        /// </summary>
        ///
        /// <remarks>
        ///   This is used to determine which parts will continue to be
        ///   supplied with resources when there are not enough being produced
        ///   to satisfy all consumers/converters. Higher priorities will
        ///   consume resources first. The default is 0, and generally you can
        ///   leave the priority at that.
        /// </remarks>
        [KSPField]
        public int priority = 0;

        /// <summary>
        /// Get the <see cref="ConverterBehaviour"/>s that describe the
        /// resources consumed, produced, and required by this part.
        /// </summary>
        ///
        /// <remarks>
        /// Each <see cref="ConverterBehaviour"/> returned from this method
        /// is considered to logically be its own converter. Generally, you
        /// will only want to return a single converter, but this allows you
        /// to return multiple at once.
        /// </remarks>
        protected abstract List<ConverterBehaviour> GetConverterBehaviours();

        /// <summary>
        /// Get the set of <see cref="IBackgroundPartResource"/> instances that
        /// this converter can access.
        /// </summary>
        ///
        /// <remarks>
        /// This can return <c>null</c> and it will be treated the same as
        /// returning an empty resource set.
        /// </remarks>
        public virtual BackgroundResourceSet GetLinkedBackgroundResources()
        {
            return null;
        }

        /// <summary>
        /// Get a unique label that can be used when printing out error/log
        /// messages or looking at export dumps.
        /// </summary>
        /// <returns></returns>
        public virtual string GetLabel()
        {
            return GetType().Name;
        }

        /// <summary>
        /// Get the <see cref="ConverterBehaviour"/> that describes the
        /// resources consumed, produced, and required by this part.
        /// </summary>
        public List<ConverterBehaviour> GetBehaviour()
        {
            var behaviours = GetConverterBehaviours();
            if (behaviours == null)
                return behaviours;

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;
                behaviour.Priority = priority;
            }

            return behaviours;
        }
    }
}
