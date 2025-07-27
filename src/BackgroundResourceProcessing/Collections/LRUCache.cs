using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections;

internal class LRUCache<K, V>
{
    readonly int capacity;
    readonly LinkedList<V> entries = [];
    readonly Dictionary<K, LinkedListNode<V>> map;

    public int Count => entries.Count;
    public int Capacity => capacity;

    public LRUCache(int capacity)
    {
        if (capacity == 0)
            throw new ArgumentException("Cannot create an LRU cache with a capacity of 0");

        this.capacity = capacity;
        this.map = new(capacity);
    }

    public void Add(K key, V value)
    {
        if (map.TryGetValue(key, out var node))
        {
            node.Value = value;

            entries.Remove(node);
            entries.AddFirst(node);
        }
        else
        {
            if (entries.Count >= capacity)
                entries.RemoveLast();

            node = entries.AddFirst(value);
            map.Add(key, node);
        }
    }

    public bool Remove(K key)
    {
        if (!map.TryGetValue(key, out var node))
            return false;

        map.Remove(key);
        entries.Remove(node);
        return true;
    }

    public bool TryGetValue(K key, out V value)
    {
        if (!map.TryGetValue(key, out var node))
        {
            value = default;
            return false;
        }

        entries.Remove(node);
        entries.AddFirst(node);

        value = node.Value;
        return true;
    }

    public void RemoveIf(Func<K, V, bool> func)
    {
        List<K> removed = [];
        foreach (var (key, node) in map)
        {
            if (!func(key, node.Value))
                continue;

            removed.Add(key);
            entries.Remove(node);
        }

        foreach (var key in removed)
            map.Remove(key);
    }

    public void Clear()
    {
        map.Clear();
        entries.Clear();
    }
}
