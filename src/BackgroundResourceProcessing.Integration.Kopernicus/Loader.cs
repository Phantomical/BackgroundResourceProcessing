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
}
