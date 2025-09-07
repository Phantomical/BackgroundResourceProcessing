using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BackgroundResourceProcessing.Collections.Burst;

/// <summary>
/// A class which calls T.Dispose if it implements <see cref="IDisposable"/>.
/// This uses a bunch of internal hacks to ensure that it even works when
/// called from burst code.
/// </summary>
/// <typeparam name="T"></typeparam>
internal static class ItemDisposer<T>
    where T : struct
{
    static readonly MethodInfo DisposeMethod = null;

    // So we really need this to be usable from burst-compiled code.
    // Unfortunately, making that work eliminates most of the sane methods you
    // could use to implement this.
    //
    // Instead, we use harmony to replace the body of the method at during the
    // static constructor, which is guaranteed to happen before burst can look
    // at the IL for the method.
    static ItemDisposer()
    {
        if (!typeof(T).GetInterfaces().Contains(typeof(IDisposable)))
            return;

        var implty = typeof(ItemDisposer.DisposeImpl<>).MakeGenericType([typeof(T)]);
        DisposeMethod = implty.GetMethod(nameof(ItemDisposer.DisposeImpl<>.Dispose));
        if (DisposeMethod == null)
            throw new InvalidOperationException(
                $"unable to find Dispose method for IDisposable type {typeof(T).Name}"
            );

        NeedsDispose = true;
        var target = typeof(ItemDisposer<T>).GetMethod(
            nameof(DisposeImpl),
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Harmony harmony = new($"BackgroundResourceProcessing.ItemDisposer<{typeof(T).Name}>");
        harmony.Patch(
            target,
            transpiler: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => Transpile(null)))
        );
    }

    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> _)
    {
        return
        [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, DisposeMethod),
            new CodeInstruction(OpCodes.Ret),
        ];
    }

    public static readonly bool NeedsDispose;

    static void DisposeImpl(ref T item) { }

    public static void Dispose(ref T item) => DisposeImpl(ref item);

    public static void Dispose(T item) => DisposeImpl(ref item);

    public static void DisposeRange(MemorySpan<T> items)
    {
        if (!NeedsDispose)
            return;

        foreach (ref T item in items)
            Dispose(ref item);
    }
}

internal static class ItemDisposer
{
    internal static class DisposeImpl<U>
        where U : struct, IDisposable
    {
        public static void Dispose(ref U item) => item.Dispose();
    }

    internal static DisposeGuard<T> Guard<T>(T item)
        where T : struct
    {
        return new(item);
    }

    internal static DisposeRangeGuard<T> Guard<T>(MemorySpan<T> item)
        where T : struct
    {
        return new(item);
    }
}

internal ref struct DisposeGuard<T>(T item) : IDisposable
    where T : struct
{
    public readonly void Dispose() => ItemDisposer<T>.Dispose(item);
}

internal readonly ref struct DisposeRangeGuard<T>(MemorySpan<T> items) : IDisposable
    where T : struct
{
    readonly MemorySpan<T> items = items;

    public void Dispose() => ItemDisposer<T>.DisposeRange(items);
}
