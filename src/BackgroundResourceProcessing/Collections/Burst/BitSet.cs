using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

namespace BackgroundResourceProcessing.Collections.Burst;

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
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(BitSet set) : IEnumerator<int>
    {
        RawArray<ulong>.Enumerator words = set.bits.GetEnumerator();
        BitEnumerator bits = default;
        int wordIndex = -1;

        public readonly int Current => bits.Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (bits.MoveNext())
                return true;

            while (true)
            {
                if (!words.MoveNext())
                    return false;
                wordIndex++;
                ulong word = words.Current;
                if (word != 0)
                {
                    int index = wordIndex * ULongBits;
                    bits = new BitEnumerator(index, word);
                    return bits.MoveNext();
                }
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }

    #endregion
}
