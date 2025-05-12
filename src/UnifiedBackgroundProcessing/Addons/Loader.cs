using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace UnifiedBackgroundProcessing.Addons
{
    /// <summary>
    /// Static registration for UBP.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class BackgroundResourceProcessingLoader : MonoBehaviour
    {
        void Awake()
        {
            Registrar.RegisterAllBehaviours();
        }
    }
}
