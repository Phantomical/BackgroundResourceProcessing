using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.XR;

namespace BackgroundResourceProcessing.Solver;

internal struct VariableMap : IEnumerable<KeyValuePair<int, int>>
{
    readonly int[] data;
    int count = 0;

    public readonly int Count => count;
    public readonly int Capacity => data.Length;

    public int this[int key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get
        {
            static void ThrowKeyNotFoundException(int key) =>
                throw new KeyNotFoundException($"key {key} not present in the map");

            if (data[key] < 0)
                ThrowKeyNotFoundException(key);
            return data[key];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (data[key] < 0)
                count += 1;
            if (value < 0)
                count -= 1;
            data[key] = value;
        }
    }

    public VariableMap(int capacity)
    {
        data = new int[capacity];
        Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryGetValue(int key, out int value)
    {
        value = data[key];
        return value >= 0;
    }

    public void Clear()
    {
        count = 0;
        for (int i = 0; i < data.Length; ++i)
            data[i] = -1;
    }

    public readonly Enumerator GetEnumerator()
    {
        return new(this);
    }

    readonly IEnumerator<KeyValuePair<int, int>> IEnumerable<KeyValuePair<int, int>>.GetEnumerator()
    {
        return GetEnumerator();
    }

    readonly IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct Enumerator(VariableMap map) : IEnumerator<KeyValuePair<int, int>>
    {
        readonly int[] data = map.data;
        int index = -1;

        public readonly KeyValuePair<int, int> Current => new(index, data[index]);
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                index += 1;
                if (index >= data.Length)
                    return false;
                if (data[index] >= 0)
                    return true;
            }
        }

        public readonly void Dispose() { }

        public void Reset()
        {
            index = -1;
        }
    }
}
