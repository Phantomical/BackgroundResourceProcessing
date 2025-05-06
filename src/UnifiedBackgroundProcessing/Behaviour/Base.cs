using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnifiedBackgroundProcessing.Collections;

namespace UnifiedBackgroundProcessing.Behaviour
{
    /// <summary>
    /// A base class for all behaviours.
    /// </summary>
    ///
    /// <remarks>
    /// This class contains some common infrastructure that is shared between
    /// the different behaviour classes. This is mostly related to serialization
    /// and shared debugging interfaces.
    /// </remark>
    public abstract class BaseBehaviour
    {
        /// <summary>
        /// The module that constructed this behaviour.
        /// </summary>
        ///
        /// <remarks>
        /// This is mainly used for debugging and log messages. It will be
        /// automatically set when returned from a component module.
        /// </remarks>
        public string SourceModule => sourceModule;
        internal string sourceModule = null;

        /// <summary>
        /// Get the time at which the rates for this behaviour will change next.
        /// </summary>
        ///
        /// <param name="state">State information about the vessel.</param>
        /// <returns>The duration until the next changepoint, in seconds.</returns>
        ///
        /// <remarks>
        ///   <para>
        ///     This can be used to simulate behaviours that have non-linear
        ///     behaviour by approximating them using a piecewise linear rate
        ///     function. However, adding more changepoints does have a cost so
        ///     it is best to limit updates to at most once per day per vessel.
        ///   </para>
        ///
        ///   <para>
        ///     In cases where there are no future changepoints, you can return
        ///     <c>double.PositiveInfinity</c>. In this case, the behaviour rates
        ///     will not be loaded again due to changepoint timeout. Note that
        ///     refreshes will still happen when the vessel is switched to, or
        ///     when it switches from one SOI to another.
        ///   </para>
        /// </remarks>
        public virtual double GetNextChangepoint(VesselState state)
        {
            return double.PositiveInfinity;
        }

        public virtual void Load(ConfigNode node)
        {
            node.TryGetValue("sourceModule", ref sourceModule);
        }

        public virtual void Save(ConfigNode node)
        {
            node.AddValue("name", GetBehaviourName(GetType()));

            if (sourceModule != null)
                node.AddValue("sourceModule", sourceModule);
        }

        /// <summary>
        /// Load a behaviour directly from a config node.
        /// </summary>
        /// <param name="node">The config node.</param>
        /// <returns>
        ///   The behaviour instance, or <c>null</c> if there was no registered
        ///   behaviour with the name stored in the <see cref="ConfigNode"/>.
        /// </returns>
        ///
        /// <remarks>
        ///   This relies on the behaviour class having been registered
        ///   beforehand via <see cref="UnifiedBackgroundProcessing.RegisterAllBehaviours"/>.
        /// </remarks>
        public static BaseBehaviour LoadStatic(ConfigNode node)
        {
            string name = "";
            if (!node.TryGetValue("name", ref name))
            {
                LogUtil.Error("ConfigNode for BaseBehaviour did not have a 'name' property");
                return null;
            }

            var type = BehaviourRegistry.Registry.GetRegisteredType(name);
            if (type == null)
            {
                LogUtil.Error(
                    $"Attempted to load a Behaviour ConfigNode with name '{name}' but no behaviour has been registered with that name"
                );
                return null;
            }

            var inst = Activator.CreateInstance(type);
            if (inst is not BaseBehaviour behaviour)
            {
                LogUtil.Error(
                    $"Registered type for Behaviour with name '{name}' did not derive from BaseBehaviour"
                );
                return null;
            }

            behaviour.Load(node);
            return behaviour;
        }

        internal static void RegisterAll(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<Behaviour>();
                if (attribute == null)
                    continue;

                var baseType = typeof(BaseBehaviour);
                if (baseType.IsAssignableFrom(type))
                {
                    throw new InvalidBehaviourException(
                        $"Behaviour type '{type.Name}' does not inherit from '{baseType.FullName}'"
                    );
                }

                // We need the type to be default-constructible in order for us to
                // create an instance to deserialize a ConfigNode into.
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    throw new InvalidBehaviourException(
                        $"Behaviour type '{type.Name}' does not have a default constructor"
                    );
                }
            }
        }

        private static string GetBehaviourName(Type type)
        {
            var attribute =
                type.GetCustomAttribute<Behaviour>()
                ?? throw new InvalidBehaviourException(
                    $"Behaviour type '{type.Name}' does not have a [Behaviour] attribute"
                );
            if (BehaviourRegistry.Registry.GetRegisteredPrimaryType(attribute.Name) == null)
            {
                throw new InvalidBehaviourException(
                    $"Behaviour type '{type.Name}' has not been registered. Make sure to call UnifiedBackgroundProcessing.RegisterAllBehaviours during startup."
                );
            }

            return attribute.Name;
        }
    }

    public class InvalidBehaviourException(string message) : Exception(message) { }

    public class BehaviourLoadException(string message) : Exception(message) { }

    internal class BehaviourRegistry
    {
        internal static BehaviourRegistry Registry = new BehaviourRegistry();

        ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim();
        Dictionary<string, Type> primaries = [];
        Dictionary<string, Type> alternates = [];

        /// <summary>
        /// Register a new behaviour.
        /// </summary>
        /// <param name="attr">The <see cref="Behaviour"/> attribute on <c>type</c></param>
        /// <param name="type">The concrete type of this behaviour.</param>
        /// <returns><c>true</c> if the type was successfully registered.</returns>
        public void Register(Behaviour attr, Type type)
        {
            try
            {
                rwlock.EnterWriteLock();

                if (!primaries.TryAdd(attr.Name, type))
                {
                    var other = primaries[attr.Name];

                    throw new InvalidBehaviourException(
                        $"Behaviour name conflict: types '{type.FullName}' and '{other.FullName}' both want to register as behaviour name '{attr.Name}'"
                    );
                }

                foreach (var alternate in attr.Alternates)
                {
                    if (!alternates.TryAdd(alternate, type))
                    {
                        var other = alternates[alternate];
                        LogUtil.Warn(
                            $"Behaviour alternate name conflict: types '{type.FullName}' and '{other.FullName}' both want to register with alternate name '{alternate}'. Conflict was resolved in favour of '{other.FullName}'."
                        );
                    }
                }
            }
            finally
            {
                rwlock.ExitWriteLock();
            }
        }

        public Type GetRegisteredPrimaryType(string name)
        {
            try
            {
                rwlock.EnterReadLock();

                return primaries.GetValueOr(name, null);
            }
            finally
            {
                rwlock.ExitReadLock();
            }
        }

        public Type GetRegisteredType(string name)
        {
            try
            {
                rwlock.EnterReadLock();

                return primaries.GetValueOr(name, null) ?? alternates.GetValueOr(name, null);
            }
            finally
            {
                rwlock.ExitReadLock();
            }
        }
    }
}
