using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.BurstSolver;
using Unity.Burst.CompilerServices;
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
        get => length;
    }
    public bool IsEmpty => Length == 0;
    public T* Data => data;

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Length)
                ThrowIndexOutOfRange();

            return ref data[index];
        }
    }

    public MemorySpan() { }

    public MemorySpan(T* data, int length)
    {
        if (length < 0)
            ThrowLengthOutOfRange();

        this.data = data;
        this.length = length;
    }

    public MemorySpan(RawArray<T> array)
        : this(array.Ptr, array.Length) { }

    public MemorySpan(RawList<T> array)
        : this(array.Ptr, array.Count) { }

    public static implicit operator MemorySpan<T>(RawArray<T> array) => new(array);

    public static implicit operator MemorySpan<T>(RawList<T> list) => new(list);

    public Span<T> AsSystemSpan() => new(data, length);

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
            ThrowDestinationTooShort();
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

    public static bool operator ==(MemorySpan<T> lhs, MemorySpan<T> rhs)
    {
        return lhs.Length == rhs.Length && lhs.data == rhs.data;
    }

    public static bool operator !=(MemorySpan<T> lhs, MemorySpan<T> rhs)
    {
        return !(lhs == rhs);
    }

    public override bool Equals(object obj)
    {
        return obj is MemorySpan<T> span && span == this;
    }

    public override int GetHashCode()
    {
        throw new NotSupportedException();
    }

    public MemorySpan<T> Slice(int start)
    {
        if ((uint)start > Length)
            ThrowIndexOutOfRange();

        return new(data + start, Length - start);
    }

    public MemorySpan<T> Slice(int start, int length)
    {
        if ((uint)start > Length || (uint)length > (Length - start))
            ThrowIndexOutOfRange();

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

    #region Exception Helpers
    [IgnoreWarning(1370)]
    static void ThrowIndexOutOfRange() =>
        throw new IndexOutOfRangeException("span index was out of range");

    static void ThrowLengthOutOfRange() =>
        throw new ArgumentOutOfRangeException("span length was negative");

    static void ThrowDestinationTooShort() =>
        throw new ArgumentException("MemorySpan.CopyTo destination was too short");
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

public static class MemorySpanExtensions
{
    static readonly Type[] BitwiseEquatableTypes =
    [
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
    ];

    private static class IsBitwiseEquatableData<T>
    {
        internal static readonly bool Comparable;

        static IsBitwiseEquatableData()
        {
            Comparable = IsBitwiseComparableImpl();
        }

        static bool IsBitwiseComparableImpl()
        {
            if (BitwiseEquatableTypes.Contains(typeof(T)))
                return true;

            if (typeof(T).IsPointer)
                return true;
            if (typeof(T).IsEnum)
                return true;

            return false;
        }
    }

    private static bool IsBitwiseEquatable<T>()
    {
        return IsBitwiseEquatableData<T>.Comparable;
    }

    internal static unsafe bool SequenceEqual<T>(this MemorySpan<T> lhs, MemorySpan<T> rhs)
        where T : struct, IEquatable<T>
    {
        if (lhs.Length != rhs.Length)
            return false;

        if (BurstUtil.IsBurstCompiled)
        {
            if (IsBitwiseEquatable<T>())
                return UnityAllocator.Cmp(lhs.Data, rhs.Data, lhs.Length) == 0;
        }

        for (int i = 0; i < lhs.Length; ++i)
        {
            if (!lhs[i].Equals(rhs[i]))
                return false;
        }

        return true;
    }
}
