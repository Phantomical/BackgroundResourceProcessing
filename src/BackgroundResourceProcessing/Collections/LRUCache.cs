using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections;

internal class LRUCache<K, V>
{
    readonly int capacity;
    readonly LinkedList<KeyValuePair<K, V>> entries = [];
    readonly Dictionary<K, LinkedListNode<KeyValuePair<K, V>>> map;

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
            node.Value = new(key, value);

            entries.Remove(node);
            entries.AddFirst(node);
        }
        else
        {
            if (entries.Count >= capacity)
            {
                var last = entries.Last;
                map.Remove(last.Value.Key);
                entries.RemoveLast();
            }

            map.Add(key, entries.AddFirst(new KeyValuePair<K, V>(key, value)));
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

        value = node.Value.Value;
        return true;
    }

    public void RemoveIf(Func<K, V, bool> func)
    {
        for (var node = entries.First; node != null; node = node.Next)
        {
            var (key, value) = node.Value;
            if (!func(key, value))
                continue;

            map.Remove(key);
            entries.Remove(node);
        }
    }

    public void Clear()
    {
        map.Clear();
        entries.Clear();
    }
}
