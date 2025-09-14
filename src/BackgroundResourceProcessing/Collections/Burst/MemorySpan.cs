using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.BurstSolver;
using KSPAchievements;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using CSUnsafe = System.Runtime.CompilerServices.Unsafe;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerDisplay("Length = {Length}")]
[DebuggerTypeProxy(typeof(SpanDebugView<>))]
internal readonly unsafe struct MemorySpan<T> : IEnumerable<T>
    where T : struct
{
    readonly T* data;
    readonly int length;

    public int Length
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => length;
    }
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Length == 0;
    }
    public T* Data => data;

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Length)
                BurstCrashHandler.Crash(Error.MemorySpan_IndexOutOfRange, index);

            return ref data[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MemorySpan() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MemorySpan(T* data, int length)
    {
        if (length < 0)
            BurstCrashHandler.Crash(Error.MemorySpan_NegativeLength);

        this.data = data;
        this.length = length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MemorySpan(RawArray<T> array)
        : this(array.Ptr, array.Length) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MemorySpan(RawList<T> array)
        : this(array.Ptr, array.Count) { }

    public static implicit operator MemorySpan<T>(RawArray<T> array) => new(array);

    public static implicit operator MemorySpan<T>(RawList<T> list) => new(list);

    public Span<T> AsSystemSpan() => new(data, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref T GetUnchecked(int key) => ref data[key];

    public void Clear()
    {
        if (BurstUtil.IsBurstCompiled)
            UnityAllocator.Clear(data, Length);
        else
        {
            for (int i = 0; i < Length; ++i)
                data[i] = default;
        }
    }

    public void Fill(T value)
    {
        if (BurstUtil.IsBurstCompiled && CSUnsafe.SizeOf<T>() == 1)
        {
            UnityAllocator.Set(data, CSUnsafe.As<T, byte>(ref value), Length);
        }
        else
        {
            for (int i = 0; i < Length; ++i)
                data[i] = value;
        }
    }

    public void CopyTo(MemorySpan<T> dst)
    {
        if (!TryCopyTo(dst))
            BurstCrashHandler.Crash(Error.MemorySpan_CopyTo_DestinationTooShort);
    }

    public bool TryCopyTo(MemorySpan<T> dst)
    {
        if (IsEmpty)
            return true;

        if (Length > dst.Length)
            return false;

        if (BurstUtil.IsBurstCompiled)
        {
            UnityAllocator.Copy(dst.data, data, Length);
        }
        else
        {
            for (int i = 0; i < Length; ++i)
                dst.data[i] = data[i];
        }

        return true;
    }

    public MemorySpan<T> Slice(int start)
    {
        if ((uint)start > Length)
            BurstCrashHandler.Crash(Error.MemorySpan_IndexOutOfRange);

        return new(data + start, Length - start);
    }

    public MemorySpan<T> Slice(int start, int length)
    {
        if ((uint)start > Length || (uint)length > (Length - start))
            BurstCrashHandler.Crash(Error.MemorySpan_IndexOutOfRange);

        return new(data + start, length);
    }

    #region Enumerator
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(MemorySpan<T> span) : IEnumerator<T>
    {
        T* current = span.data - 1;
        readonly T* end = span.data + span.Length;

        public readonly ref T Current => ref *current;
        readonly T IEnumerator<T>.Current => Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            current += 1;
            return current < end;
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }

    #endregion
}

internal sealed class SpanDebugView<T>(MemorySpan<T> span)
    where T : struct
{
    public SpanDebugView(RawList<T> list)
        : this((MemorySpan<T>)list) { }

    public SpanDebugView(RawArray<T> list)
        : this((MemorySpan<T>)list) { }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public T[] Items { get; } = [.. span];
}

[BurstCompile]
public static class MemorySpanExtensions
{
    internal static unsafe bool SequenceEqual(this MemorySpan<ulong> lhs, MemorySpan<ulong> rhs)
    {
        if (lhs.Length != rhs.Length)
            return false;

        if (BurstUtil.IsBurstCompiled)
            return UnityAllocator.Cmp(lhs.Data, rhs.Data, lhs.Length) == 0;
        else
            return SequenceEqualManaged(lhs.Data, rhs.Data, (uint)lhs.Length);
    }

    static unsafe bool SequenceEqualManaged(ulong* lhs, ulong* rhs, uint length)
    {
        for (uint i = 0; i < length; ++i)
        {
            if (lhs[i] != rhs[i])
                return false;
        }

        return true;
    }

    internal static unsafe void Sort<T>(this MemorySpan<T> span)
        where T : unmanaged, IComparable<T>
    {
        NativeSortExtension.Sort(span.Data, span.Length);
    }

    internal static unsafe void Sort<T, U>(this MemorySpan<T> span, U comparer)
        where T : unmanaged
        where U : IComparer<T>
    {
        NativeSortExtension.Sort(span.Data, span.Length, comparer);
    }
}
