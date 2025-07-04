using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BackgroundResourceProcessing.Collections;

namespace BackgroundResourceProcessing.Utils;

public class DynamicallySerializable
{
    internal static readonly object Mutex = new();
}

/// <summary>
/// A helper class for types which can be generically deserialized from a
/// <see cref="ConfigNode"/>. This is mostly an implementation detail, the
/// only way you should be interacting with it is by override
/// <see cref="OnLoad"/> and <see cref="OnSave"/> if you need to.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class DynamicallySerializable<T> : DynamicallySerializable
{
    private static readonly ReaderWriterLockSlim rwlock = new();
    private static Dictionary<string, Type> registry = [];

    private readonly BaseFieldList fields;

    protected DynamicallySerializable()
    {
        // This runs in parallel when running tests. We can prevent this
        // from being an issue here by just adding a lock.
        //
        // This is not an issue when running this in KSP because everything
        // happens in a single thread there.
        lock (Mutex)
        {
            fields = new(this);
        }
    }

    protected virtual void OnLoad(ConfigNode node)
    {
        fields.Load(node);
    }

    protected virtual void OnSave(ConfigNode node)
    {
        fields.Save(node);
    }

    public void Save(ConfigNode node)
    {
        node.AddValue("name", GetType().Name);
        OnSave(node);
    }

    protected static DynamicallySerializable<T> Load(
        ConfigNode node,
        Action<DynamicallySerializable<T>> preload = null
    )
    {
        Type type;

        try
        {
            rwlock.EnterReadLock();

            string name = null;
            if (!node.TryGetValue("name", ref name))
            {
                LogUtil.Error("ConfigNode has no `name` field");
                return null;
            }

            if (!registry.TryGetValue(name, out type))
            {
                LogUtil.Error(
                    $"Attempted to load a ConfigNode with name `{name}` but no type has been registered with that name"
                );
                return null;
            }
        }
        finally
        {
            rwlock.ExitReadLock();
        }

        var inst = (DynamicallySerializable<T>)Activator.CreateInstance(type);

        preload?.Invoke(inst);
        inst.OnLoad(node);
        return inst;
    }

    protected static void RegisterAll(IEnumerable<Type> types)
    {
        Dictionary<string, Type> entries = [];
        var baseType = typeof(DynamicallySerializable<T>);

        foreach (var type in types)
        {
            if (!type.IsSubclassOf(baseType))
            {
                LogUtil.Error(
                    $"Type {type.Name} does not inherit from DynamicallySerializable<{typeof(T).Name}>"
                );
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                LogUtil.Error($"Type {type.Name} does not have a default constructor");
                continue;
            }

            if (!entries.TryAddExt(type.Name, type))
            {
                var other = entries[type.Name];

                LogUtil.Error(
                    $"Name conflict: types {type.FullName} and {other.FullName} both have name {type.Name}"
                );
                continue;
            }
        }

        try
        {
            rwlock.EnterWriteLock();
            registry = entries;
        }
        finally
        {
            rwlock.ExitWriteLock();
        }
    }
}
