using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Sse;
using static Unity.Burst.Intrinsics.X86.Sse2;

namespace BackgroundResourceProcessing.Collections.Burst;

[BurstCompile]
internal unsafe struct AdjacencyMatrix : IEnumerable<BitSpan>
{
    const int ULongBits = 64;

    ulong* bits;
    readonly int rows;
    readonly int cols;

    public readonly int Rows
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => rows;
    }
    public readonly int Cols
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => cols * ULongBits;
    }
    public readonly int ColumnWords
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => cols;
    }

    public readonly MemorySpan<ulong> Words => new(bits, rows * cols);

    public readonly BitSpan Bits => new(Words);

    public readonly BitSpan this[int r]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (r < 0 || r >= Rows)
                ThrowRowOutOfRangeException();

            return new(new MemorySpan<ulong>(&bits[r * cols], cols));
        }
    }

    public readonly bool this[int r, int c]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this[r][c];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            var span = this[r];
            span[c] = value;
        }
    }

    public AdjacencyMatrix(int rows, int cols, AllocatorHandle allocator)
    {
        if (rows < 0)
            ThrowNegativeRows();
        if (cols < 0)
            ThrowNegativeCols();

        this.rows = rows;
        this.cols = (int)MathUtil.NextPowerOf2((uint)((cols + (ULongBits - 1)) / ULongBits));
        this.bits = new RawArray<ulong>(this.rows * this.cols, allocator).Ptr;
    }

    [IgnoreWarning(1370)]
    public readonly unsafe void RemoveUnequalColumns(BitSpan bspan, int column)
    {
        if (bspan.Words != ColumnWords)
            throw new ArgumentException("span capacity did not match matrix columns");
        if (column < 0 || column >= Cols)
            throw new ArgumentOutOfRangeException(nameof(column), "column index was out of range");

        int word = column / ULongBits;
        int bit = column % ULongBits;
        var span = bspan.Span;
        var bits = Words;
        ulong* data = Bits.Span.Data;

        Hint.Assume(word < cols);
        Hint.Assume(((uint)cols & ((uint)cols - 1)) == 0);

        // We specialize for column words <= 4, since this covers the vast majority
        // of all KSP vessels and this one method is ~30% of the solver runtime.
        switch (cols)
        {
            case 0:
                break;

            case 1:
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
                break;
            }

            case 2:
                if (IsSse2Supported)
                {
                    v128 set = loadu_si128(span.Data);
                    v128 ones = set1_epi64x(1);
                    v128 vbit = set1_epi64x(bit);

                    for (int i = 0; i < Rows; i += 2)
                    {
                        var vrow = loadu_si128(&data[i * ColumnWords]);
                        var vword =
                            word == 0
                                ? shuffle_epi32(vrow, SHUFFLE(0, 1, 0, 1))
                                : shuffle_epi32(vrow, SHUFFLE(2, 3, 2, 3));
                        // ulong invmask = ((row[word] >> bit) & 1) - 1;
                        var vinvmask = sub_epi64(and_si128(srl_epi64(vword, vbit), ones), ones);

                        set = and_si128(set, xor_si128(vinvmask, vrow));
                    }

                    storeu_si128(span.Data, set);
                    break;
                }

                goto default;

            case 4:
                if (IsAvx2Supported)
                {
                    v256 set = mm256_loadu_si256(span.Data);
                    v256 ones = mm256_set1_epi64x(1);
                    v128 vbit = set1_epi64x(bit);

                    for (int i = 0; i < Rows; ++i)
                    {
                        ulong* row = &data[i * ColumnWords];
                        ulong invmask = ((row[word] >> bit) & 1) - 1;
                        var vinvmask = mm256_set1_epi64x((long)invmask);

                        set = mm256_and_si256(
                            set,
                            mm256_xor_si256(vinvmask, mm256_loadu_si256(row))
                        );
                    }

                    mm256_storeu_si256(span.Data, set);
                    break;
                }
                else if (IsSse2Supported)
                {
                    v128 setlo = loadu_si128(&span.Data[0]);
                    v128 sethi = loadu_si128(&span.Data[2]);
                    v128 ones = set1_epi64x(1);
                    v128 vbit = set1_epi64x(bit);

                    for (int i = 0; i < Rows; ++i)
                    {
                        ulong* row = &data[i * ColumnWords];
                        ulong invmask = ((row[word] >> bit) & 1) - 1;
                        var vinvmask = set1_epi64x(word);

                        setlo = and_si128(setlo, xor_si128(vinvmask, loadu_si128(&row[0])));
                        sethi = and_si128(sethi, xor_si128(vinvmask, loadu_si128(&row[2])));
                    }

                    storeu_si128(&span.Data[0], setlo);
                    storeu_si128(&span.Data[2], sethi);
                    break;
                }

                goto default;

            default:
                for (int r = 0; r < Rows; ++r)
                {
                    ulong* row = &data[r * ColumnWords];
                    // mask is 0 if bit is 1, ~0 otherwise
                    ulong invmask = ((row[word] >> bit) & 1) - 1;

                    for (int c = 0; c < span.Length; ++c)
                        span[c] &= row[c] ^ invmask;
                }
                break;
        }
    }

    /// <summary>
    /// Set all the indices with r > c to 1, making sure to unset any with r >= columns.
    /// </summary>
    [IgnoreWarning(1370)]
    public unsafe void FillUpperDiagonal()
    {
        if (Cols < Rows)
            throw new InvalidOperationException(
                "cannot fill the upper diagonal on a matrix that is taller than it is wide"
            );

        int rowWords = (Rows + (ULongBits - 1)) / ULongBits;
        int lastWord = Rows / ULongBits;
        int lastBit = Rows % ULongBits;
        ulong himask = (1ul << lastBit) - 1;

        for (int r = 0; r < Rows; ++r)
        {
            ulong* row = &bits[r * ColumnWords];
            int word = r / ULongBits;
            int bit = r % ULongBits;
            ulong lomask = ulong.MaxValue << bit << 1;

            row[word] = lomask;

            for (int i = word + 1; i < rowWords; ++i)
                row[i] = ulong.MaxValue;

            row[lastWord] &= himask;
        }
    }

    /// <summary>
    /// Unset any bits in <paramref name="equal"/> whose for which row1 != row2 in the
    /// current matrix.
    /// </summary>
    ///
    /// <remarks>
    /// This method only partially removes unequal rows:
    /// - Bits below the diagonal (i.e. r2 &lt;= r1) will not be modified.
    /// - Rows that are equal to another one with a smaller index are left in an
    ///   indeterminate state.
    /// </remarks>
    [IgnoreWarning(1370)]
    public readonly unsafe void RemoveUnequalRows(AdjacencyMatrix equal)
    {
        if (equal.Cols < Rows || equal.Rows < Rows)
            throw new ArgumentException("equal matrix was too small", nameof(equal));

        Hint.Assume(equal.bits + equal.cols * equal.rows < bits || bits + cols * rows < equal.bits);

        // ulong* mergedData = stackalloc ulong[equal.ColumnWords];
        // BitSpan merged = new(mergedData, equal.ColumnWords);

        for (int r1 = 0; r1 < Rows; ++r1)
        {
            // if (merged[r1])
            //     continue;

            ulong* eqr = &equal.bits[r1 * equal.ColumnWords];

            var rWord = r1 / ULongBits;
            var span = new BitSpan(new MemorySpan<ulong>(eqr, equal.ColumnWords));

            var row1 = &bits[r1 * ColumnWords];

            foreach (int r2 in span.GetEnumeratorAt(r1 + 1))
            {
                var row2 = &bits[r2 * ColumnWords];

                for (int i = 0; i < ColumnWords; ++i)
                {
                    if (row1[i] != row2[i])
                        goto NOT_EQUAL;
                }

                // merged[r2] = true;
                continue;

                NOT_EQUAL:
                equal[r1, r2] = false;
            }
        }
    }

    private interface IShuffleMask
    {
        int Shuffle { get; }
    }

    struct Shuffle0() : IShuffleMask
    {
        public readonly int Shuffle => SHUFFLE(0, 1, 0, 1);
    }

    struct Shuffle1() : IShuffleMask
    {
        public readonly int Shuffle => SHUFFLE(2, 3, 2, 3);
    }

    private readonly unsafe bool RemoveUnequalColumns_Variant2<T>(ulong* span, int bit, T provider)
        where T : unmanaged, IShuffleMask
    {
        ulong* data = Bits.Span.Data;

        if (IsAvx2Supported)
        {
            v128 hset = loadu_si128(span);
            v256 set = mm256_set_m128i(hset, hset);
            v256 ones = mm256_set1_epi64x(1);
            v128 vbit = set1_epi64x(bit);

            int i = 0;
            for (i = 0; i + 4 <= ColumnWords; i += 4)
            {
                var vrow = mm256_loadu_si256(&data[i]);
                var vword = mm256_shuffle_epi32(vrow, provider.Shuffle);
                // ulong invmask = ((row[word] >> bit) & 1) - 1;
                var vinvmask = mm256_sub_epi64(
                    mm256_and_si256(mm256_srl_epi64(vword, vbit), ones),
                    ones
                );

                set = mm256_and_si256(set, mm256_xor_si256(vinvmask, vrow));
            }

            hset = and_si128(mm256_extractf128_si256(set, 0), mm256_extractf128_si256(set, 1));

            if (i < ColumnWords)
            {
                v128 hones = set1_epi64x(1);
                var vrow = loadu_si128(&data[i]);
                var vword = shuffle_epi32(vrow, provider.Shuffle);
                // ulong invmask = ((row[word] >> bit) & 1) - 1;
                var vinvmask = sub_epi64(and_si128(srl_epi64(vword, vbit), hones), hones);

                hset = and_si128(hset, xor_si128(vinvmask, vrow));
            }

            storeu_si128(span, hset);
        }
        else if (IsSse2Supported)
        {
            v128 set = loadu_si128(span);
            v128 ones = set1_epi64x(1);
            v128 vbit = set1_epi64x(bit);

            for (int i = 0; i < ColumnWords; i += 2)
            {
                var vrow = loadu_si128(&data[i]);
                var vword = shuffle_epi32(vrow, provider.Shuffle);
                // ulong invmask = ((row[word] >> bit) & 1) - 1;
                var vinvmask = sub_epi64(and_si128(srl_epi64(vword, vbit), ones), ones);

                set = and_si128(set, xor_si128(vinvmask, vrow));
            }

            storeu_si128(span, set);
        }
        else
        {
            return false;
        }

        return true;
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
        readonly MemorySpan<ulong> words = matrix.Words;
        readonly int cols = matrix.cols;
        int offset = -matrix.cols;

        public readonly BitSpan Current => new(words.Slice(offset, cols));
        readonly object IEnumerator.Current => Current;

        public readonly int Index => offset / cols;

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
