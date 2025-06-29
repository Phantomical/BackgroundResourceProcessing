using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Inventory;
using BackgroundResourceProcessing.Utils;
using KSP.Localization;
using UnityEngine;

namespace BackgroundResourceProcessing.Addons
{
    /// <summary>
    /// Static registration for UBP.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class BackgroundResourceProcessingLoader : MonoBehaviour
    {
        void Start()
        {
            // The actual details of the loading get done in a loading system.
            //
            // This way we can ensure it happens after module manager and
            // anything else that happens during loading.
            LoadingScreen.Instance.loaders.Add(new BackgroundLoadingSystem());
        }

        private class BackgroundLoadingSystem() : LoadingSystem
        {
            private string title;

            public override bool IsReady()
            {
                return true;
            }

            public override string ProgressTitle()
            {
                title ??= Localizer.GetStringByTag("#LOC_BRP_LoadingScreenText");
                return title;
            }

            public override void StartLoad()
            {
                TypeRegistry.RegisterAll();
            }
        }
    }
}
