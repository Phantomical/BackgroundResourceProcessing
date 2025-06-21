using System.Linq;
using System.Reflection;

namespace BackgroundResourceProcessing
{
    internal static class Registrar
    {
        /// <summary>
        /// Register all behaviour classes annotated with <c>[<see cref="Behaviour"/>]</c>
        /// within the current assembly.
        /// </summary>
        internal static void RegisterAllBehaviours()
        {
            var types = AssemblyLoader
                .GetSubclassesOfParentClass(typeof(ConverterBehaviour))
                .Where(type => !type.IsAbstract);
            BehaviourRegistry.RegisterAll(types);
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
            BehaviourRegistry.RegisterAll(types);
        }
    }
}
