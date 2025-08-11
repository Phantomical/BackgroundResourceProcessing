using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Unsafe;

[DebuggerTypeProxy(typeof(MemorySpan<>.DebugView))]
internal readonly unsafe struct MemorySpan<T>(T* data, uint length)
    where T : struct
{
    public readonly T* Data = data;
    public readonly uint Length = length;

    public T this[uint index]
    {
        get
        {
            if (index >= Length)
                ThrowIndexOutOfRangeException();
            return Data[index];
        }
        set
        {
            if (index >= Length)
                ThrowIndexOutOfRangeException();
            Data[index] = value;
        }
    }

    [IgnoreWarning(1370)]
    private static void ThrowLengthNegativeException() =>
        throw new ArgumentOutOfRangeException("length was negative");

    [IgnoreWarning(1370)]
    private static void ThrowIndexOutOfRangeException() =>
        throw new IndexOutOfRangeException("MemorySpan<T> index was out of range");

    [IgnoreWarning(1370)]
    private static void ThrowSliceArgumentsOutOfRangeException() =>
        throw new IndexOutOfRangeException("MemorySpan<T>.Slice arguments were out of range");

    public MemorySpan<T> Slice(uint start, uint length)
    {
        if (start + length > Length)
            ThrowSliceArgumentsOutOfRangeException();

        return new(Data + start, length);
    }

    public void Fill(T value)
    {
        for (int i = 0; i < Length; ++i)
            Data[i] = value;
    }

    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    public ref struct Enumerator(MemorySpan<T> span) : IEnumerator<T>
    {
        T* current = span.Data;
        readonly T* end = span.Data + span.Length;

        public readonly T Current => *current;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (current == end)
                return false;
            current += 1;
            return true;
        }

        public readonly void Dispose() { }

        public readonly void Reset()
        {
            throw new NotImplementedException();
        }
    }

    private class DebugView(MemorySpan<T> span)
    {
        T[] values = [.. span];

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items => values;
    }
}
