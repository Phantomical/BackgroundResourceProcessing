using System.Diagnostics;
using System.Reflection;
using UnifiedBackgroundProcessing.Addons;
using UnifiedBackgroundProcessing.VesselModules;
using UnityEngine;

namespace UnifiedBackgroundProcessing
{
    public static class Registrar
    {
        internal static Timer Timer;

        #region Timers
        internal static void RegisterChangepointCallback(
            BackgroundResourceProcessor module,
            double changepoint
        )
        {
            if (Timer == null)
            {
                LogUtil.Error(
                    "RegisterChangepointCallback called but there is no active instance of UnifiedBackgroundProcessingTimer"
                );
                return;
            }

            Timer.RegisterChangepointCallback(module, changepoint);
        }

        internal static void UnregisterChangepointCallbacks(BackgroundResourceProcessor module)
        {
            if (Timer == null)
            {
                // LogUtil.Error(
                //     "UnregisterChangepointCallbacks called but there is no active instance of UnifiedBackgroundProcessingTimer"
                // );
                return;
            }

            Timer.UnregisterChangepointCallbacks(module);
        }
        #endregion

        #region Behaviour Registration
        /// <summary>
        /// Register all behaviour classes annotated with <c>[<see cref="UnityEngine.Behaviour"/>]</c>
        /// within the current assembly.
        /// </summary>
        public static void RegisterAllBehaviours()
        {
            Assembly assembly = new StackTrace().GetFrame(1).GetMethod().ReflectedType.Assembly;
            RegisterAllBehaviours(assembly);
        }

        /// <summary>
        /// Register all behaviour classes annotated with <c>[<see cref="UnityEngine.Behaviour"/>]</c>
        /// within the provided assembly.
        /// </summary>
        public static void RegisterAllBehaviours(Assembly assembly)
        {
            BehaviourRegistry.RegisterAll(assembly);
        }
        #endregion
    }
}
