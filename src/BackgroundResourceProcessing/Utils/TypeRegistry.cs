using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Inventory;

namespace BackgroundResourceProcessing.Utils
{
    internal interface IRegistryItem
    {
        void Load(ConfigNode node);
    }

    internal class TypeRegistry<T>(string nodeName)
        where T : IRegistryItem
    {
        private static bool initializedTypes = false;
        private readonly string nodeName = nodeName;
        private readonly Dictionary<Type, T> entries = [];

        internal T GetEntryForType(Type type)
        {
            if (!entries.TryGetValue(type, out var entry))
                entry = default;
            return entry;
        }

        internal void LoadAll()
        {
            if (!initializedTypes)
            {
                InitializeAssemblyLoaderTypes();
                initializedTypes = true;
            }

            entries.Clear();
            var nodes = GameDatabase.Instance.GetConfigNodes(nodeName);

            foreach (var node in nodes)
            {
                var entry = LoadOne(node);
                if (entry == null)
                    continue;

                var (type, item) = (KeyValuePair<Type, T>)entry;
                if (!entries.TryAddExt(type, item))
                {
                    LogUtil.Error(
                        $"Multiple {nodeName}s targeting {type.Name}. Only the first one will be used."
                    );
                    continue;
                }
            }
        }

        private KeyValuePair<Type, T>? LoadOne(ConfigNode node)
        {
            string name = null;
            string adapter = null;

            if (!node.TryGetValue("name", ref name))
            {
                LogUtil.Error($"{nodeName} node did not have a `name` key");
                return null;
            }

            if (!node.TryGetValue("adapter", ref adapter))
            {
                LogUtil.Error($"{nodeName} node for module {name} did not have an `adapter` key");
                return null;
            }

            LogUtil.Log($"Loading {nodeName} {adapter} for part module {name}");

            Type moduleType = AssemblyLoader.GetClassByName(typeof(PartModule), name);
            if (moduleType == null)
            {
                LogUtil.Error($"{nodeName}: Unable to find PartModule {name}");
                return null;
            }

            Type adapterType = AssemblyLoader.GetClassByName(typeof(T), adapter);
            if (adapterType == null)
            {
                LogUtil.Error($"{nodeName}[{name}]: Unable to find {typeof(T).Name} {adapter}");
                return null;
            }

            try
            {
                T instance = (T)Activator.CreateInstance(adapterType);
                instance.Load(node);
                return new(moduleType, instance);
            }
            catch (Exception e)
            {
                LogUtil.Error($"{nodeName}[{name}]: {adapter} load threw an exception: {e}");
                return null;
            }
        }

        private static void InitializeAssemblyLoaderTypes()
        {
            var baseType = typeof(T);

            foreach (var assembly in AssemblyLoader.loadedAssemblies)
            {
                foreach (var type in assembly.assembly.GetTypes())
                {
                    if (!type.IsSubclassOf(baseType))
                        continue;

                    assembly.types.Add(baseType, type);
                    assembly.typesDictionary.Add(baseType, type);
                }
            }
        }
    }

    internal static class TypeRegistry
    {
        internal static void RegisterAll()
        {
            ConverterBehaviour.RegisterAll();
            BackgroundConverter.LoadAll();
            BackgroundInventory.LoadAll();
        }

        internal static void RegisterForTest()
        {
            var assembly = typeof(TypeRegistry).Assembly;

            ConverterBehaviour.RegisterAll(
                assembly
                    .GetTypes()
                    .Where(type => type.IsSubclassOf(typeof(ConverterBehaviour)))
                    .Where(type => !type.IsAbstract)
            );
        }
    }
}
