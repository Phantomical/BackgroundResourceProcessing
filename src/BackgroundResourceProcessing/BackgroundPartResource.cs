using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing
{
    /// <summary>
    /// A fake <see cref="PartResource"/> that is only present for background
    /// processing. See the documentation on <see cref="IBackgroundPartResource"/>
    /// for details on how this is meant to be used.
    /// </summary>
    public class FakePartResource
    {
        /// <summary>
        /// The name of the resource that is being stored within this fake
        /// inventory.
        /// </summary>
        public string resourceName;

        /// <summary>
        /// The amount of resource that is stored in this inventory.
        /// </summary>
        public double amount = 0.0;

        /// <summary>
        /// The maximum amount of resource that can be stored in this inventory.
        /// </summary>
        ///
        /// <remarks>
        /// This is permitted to be infinite, but negative or NaN values will
        /// result in this inventory being ignored.
        /// </remarks>
        public double maxAmount = 0.0;
    }

    /// <summary>
    /// A "fake" inventory that only exists for interacting with background
    /// processing.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    ///   The main purpose of this interface is to allow for background
    ///   processing to interact with resources that aren't KSP resources.
    ///   Stock has a bunch of these (think science in science labs, or
    ///   asteroid mass) and mods end up having even more.
    /// </para>
    ///
    /// <para>
    ///   Note that implementors must be derived from <see cref="PartModule"/>.
    ///   This is used to find the proper module again when the vessel is loaded
    ///   so any <c>IBackgroundPartResource</c>s that are not derived from
    ///   PartModule will be ignored.
    /// </para>
    ///
    /// <para>
    ///   Note that this <c>IBackgroundPartResource</c> must be manually linked
    ///   with a converter in order to do anything. ResourceProcessor does not
    ///   scan the vessel to build up a list of what is linked up according to
    ///   flow rules so it is up to the individual parts themselves to do so if
    ///   needed. If you find yourself needing something more complicated then
    ///   you will likely want to use an actual resource instead.
    /// </para>
    /// </remarks>
    public interface IBackgroundPartResource
    {
        /// <summary>
        /// Get a <see cref="FakePartResource"/> that describes the contents
        /// and connected parts for this fake inventory.
        /// </summary>
        public FakePartResource GetResource();

        /// <summary>
        /// Update the inventory with the new resource amount.
        /// </summary>
        /// <param name="amount"></param>
        public void UpdateStoredAmount(double amount);
    }

    public class BackgroundResourceSet()
    {
        public List<IBackgroundPartResource> push = null;
        public List<IBackgroundPartResource> pull = null;

        public void AddPushResource(IBackgroundPartResource resource)
        {
            ValidateResource(resource);

            if (push == null)
                push = [];
            push.Add(resource);
        }

        public void AddPullResource(IBackgroundPartResource resource)
        {
            ValidateResource(resource);

            if (pull == null)
                pull = [];
            pull.Add(resource);
        }

        private static void ValidateResource(IBackgroundPartResource resource)
        {
            if (resource == null)
                throw new ArgumentNullException("resource");

            if (resource is not PartModule module)
                throw new ArgumentException(
                    $"IBackgroundPartResource implementer {resource.GetType().Name} is not an instance of PartModule"
                );

            if (module.part == null)
                throw new ArgumentException(
                    $"IBackgroundPartResource instance is not a PartModule attached to a part"
                );
        }
    }
}
