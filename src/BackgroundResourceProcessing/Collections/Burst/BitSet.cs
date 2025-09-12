using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerDisplay("Capacity = {Capacity}")]
[DebuggerTypeProxy(typeof(BitSpan.DebugView))]
internal struct BitSet : IEnumerable<int>
{
    public const int ULongBits = 64;

    RawArray<ulong> bits;

    public readonly int Capacity => bits.Count * 64;
    public readonly BitSpan Span => new(bits.Span);
    public readonly AllocatorHandle Allocator => bits.Allocator;

    public readonly bool this[int key]
    {
        get => Span[key];
        set
        {
            var span = Span;
            span[key] = value;
        }
    }

    public BitSet() => bits = new();

    public BitSet(int capacity, AllocatorHandle allocator) =>
        bits = new(WordsRequired(capacity), allocator);

    public unsafe BitSet(ulong* words, int length) => bits = new(words, length);

    public BitSet(MemorySpan<ulong> span, AllocatorHandle allocator) => bits = new(span, allocator);

    public BitSet(Span<ulong> span, AllocatorHandle allocator) => bits = new(span, allocator);

    public BitSet(BitSpan span, AllocatorHandle allocator)
        : this(span.Span, allocator) { }

    public static int WordsRequired(int capacity) => (capacity + (ULongBits - 1)) / ULongBits;

    public readonly bool Contains(int key) => Span.Contains(key);

    public void Add(int key) => Span.Add(key);

    public void Remove(int key) => Span.Remove(key);

    public void Clear() => Span.Clear();

    public readonly int GetCount() => Span.GetCount();

    public void Fill(bool value = true) => Span.Fill(value);

    public void ClearUpFrom(int index) => Span.ClearUpFrom(index);

    public void ClearUpTo(int index) => Span.ClearUpTo(index);

    public void ClearOutsideRange(int start, int end) => Span.ClearOutsideRange(start, end);

    public void SetUpTo(int index) => Span.SetUpTo(index);

    public void CopyFrom(BitSet other) => Span.CopyFrom(other.Span);

    public void CopyInverseFrom(BitSet other) => Span.CopyInverseFrom(other.Span);

    public void RemoveAll(BitSet other) => Span.RemoveAll(other.Span);

    public readonly BitSet Clone() => new(Span.Span, Allocator);

    #region IEnumerator<T>
    public readonly BitSpan.Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion
}
