using UnityEngine;

namespace BackgroundResourceProcessing.Addons
{
    /// <summary>
    /// Static registration for UBP.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class BackgroundResourceProcessingLoader : MonoBehaviour
    {
        void Awake()
        {
            // GameEventsBase.debugEvents = true;
        }

        void Start()
        {
            GameEvents.OnPartLoaderLoaded.Add(OnPartLoaderLoaded);
        }

        void OnPartLoaderLoaded()
        {
            BehaviourRegistry.RegisterAllBehaviours();
            GameEvents.OnPartLoaderLoaded.Remove(OnPartLoaderLoaded);
        }
    }
}
