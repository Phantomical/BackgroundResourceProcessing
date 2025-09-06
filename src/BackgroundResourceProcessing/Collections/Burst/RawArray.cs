using System;
using System.Collections;
using System.Collections.Generic;
using BackgroundResourceProcessing.BurstSolver;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Burst;

internal unsafe struct RawArray<T>(Allocator allocator) : IEnumerable<T>, IDisposable
    where T : struct
{
    T* data = null;
    uint length = 0;
    readonly Allocator allocator = allocator;

    public readonly int Length
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => (int)length;
    }
    public readonly int Count => Length;
    public readonly Allocator Allocator => allocator;
    public readonly Span<T> Span => new(data, Length);

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

    public RawArray(
        int length,
        Allocator allocator,
        NativeArrayOptions options = NativeArrayOptions.ClearMemory
    )
        : this(allocator)
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
                new Span<T>(data, length).Fill(default);
        }
        else
        {
            data = UnityAllocator.Alloc<T>(length, allocator);

            if (options == NativeArrayOptions.ClearMemory)
                UnityAllocator.Clear(data, length);
        }

        this.length = (uint)length;
    }

    public RawArray(Span<T> values, Allocator allocator)
        : this(values.Length, allocator, NativeArrayOptions.UninitializedMemory)
    {
        if (BurstUtil.UseTestAllocator)
        {
            values.CopyTo(Span);
        }
        else
        {
            fixed (T* src = values)
            {
                UnityAllocator.Copy(data, src, values.Length);
            }
        }
    }

    public readonly RawArray<T> Clone() => new(Span, Allocator);

    public void Dispose()
    {
        if (data is null)
            return;

        if (BurstUtil.UseTestAllocator)
            TestAllocator.Free(data);
        else
            UnityAllocator.Free(data, allocator);

        data = null;
        length = 0;
    }

    public void Fill(T item) => Span.Fill(item);

    public static implicit operator Span<T>(RawArray<T> array) => array.Span;

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

    [IgnoreWarning(1310)]
    static void ThrowLengthOutOfRange() => throw new ArgumentOutOfRangeException("length");

    [IgnoreWarning(1310)]
    static void ThrowIndexOutOfRange() =>
        throw new IndexOutOfRangeException("array index out of range");
}
