using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Solver.Graph;
using BackgroundResourceProcessing.Utils;

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

namespace BackgroundResourceProcessing.Collections
{
    [DebuggerTypeProxy(typeof(DebugView))]
    [DebuggerDisplay("Rows = {Rows}, Columns = {Columns}")]
    internal class AdjacencyMatrix
    {
        const int ULongBits = 64;

        readonly ulong[] bits;

        readonly int rows;
        readonly int cols;

        public int Rows => rows;
        public int Columns => cols * ULongBits;

        public ulong[] Bits => bits;

        public int ColumnWords => cols;

        public bool this[int r, int c]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return GetRow(r)[c]; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { GetRow(r)[c] = value; }
        }

        public AdjacencyMatrix(int rows, int cols)
        {
            this.rows = rows;
            this.cols = (cols + (ULongBits - 1)) / ULongBits;
            this.bits = new ulong[this.rows * this.cols];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitSliceX GetRow(int row)
        {
            if (row < 0 || row > Rows)
                ThrowRowOutOfRangeException(row);

            Span<ulong> span = new(bits);
            return new(span.Slice(row * ColumnWords, ColumnWords));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitSliceY GetColumn(int column)
        {
            if (column < 0 || column > Columns)
                ThrowColumnOutOfRangeException(column);
            return new(this, column);
        }

        private void ThrowColumnOutOfRangeException(int column)
        {
            throw new IndexOutOfRangeException(
                $"Column index is out of bounds for this AdjacencyMatrix ({column} >= {Columns})"
            );
        }

        private void ThrowRowOutOfRangeException(int row)
        {
            throw new IndexOutOfRangeException(
                $"Row index is out of bounds for this AdjacencyMatrix ({row} >= {Rows})"
            );
        }

        public RowEnumerator GetRows()
        {
            return new(this);
        }

        /// <summary>
        /// Unset bits <paramref name="set"/> corresponding to columns in the
        /// matrix that are not equal to <paramref name="column"/>.
        /// </summary>
        /// <param name="set">A <see cref="BitSet"/> with the same capacity as the number of columns in this matrix. It will be overwritten.</param>
        /// <param name="column">The column to compare against.</param>
        /// <exception cref="ArgumentException"></exception>
        public void RemoveUnequalColumns(BitSet set, int column)
        {
            if (set.Capacity != Columns)
                throw new ArgumentException(
                    "set",
                    "Set capacity did not match the adjacency matrix column count"
                );
            if (column < 0 || column >= Columns)
                ThrowColumnOutOfRangeException(column);

            Span<ulong> swords = set.Bits;
            for (int r = 0; r < Rows; ++r)
            {
                var row = GetRow(r);
                var rwords = row.Bits;
                var mask = row[column] ? ulong.MaxValue : 0;

                for (int c = 0; c < ColumnWords; ++c)
                {
                    // a ^ b gives us the bits that are different so ~(a ^ b)
                    // gives us the bits that are equal.
                    var equal = ~(rwords[c] ^ mask);
                    swords[c] &= equal;
                }
            }
        }

        /// <summary>
        /// Efficiently fill <paramref name="set"/> with all columns equal to <paramref name="column"/>.
        /// </summary>
        /// <param name="set">A <see cref="BitSet"/> with the same capacity as the number of columns in this matrix. It will be overwritten.</param>
        /// <param name="column">The column to compare against.</param>
        /// <exception cref="ArgumentException"></exception>
        public void SetEqualColumns(BitSet set, int column)
        {
            new Span<ulong>(set.Bits).Fill(ulong.MaxValue);
            RemoveUnequalColumns(set, column);
        }

        public ref struct RowEnumerator(AdjacencyMatrix matrix)
        {
            readonly AdjacencyMatrix matrix = matrix;
            int row = -1;

            public readonly int Index => row;
            public readonly BitSliceX Current => matrix.GetRow(row);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                row += 1;
                return row < matrix.Rows;
            }

            public void Reset()
            {
                row = -1;
            }

            public void Dispose() { }

            public readonly RowEnumerator GetEnumerator()
            {
                return this;
            }
        }

        private class DebugView
        {
            AdjacencyMatrix matrix;
            Row[] rows = null;

            public DebugView(AdjacencyMatrix matrix)
            {
                this.matrix = matrix;
            }

            Row[] GetRows()
            {
                List<Row> rows = [];

                for (int r = 0; r < matrix.Rows; ++r)
                {
                    int[] items = [.. matrix.GetRow(r)];
                    if (items.Length == 0)
                        continue;

                    rows.Add(new Row() { converter = r, inventories = items });
                }

                return [.. rows];
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            Row[] Items
            {
                get
                {
                    rows ??= GetRows();
                    return rows;
                }
            }

            [DebuggerTypeProxy(typeof(RowDebugView))]
            [DebuggerDisplay("Converter = {converter}, Count = {Count}")]
            private struct Row
            {
                public int converter;
                public int[] inventories;

                private readonly int Count => inventories.Length;
            }

            private class RowDebugView(Row row)
            {
                [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
                public int[] Items => row.inventories;
            }
        }
    }

    [DebuggerTypeProxy(typeof(BitSliceDebugView))]
    [DebuggerDisplay("Capacity = {Capacity}")]
    internal readonly ref struct BitSliceX(Span<ulong> bits)
    {
        const int ULongBits = 64;

        readonly Span<ulong> bits = bits;

        public readonly int Capacity => bits.Length * ULongBits;
        public readonly Span<ulong> Bits => bits;

        public bool this[int column]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (column < 0 || column >= Capacity)
                    ThrowOutOfRangeException(column);

                var word = bits[column / ULongBits];
                int bit = column % ULongBits;

                return (word & (1ul << bit)) != 0;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (column < 0 || column >= Capacity)
                    ThrowOutOfRangeException(column);

                var word = column / ULongBits;
                var bit = column % ULongBits;
                var mask = 1ul << bit;

                if (value)
                    bits[word] |= mask;
                else
                    bits[word] &= ~mask;
            }
        }

        private void ThrowOutOfRangeException(int column)
        {
            throw new IndexOutOfRangeException(
                $"Column index was out of bounds for this BitSlice ({column} >= {Capacity})"
            );
        }

        public void AndWith(BitSliceX other)
        {
            if (other.Capacity > Capacity)
                throw new ArgumentException("BitSlice instances have different lengths");

            for (int i = 0; i < other.bits.Length; ++i)
                bits[i] &= other.bits[i];
        }

        public void OrWith(BitSliceX other)
        {
            if (other.Capacity > Capacity)
                throw new ArgumentException("BitSlice instances have different lengths");

            for (int i = 0; i < other.bits.Length; ++i)
                bits[i] |= other.bits[i];
        }

        public void Zero()
        {
            bits.Fill(0);
        }

        public readonly Enumerator GetEnumerator()
        {
            return new(this);
        }

        public static bool operator ==(BitSliceX a, BitSliceX b)
        {
            if (a.Capacity != b.Capacity)
                return false;

            for (int i = 0; i < a.bits.Length; ++i)
            {
                if (a.bits[i] != b.bits[i])
                    return false;
            }

            return true;
        }

        public static bool operator !=(BitSliceX a, BitSliceX b)
        {
            return !(a == b);
        }

        public ref struct Enumerator(BitSliceX slice) : IEnumerator<int>
        {
            int index = -1;
            readonly BitSliceX slice = slice;
            BitEnumerator inner = default;

            public readonly int Current => inner.Current;

            readonly object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                while (true)
                {
                    if (inner.MoveNext())
                        return true;

                    index += 1;

                    if (index >= slice.bits.Length)
                        return false;
                    inner = new(index * ULongBits, slice.bits[index]);
                }
            }

            public void Reset()
            {
                index = -1;
                inner = default;
            }

            public readonly void Dispose() { }
        }
    }

    [DebuggerTypeProxy(typeof(BitSliceDebugView))]
    [DebuggerDisplay("Capacity = {Capacity}")]
    internal readonly ref struct BitSliceY
    {
        const int ULongBits = 64;

        readonly AdjacencyMatrix matrix;
        readonly int word;
        readonly ulong mask;

        public int Capacity => matrix.Rows;

        public bool this[int row]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (row < 0 || row >= Capacity)
                    ThrowRowOutOfRangeException(row);

                return (matrix.Bits[matrix.ColumnWords * row + word] & mask) != 0;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (row < 0 || row >= Capacity)
                    ThrowRowOutOfRangeException(row);

                var index = matrix.ColumnWords * row + word;
                if (value)
                    matrix.Bits[index] |= mask;
                else
                    matrix.Bits[index] &= mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitSliceY(AdjacencyMatrix matrix, int column)
        {
            this.matrix = matrix;
            this.word = column / ULongBits;
            this.mask = 1ul << (column % ULongBits);

            if (word >= matrix.ColumnWords)
                ThrowColumnOutOfRangeException(column);
        }

        private void ThrowColumnOutOfRangeException(int column)
        {
            throw new IndexOutOfRangeException(
                $"Column index out of bounds for this matrix ({column} >= {matrix.Columns})"
            );
        }

        private void ThrowRowOutOfRangeException(int row)
        {
            throw new IndexOutOfRangeException(
                $"Row index out of bounds for this matrix ({row} >= {Capacity})"
            );
        }

        public void AndWith(BitSliceY other)
        {
            if (other.Capacity > Capacity)
                throw new ArgumentException("BitSlice instances have different lengths");

            for (int i = 0; i < other.Capacity; ++i)
            {
                var enabled = other[i] ? ulong.MaxValue : 0;
                matrix.Bits[i] &= mask & enabled;
            }
        }

        public void OrWith(BitSliceY other)
        {
            if (other.Capacity > Capacity)
                throw new ArgumentException("BitSlice instances have different lengths");

            for (int i = 0; i < other.Capacity; ++i)
            {
                var enabled = other[i] ? ulong.MaxValue : 0;
                matrix.Bits[i] |= mask & enabled;
            }
        }

        public void Zero()
        {
            for (int y = 0; y < Capacity; ++y)
                matrix.Bits[matrix.ColumnWords * y + word] &= ~mask;
        }

        public readonly Enumerator GetEnumerator()
        {
            return new(this);
        }

        public static bool operator ==(BitSliceY a, BitSliceY b)
        {
            if (a.Capacity != b.Capacity)
                return false;

            for (int i = 0; i < a.Capacity; ++i)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        public static bool operator !=(BitSliceY a, BitSliceY b)
        {
            return !(a == b);
        }

        public ref struct Enumerator(BitSliceY slice) : IEnumerator<int>
        {
            int index = -1;
            readonly BitSliceY slice = slice;

            public readonly int Current => index;
            readonly object IEnumerator.Current => Current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                index += 1;

                for (; index < slice.Capacity; ++index)
                {
                    if (slice[index])
                        return true;
                }

                return false;
            }

            public void Reset()
            {
                index = -1;
            }

            public void Dispose() { }
        }
    }

    internal class BitSliceDebugView
    {
        readonly int[] elements;

        public BitSliceDebugView(BitSliceX slice)
        {
            elements = [.. slice];
        }

        public BitSliceDebugView(BitSliceY slice)
        {
            elements = [.. slice];
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items => elements;

        public override string ToString()
        {
            return $"Count = {elements.Length}";
        }
    }

    internal ref struct BitEnumerator(int start, ulong bits) : IEnumerator<int>
    {
        int index = start - 1;
        ulong bits = bits;

        public readonly int Current => index;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (bits == 0)
                return false;
            index += 1;
            int bit = MathUtil.TrailingZeroCount(bits);

            index += bit;
            if (bit == 63)
                bits = 0;
            else
                bits >>= bit + 1;
            return true;
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public readonly void Dispose() { }

        public readonly BitEnumerator GetEnumerator()
        {
            return this;
        }

        public override string ToString()
        {
            return $"index = {index}, bits = {bits:X}";
        }
    }
}
