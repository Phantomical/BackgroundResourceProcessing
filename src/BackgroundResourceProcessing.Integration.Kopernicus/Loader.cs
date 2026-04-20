using UnityEngine;

namespace BackgroundResourceProcessing.Integration.Kopernicus;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    public void OnStart()
    {
        ShadowState.StarProvider = new KopernicusStarProvider();
        Destroy(this);
    }

    // Loader.OnStart calls Destroy(this) once it has wired the provider, so
    // there is no live MonoBehaviour for HotReloadKSP to recreate. Re-bind
    // the provider here so the new-assembly type takes over the static
    // ShadowState slot after a reload.
    static void OnHotLoad()
    {
        ShadowState.StarProvider = new KopernicusStarProvider();
    }
}
