using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst.CompilerServices;
using Unity.Collections;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerDisplay("Length = {Length}")]
[DebuggerTypeProxy(typeof(SpanDebugView<>))]
internal readonly unsafe struct RawArray<T>(AllocatorHandle allocator) : IEnumerable<T>
    where T : struct
{
    readonly T* data = null;
    readonly uint length = 0;
    readonly AllocatorHandle allocator = allocator;

    public readonly int Length
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => (int)length;
    }
    public readonly int Count => Length;
    public readonly AllocatorHandle Allocator => allocator;

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

    public RawArray(T* data, int length)
        : this(AllocatorHandle.Temp)
    {
        if (length < 0)
            ThrowLengthOutOfRange();

        if (data == null && length != 0)
            ThrowLengthOutOfRange();

        this.data = data;
        this.length = (uint)length;
    }

    public RawArray(
        int length,
        AllocatorHandle allocator,
        NativeArrayOptions options = NativeArrayOptions.ClearMemory
    )
        : this(allocator)
    {
        if (length < 0)
            ThrowLengthOutOfRange();

        this.length = (uint)length;

        if (length == 0)
            data = null;
        else
        {
            data = allocator.Allocate<T>(length);
            if (options == NativeArrayOptions.ClearMemory)
                Span.Clear();
        }
    }

    public RawArray(MemorySpan<T> values, AllocatorHandle allocator)
        : this(values.Length, allocator, NativeArrayOptions.UninitializedMemory)
    {
        values.CopyTo(Span);
    }

    public RawArray(Span<T> values, AllocatorHandle allocator)
        : this(values.Length, allocator, NativeArrayOptions.UninitializedMemory)
    {
        fixed (T* ptr = values)
        {
            new MemorySpan<T>(ptr, values.Length).CopyTo(Span);
        }
    }

    public readonly RawArray<T> Clone() => new(Span, Allocator);

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
