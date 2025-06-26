using UnityEngine;

namespace BackgroundResourceProcessing.Addons
{
    /// <summary>
    /// Static registration for UBP.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class BackgroundResourceProcessingLoader : MonoBehaviour
    {
        public static bool IsPartLoadingComplete = false;

        void Start()
        {
            GameEvents.OnPartLoaderLoaded.Add(OnPartLoaderLoaded);
        }

        void OnPartLoaderLoaded()
        {
            BehaviourRegistry.RegisterAllBehaviours();
            GameEvents.OnPartLoaderLoaded.Remove(OnPartLoaderLoaded);
            IsPartLoadingComplete = true;
        }
    }
}
