using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.BurstSolver;
using Unity.Burst.CompilerServices;
using Unity.Collections;

namespace BackgroundResourceProcessing.Collections.Burst;

internal struct AdjacencyMatrix : IEnumerable<BitSpan>
{
    const int ULongBits = 64;

    RawArray<ulong> bits;
    readonly int rows;
    readonly int cols;

    public readonly int Rows
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => rows;
    }
    public readonly int Cols
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => cols * ULongBits;
    }
    public readonly int ColumnWords
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => cols;
    }

    public readonly BitSpan Bits => new(bits.Span);

    public readonly BitSpan this[int r]
    {
        get
        {
            AssumeSize();
            if (r < 0 || r >= Rows)
                ThrowRowOutOfRangeException();

            return new(bits.Span.Slice(r * ColumnWords, ColumnWords));
        }
    }

    public readonly bool this[int r, int c]
    {
        get => this[r][c];
        set
        {
            var span = this[r];
            span[c] = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void AssumeSize() => Hint.Assume(rows * cols < bits.Length);

    public AdjacencyMatrix(int rows, int cols)
    {
        if (rows < 0)
            ThrowNegativeRows();
        if (cols < 0)
            ThrowNegativeCols();

        this.rows = rows;
        this.cols = (cols + (ULongBits - 1)) / ULongBits;
        this.bits = new RawArray<ulong>(this.rows * this.cols);
    }

    [IgnoreWarning(1370)]
    public readonly unsafe void RemoveUnequalColumns(BitSpan bspan, int column)
    {
        if (bspan.Words != ColumnWords)
            throw new ArgumentException("span capacity did not match matrix columns");
        if (column < 0 || column >= Cols)
            throw new ArgumentOutOfRangeException("column index was out of range");

        int word = column / ULongBits;
        int bit = column % ULongBits;
        var span = bspan.Span;
        var bits = this.bits.Span;

        if (cols == 0)
            return;
        // We specialize for 1 column words, since that is an extraordinarily common case
        // and it is rather easy to vectorize.
        if (cols == 1)
        {
            ulong set = span[0];
            foreach (ulong w in bits)
            {
                // mask is 0 if bit is 1, ~0 otherwise
                var invmask = ((w >> bit) & 1ul) - 1ul;
                var equal = w ^ invmask;

                set &= equal;
            }
            span[0] = set;
        }
        else
        {
            for (int r = 0; r < Rows; ++r)
            {
                var row = this[r].Span;
                // mask is 0 if bit is 1, ~0 otherwise
                ulong invmask = ((row[word] >> bit) & 1) - 1;

                for (int c = 0; c < span.Length; ++c)
                    span[c] &= row[c] ^ invmask;
            }
        }
    }

    public readonly void SetEqualColumns(BitSpan span, int column)
    {
        span.Fill(true);
        RemoveUnequalColumns(span, column);
    }

    #region Exception Methods
    [IgnoreWarning(1370)]
    static void ThrowRowOutOfRangeException() =>
        throw new IndexOutOfRangeException("row index was out of range");

    [IgnoreWarning(1370)]
    static void ThrowNegativeRows() => throw new ArgumentOutOfRangeException("rows");

    [IgnoreWarning(1370)]
    static void ThrowNegativeCols() => throw new ArgumentOutOfRangeException("cols");

    static void Assume(bool cond)
    {
        if (BurstUtil.IsBurstCompiled)
            Hint.Assume(cond);

        if (!cond)
            throw new Exception("assumption failed");
    }
    #endregion

    #region Enumerator
    public readonly RowEnumerator GetEnumerator() => new(this);

    readonly IEnumerator<BitSpan> IEnumerable<BitSpan>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct RowEnumerator(AdjacencyMatrix matrix) : IEnumerator<BitSpan>
    {
        readonly MemorySpan<ulong> words = matrix.bits;
        readonly int cols = matrix.cols;
        int offset = -matrix.cols;

        public readonly BitSpan Current => new(words.Slice(offset, cols));
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            offset += cols;
            return offset < words.Length;
        }

        public void Reset() => offset = -cols;

        public void Dispose() { }
    }

    #endregion
}
