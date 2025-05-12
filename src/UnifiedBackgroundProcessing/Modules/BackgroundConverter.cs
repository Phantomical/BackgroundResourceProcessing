namespace UnifiedBackgroundProcessing.Modules
{
    /// <summary>
    /// A resource converter that works in the background.
    /// </summary>
    public interface IBackgroundConverter
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
        public int Priority { get; }

        /// <summary>
        /// Get the <see cref="ConverterBehaviour"/> that describes the
        /// resources consumed, produced, and required by this part.
        /// </summary>
        public ConverterBehaviour GetBehaviour();
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
    public abstract class BackgroundConverter : PartModule, IBackgroundConverter
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
        public int Priority { get; set; }

        /// <summary>
        /// Get the <see cref="ConverterBehaviour"/> that describes the
        /// resources consumed, produced, and required by this part.
        /// </summary>
        public abstract ConverterBehaviour GetBehaviour();

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            var priority = 0;
            if (node.TryGetValue("priority", ref priority))
                Priority = priority;
        }
    }
}
