using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections;

internal static class KeyValuePairExt
{
    public static void Deconstruct<K, V>(this KeyValuePair<K, V> pair, out K key, out V value)
    {
        key = pair.Key;
        value = pair.Value;
    }
}

internal static class DictionaryExtensions
{
    /// <summary>
    /// This was added in .NET standard 2.1 but KSP does not have that available.
    /// </summary>
    /// <typeparam name="K"></typeparam>
    /// <typeparam name="V"></typeparam>
    /// <param name="dict"></param>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool TryAddExt<K, V>(this Dictionary<K, V> dict, K key, V value)
    {
        if (dict.ContainsKey(key))
            return false;
        dict.Add(key, value);
        return true;
    }

    public static V GetValueOr<K, V>(this Dictionary<K, V> dict, K key, V defaultValue)
    {
        if (dict.TryGetValue(key, out var value))
            return value;
        return defaultValue;
    }

    public static V GetOrAdd<K, V>(this Dictionary<K, V> dict, K key, V insert)
    {
        if (dict.TryGetValue(key, out var value))
            return value;

        dict.Add(key, insert);
        return insert;
    }

    public static V GetOrAdd<K, V>(this Dictionary<K, V> dict, K key, Func<V> func)
    {
        if (dict.TryGetValue(key, out var value))
            return value;

        value = func();
        dict.Add(key, value);
        return value;
    }

    public static void Add<K, V>(this Dictionary<K, V> dict, KeyValuePair<K, V> pair)
    {
        dict.Add(pair.Key, pair.Value);
    }

    public static bool KeysEqual<K, V1, V2>(this Dictionary<K, V1> dict, Dictionary<K, V2> other)
    {
        if (dict.Count != other.Count)
            return false;

        foreach (var key in dict.Keys)
            if (!other.ContainsKey(key))
                return false;

        return true;
    }

    public static bool DictEqual<K, V>(Dictionary<K, V> a, Dictionary<K, V> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (key, aVal) in a)
        {
            if (!b.TryGetValue(key, out var bVal))
                return false;

            if (!aVal.Equals(bVal))
                return false;
        }

        return true;
    }
}

internal static class DictUtil
{
    internal static KeyValuePair<K, V> CreateKeyValuePair<K, V>(K key, V value)
    {
        return new KeyValuePair<K, V>(key, value);
    }
}
