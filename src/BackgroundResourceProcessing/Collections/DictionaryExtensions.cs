using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Collections;

public static class KeyValuePairExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static V GetValueOr<K, V>(this IDictionary<K, V> dict, K key, V defaultValue)
    {
        if (dict.TryGetValue(key, out var value))
            return value;
        return defaultValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static V GetValueOrDefault<K, V>(this IDictionary<K, V> dict, K key)
    {
        if (dict.TryGetValue(key, out var value))
            return value;
        return default;
    }

    public static void Add<K, V>(this Dictionary<K, V> dict, KeyValuePair<K, V> pair)
    {
        dict.Add(pair.Key, pair.Value);
    }
}
