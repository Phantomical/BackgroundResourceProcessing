using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst.CompilerServices;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Unsafe;

[DebuggerVisualizer(typeof(MemorySpan<>.DebugView))]
internal readonly unsafe struct MemorySpan<T>
    where T : struct
{
    public readonly T* Data;
    public readonly int Length;

    public T this[int index]
    {
        [IgnoreWarning(1370)]
        get
        {
            if (index < 0 || index >= Length)
                throw new IndexOutOfRangeException("MemorySpan<T> index was out of range");
            return Data[index];
        }
        [IgnoreWarning(1370)]
        set
        {
            if (index < 0 || index >= Length)
                throw new IndexOutOfRangeException("MemorySpan<T> index was out of range");
            Data[index] = value;
        }
    }

    public unsafe MemorySpan(T* data, int length)
    {
        if (length < 0)
            ThrowLengthNegativeException();

        Data = data;
        Length = length;
    }

    [IgnoreWarning(1370)]
    private static void ThrowLengthNegativeException()
    {
        throw new ArgumentOutOfRangeException("length was negative");
    }

    [IgnoreWarning(1370)]
    public MemorySpan<T> Slice(int start, int length)
    {
        if (start < 0 || start + length > Length)
            throw new IndexOutOfRangeException("MemorySpan<T>.Slice arguments were out of range");

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
