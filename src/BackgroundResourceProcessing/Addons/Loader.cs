using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;
using KSP.Localization;
using UnityEngine;

namespace BackgroundResourceProcessing.Addons;

/// <summary>
/// Manually load integration DLLs as part of startup.
/// </summary>
[KSPAddon(KSPAddon.Startup.Instantly, true)]
internal sealed class BackgroundResourceProcessingLoader : MonoBehaviour
{
    // This defines the dependencies that need to be satisfied in order for
    // us to load a plugin dll.
    private static readonly Dictionary<string, List<AssemblyDependency>> Dependencies =
    [
        new(
            "BackgroundResourceProcessing.Integration.ClickThroughBlocker",
            [new KSPAssemblyDependency("ClickThroughBlocker", new(2, 0))]
        ),
        new(
            "BackgroundResourceProcessing.Integration.CryoTanks",
            [new DirectAssemblyDependency("SimpleBoiloff", new(0, 2))]
        ),
        new(
            "BackgroundResourceProcessing.Integration.EL",
            [new DirectAssemblyDependency("Launchpad", new(6, 99))]
        ),
        new(
            "BackgroundResourceProcessing.Integration.Kopernicus",
            [new KSPAssemblyDependency("Kopernicus", new(1, 0))]
        ),
        new(
            "BackgroundResourceProcessing.Integration.SystemHeat",
            [new DirectAssemblyDependency("SystemHeat", new(0, 1))]
        ),
        new(
            "BackgroundResourceProcessing.Integration.USILifeSupport",
            [new DirectAssemblyDependency("USILifeSupport", new(1, 0))]
        ),
    ];

#if !DIRECT_PLUGIN_LOAD
    void Awake()
    {
        string pluginDir = GetPluginDirectory();
        var plugins = Directory.GetFiles(pluginDir, "*.dll.plugin");
        List<AssemblyLoader.LoadedAssembly> list = [];

        foreach (var plugin in plugins)
        {
            var name = GetPluginNameWithoutExtension(plugin);

            try
            {
                if (!ShouldLoadPlugin(name))
                {
                    LogUtil.Log($"Skipping assembly {name} as dependencies are not satisfied");
                    continue;
                }

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
                AssemblyLoader.loadedAssemblies.Add(loaded);
            }
            catch (ReflectionTypeLoadException e)
            {
                LogUtil.Warn($"Failed to load integration {name}: {e}");
                var message = "Additional information:";
                foreach (Exception inner in e.LoaderExceptions)
                    message += $"\n{inner}";
                LogUtil.Warn(message);
            }
            catch (Exception e)
            {
                LogUtil.Warn($"Failed to load integration {name}: {e}");
            }
        }

        // KSP has already enumerated vessel modules so we need to add any
        // new ones ourselves.
        var numVesselModules = 0;
        foreach (var loaded in list)
            numVesselModules += RegisterAssemblyVesselModules(loaded);

        if (numVesselModules != 0)
            LogUtil.Log($"VesselModules: Found {numVesselModules} additional vessel modules");

        // Now we need to kick off all KSPAddon instances that were supposed
        // to be starting right now.
        foreach (var loaded in list)
            StartAssemblyAddons(loaded);
    }
#endif

    void Start()
    {
        // We don't want to set up the type registries until the game
        // database has been fully patched by module manager.
        //
        // We do this by sticking it as an extra loading screen step
        // that happens at the very end.
        LoadingScreen.Instance.loaders.Add(gameObject.AddComponent<RegistryLoadingSystem>());

#if TRACING
        var path = typeof(BackgroundResourceProcessingLoader).Assembly.Location;
        path = Path.GetDirectoryName(path);
        path = Path.Combine(path, "../trace.json");

        Tracing.Trace.Start(path);
#endif

        // And now we clean up after ourselves.
        Destroy(this);
    }

    /// <summary>
    /// KSP maintains a type cache for loaded assemblies that is used to
    /// answer things like <c>GetTypeByName</c>. We need to manually
    /// populate that here for the assemblies we are loading.
    /// </summary>
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

    private static bool ShouldLoadPlugin(string name)
    {
        if (!Dependencies.TryGetValue(name, out var deps))
            return true;

        foreach (var dep in deps)
        {
            if (!dep.IsSatisfied())
                return false;
        }

        return true;
    }

    private static string GetPluginNameWithoutExtension(string path)
    {
        path = Path.GetFileName(path);
        if (path.EndsWith(".dll.plugin"))
            path = path.Substring(0, path.Length - ".dll.plugin".Length);
        return path;
    }

    /// <summary>
    /// KSP is already loading addons and adding new assemblies to the list
    /// now won't cause it to pick them up. This means we need to do it ourselves.
    /// </summary>
    private static void StartAssemblyAddons(AssemblyLoader.LoadedAssembly loaded)
    {
        var assembly = loaded.assembly;
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsSubclassOf(typeof(MonoBehaviour)))
                continue;

            KSPAddon attribute = type.GetCustomAttributes<KSPAddon>(inherit: true).FirstOrDefault();
            if (attribute == null)
                continue;

            if (attribute.startup != KSPAddon.Startup.Instantly)
                continue;

            StartAddon(loaded, type, attribute);
        }
    }

    /// <summary>
    /// Vessel module handling happens before addons running so we need to
    /// duplicate our own copy of it here.
    /// </summary>
    /// <returns>The number of vessel modules that have been discovered</returns>
    private static int RegisterAssemblyVesselModules(AssemblyLoader.LoadedAssembly loaded)
    {
        var assembly = loaded.assembly;
        var types = assembly.GetTypes();
        var count = 0;

        foreach (var type in types)
        {
            if (!type.IsSubclassOf(typeof(VesselModule)))
                continue;
            if (type == typeof(VesselModule))
                continue;

            try
            {
                var wrapper = new VesselModuleManager.VesselModuleWrapper(type);
                var gameObject = new GameObject("Temp");
                var module = gameObject.AddComponent(type) as VesselModule;
                if (module != null)
                {
                    wrapper.order = module.GetOrder();
                    Debug.Log(
                        $"VesselModules: Found VesselModule of type {type.Name} with order {wrapper.order}"
                    );
                    DestroyImmediate(module);
                }

                DestroyImmediate(gameObject);
                VesselModuleManager.Modules.Add(wrapper);
                count += 1;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"VesselModules: Error getting order of VesselModule of type {type.Name} so it was not added. Exception: {e}"
                );
            }
        }

        return count;
    }

    private static string StripPrefix(string s, string prefix)
    {
        if (s.StartsWith(prefix))
            return s.Substring(prefix.Length);
        return s;
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

    private static bool IsVersionCompatible(Version existing, Version required)
    {
        if (existing.Major != required.Major)
            return false;
        if (existing.Minor < required.Minor)
            return false;

        return true;
    }

    private class RegistryLoadingSystem() : LoadingSystem
    {
        private string title;
        private bool done;

        public override bool IsReady()
        {
            return done;
        }

        public override string ProgressTitle()
        {
            title ??= Localizer.GetStringByTag("#LOC_BRP_LoadingScreenText");
            return title;
        }

        public override void StartLoad()
        {
            StartCoroutine(DoLoad());
        }

        private IEnumerator DoLoad()
        {
            try
            {
                yield return 0;
                var watch = new System.Diagnostics.Stopwatch();
                watch.Start();

                TypeRegistry.RegisterAll();

                watch.Stop();
                var elapsed = watch.ElapsedMilliseconds;

                LogUtil.Log($"Loaded background converters in {elapsed} ms");
            }
            finally
            {
                done = true;
            }
        }
    }

    private abstract class AssemblyDependency
    {
        public abstract bool IsSatisfied();
    }

    private class KSPAssemblyDependency(string name, Version version) : AssemblyDependency
    {
        public override bool IsSatisfied()
        {
            var loaded = AssemblyLoader.loadedAssemblies;

            foreach (var assembly in loaded)
            {
                if (assembly.name != name)
                    continue;

                var assemblyVersion = new Version(assembly.versionMajor, assembly.versionMinor);
                if (IsVersionCompatible(assemblyVersion, version))
                    return true;
            }

            return false;
        }
    }

    private class DirectAssemblyDependency(string name, Version version) : AssemblyDependency
    {
        public override bool IsSatisfied()
        {
            var domain = AppDomain.CurrentDomain;

            foreach (var assembly in domain.GetAssemblies())
            {
                var asmName = assembly.GetName();
                if (asmName.Name != name)
                    continue;
                if (!IsVersionCompatible(asmName.Version, version))
                    continue;

                return true;
            }

            return false;
        }
    }
}
