using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.BurstSolver;
using Unity.Burst.CompilerServices;
using Unity.Collections;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerDisplay("Length = {Length}")]
[DebuggerTypeProxy(typeof(SpanDebugView<>))]
internal unsafe struct RawArray<T>() : IEnumerable<T>
    where T : struct
{
    T* data = null;
    uint length = 0;

    public readonly int Length
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => (int)length;
    }
    public readonly int Count => Length;

    public readonly MemorySpan<T> Span => (MemorySpan<T>)this;
    public readonly T* Ptr => data;

    public readonly ref T this[int index]
    {
        get
        {
            if (index < 0 || index >= Length)
                ThrowIndexOutOfRange();

            return ref data[index];
        }
    }

    public readonly ref T this[uint index]
    {
        get
        {
            if (index >= Length)
                ThrowIndexOutOfRange();

            return ref data[index];
        }
    }

    public RawArray(int length, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        : this()
    {
        if (length < 0)
            ThrowLengthOutOfRange();

        if (length == 0)
        {
            data = null;
        }
        else if (BurstUtil.UseTestAllocator)
        {
            data = TestAllocator.Alloc<T>(length);

            if (options == NativeArrayOptions.ClearMemory)
            {
                for (int i = 0; i < length; ++i)
                    data[i] = default;
            }
        }
        else
        {
            data = UnityAllocator.Alloc<T>(length, Allocator.Temp);

            if (options == NativeArrayOptions.ClearMemory)
                UnityAllocator.Clear(data, length);
        }

        this.length = (uint)length;
    }

    public RawArray(MemorySpan<T> values)
        : this(values.Length, NativeArrayOptions.UninitializedMemory)
    {
        values.CopyTo(Span);
    }

    public RawArray(Span<T> values)
        : this(values.Length, NativeArrayOptions.UninitializedMemory)
    {
        fixed (T* ptr = values)
        {
            new MemorySpan<T>(ptr, values.Length).CopyTo(Span);
        }
    }

    public readonly RawArray<T> Clone() => new(Span);

    public readonly void Fill(T item) => Span.Fill(item);

    #region IEnumerable<T>
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(RawArray<T> array) : IEnumerator<T>
    {
        T* ptr = array.data - 1;
        readonly T* end = array.data + array.length;

        public readonly ref T Current => ref *ptr;
        readonly T IEnumerator<T>.Current => Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ptr += 1;
            return ptr < end;
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }
    #endregion

    [IgnoreWarning(1370)]
    static void ThrowLengthOutOfRange() => throw new ArgumentOutOfRangeException("length");

    [IgnoreWarning(1370)]
    static void ThrowIndexOutOfRange() =>
        throw new IndexOutOfRangeException("array index out of range");

    private sealed class DebugView(RawArray<T> array)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items { get; } = [.. array];
    }
}
