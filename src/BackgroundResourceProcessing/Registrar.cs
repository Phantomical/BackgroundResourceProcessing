using System.Diagnostics;
using System.Reflection;
using BackgroundResourceProcessing.Addons;

namespace BackgroundResourceProcessing
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
            if (changepoint == double.PositiveInfinity)
                return;

            if (Timer == null)
            {
                LogUtil.Error(
                    "RegisterChangepointCallback called but there is no active instance of BackgroundResourceProcessingTimer"
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
                //     "UnregisterChangepointCallbacks called but there is no active instance of BackgroundResourceProcessingTimer"
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
