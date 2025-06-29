using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using UnityEngine;

namespace BackgroundResourceProcessing
{
    /// <summary>
    /// Manually load integration DLLs as part of startup.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal sealed class Bootstrap : MonoBehaviour
    {
        void Awake()
        {
            LogUtil.Log("Loading mod integrations");

            string pluginDir = GetPluginDirectory();
            var plugins = Directory.GetFiles(pluginDir, "*.dll.plugin");
            List<AssemblyLoader.LoadedAssembly> list = [];

            foreach (var plugin in plugins)
            {
                var name = Path.GetFileNameWithoutExtension(plugin);

                try
                {
                    var assembly = Assembly.LoadFile(plugin);
                    var loaded = new AssemblyLoader.LoadedAssembly(
                        assembly,
                        assembly.Location,
                        assembly.Location,
                        null
                    );

                    UpdateAssemblyLoaderTypeCache(loaded);

                    LogUtil.Log($"Loaded integration {assembly.FullName}");
                    list.Add(loaded);
                }
                catch (Exception e)
                {
#if DEBUG
                    LogUtil.Warn($"Failed to load integration {name}: {e}");

                    if (e is ReflectionTypeLoadException re)
                    {
                        var message = "Additional information:\n";
                        foreach (Exception inner in re.LoaderExceptions)
                            message += "\n" + inner.ToString();

                        LogUtil.Warn(message);
                    }
#else
                    LogUtil.Log($"Skipping integration {name}");
#endif
                }
            }

            // Now we need to start all the plugins in the addons
            foreach (var loaded in list)
            {
                AssemblyLoader.loadedAssemblies.Add(loaded);

                var assembly = loaded.assembly;
                foreach (var type in assembly.GetTypes())
                {
                    if (!type.IsSubclassOf(typeof(MonoBehaviour)))
                        continue;

                    KSPAddon attribute = type.GetCustomAttributes<KSPAddon>(inherit: true)
                        .FirstOrDefault();
                    if (attribute == null)
                        continue;

                    if (attribute.startup != KSPAddon.Startup.Instantly)
                        continue;

                    StartAddon(loaded, type, attribute);
                }
            }

            // And now we clean up after ourselves.
            Destroy(this);
        }

        /// <summary>
        /// KSP maintains a type cache for loaded assemblies that is used to
        /// answer things like <c>GetTypeByName</c>. We need to manually
        /// populate that here for the assemblies we are loading.
        /// </summary>
        /// <param name="loaded"></param>
        /// <returns></returns>
        private static void UpdateAssemblyLoaderTypeCache(AssemblyLoader.LoadedAssembly loaded)
        {
            var assembly = loaded.assembly;
            var loadedTypes = new AssemblyLoader.LoadedTypes();

            foreach (var type in assembly.GetTypes())
            {
                foreach (Type loadedType in AssemblyLoader.loadedTypes)
                {
                    if (type.IsSubclassOf(loadedType) || type == loadedType)
                        loadedTypes.Add(loadedType, type);
                }
            }

            foreach (var (key, items) in loadedTypes)
            {
                foreach (Type item in items)
                {
                    loaded.types.Add(key, item);
                    loaded.typesDictionary.Add(key, item);
                }
            }
        }

        private static readonly MethodInfo StartAddonMethod = typeof(AddonLoader).GetMethod(
            "StartAddon",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        private static void StartAddon(AssemblyLoader.LoadedAssembly asm, Type type, KSPAddon addon)
        {
            StartAddonMethod.Invoke(AddonLoader.Instance, [asm, type, addon, addon.startup]);
        }

        private static string GetPluginDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }
    }
}
