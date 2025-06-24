using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BackgroundResourceProcessing.Collections;

namespace BackgroundResourceProcessing
{
    /// <summary>
    /// An internal class for keeping track of created behaviours.
    /// </summary>
    ///
    /// <remarks>
    /// We need to deserialize an abstract class hierarchy. That requires
    /// some way to construct a concrete class instance given a type name.
    /// This registry provides that way.
    /// </remarks>
    internal class BehaviourRegistry
    {
        internal static BehaviourRegistry Registry = new();

        readonly ReaderWriterLockSlim rwlock = new();
        readonly Dictionary<string, Type> primaries = [];
        readonly Dictionary<string, Type> alternates = [];

        private void Register(Behaviour attr, Type type)
        {
            try
            {
                rwlock.EnterWriteLock();

                if (!DictionaryExtensions.TryAddExt(primaries, attr.Name, type))
                {
                    var other = primaries[attr.Name];

                    throw new InvalidBehaviourException(
                        $"Behaviour name conflict: types '{type.FullName}' and '{other.FullName}' both want to register as behaviour name '{attr.Name}'"
                    );
                }

                foreach (var alternate in attr.Alternates)
                {
                    if (!DictionaryExtensions.TryAddExt(alternates, alternate, type))
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

        internal Type GetRegisteredPrimaryType(string name)
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

        internal Type GetRegisteredType(string name)
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

        /// <summary>
        /// Register all types annotated with <c>[Behaviour]</c> within the
        /// provided assembly.
        /// </summary>
        static void RegisterAll(IEnumerable<Type> types)
        {
            foreach (var type in types)
            {
                var attribute = type.GetCustomAttribute<Behaviour>() ?? new Behaviour(type.Name);

                var baseType = typeof(ConverterBehaviour);
                if (!type.IsSubclassOf(baseType))
                {
                    LogUtil.Error(
                        $"Behaviour type '{type.Name}' does not inherit from '{baseType.FullName}' and will not be registered"
                    );
                    continue;
                }

                // We need the type to be default-constructible in order for us to
                // create an instance to deserialize a ConfigNode into.
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    LogUtil.Error(
                        $"Behaviour type '{type.Name}' does not have a default constructor and will not be registered"
                    );
                    continue;
                }

                LogUtil.Log($"Registering behaviour {type.FullName} as {attribute.Name}");
                Registry.Register(attribute, type);
            }
        }

        internal static string GetBehaviourName(Type type)
        {
            var attribute = type.GetCustomAttribute<Behaviour>() ?? new Behaviour(type);
            if (Registry.GetRegisteredPrimaryType(attribute.Name) == null)
            {
                throw new InvalidBehaviourException(
                    $"Behaviour type '{type.Name}' has not been registered. Make sure to call BackgroundResourceProcessing.RegisterAllBehaviours during startup."
                );
            }

            return attribute.Name;
        }

        /// <summary>
        /// Register all behaviour classes annotated with <c>[<see cref="Behaviour"/>]</c>
        /// within the current assembly.
        /// </summary>
        internal static void RegisterAllBehaviours()
        {
            var types = AssemblyLoader
                .GetSubclassesOfParentClass(typeof(ConverterBehaviour))
                .Where(type => !type.IsAbstract);
            RegisterAll(types);
        }

        /// <summary>
        /// Register all behaviour classes annotated with <c>[<see cref="UnityEngine.Behaviour"/>]</c>
        /// within the provided assembly.
        /// </summary>
        ///
        /// <remarks>
        /// This is only exposed for tests, which can't call into any UnityEngine
        /// methods.
        /// </remarks>
        internal static void RegisterAllBehaviours(Assembly assembly)
        {
            var types = assembly
                .GetTypes()
                .Where(type => type.IsSubclassOf(typeof(ConverterBehaviour)))
                .Where(type => !type.IsAbstract);
            RegisterAll(types);
        }
    }
}
