using System.Collections.Generic;
using System.Linq;

namespace BackgroundResourceProcessing.Collections
{
    internal struct KVPair<K, V>(K key, V value)
    {
        public K Key = key;
        public V Value = value;

        public readonly void Deconstruct(out K key, out V value)
        {
            key = Key;
            value = Value;
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

        public static void Add<K, V>(this Dictionary<K, V> dict, KeyValuePair<K, V> pair)
        {
            dict.Add(pair.Key, pair.Value);
        }

        /// <summary>
        /// <see cref="KeyValuePair.Deconstruct"/> is not available in the
        /// assemblies shipped with KSP, so this returns an iterator with a pair
        /// type that does support deconstruction.
        /// </summary>
        /// <typeparam name="K"></typeparam>
        /// <typeparam name="V"></typeparam>
        /// <param name="dict"></param>
        /// <returns></returns>
        public static IEnumerable<KVPair<K, V>> KSPEnumerate<K, V>(this Dictionary<K, V> dict)
        {
            return dict.Select(pair => new KVPair<K, V>(pair.Key, pair.Value));
        }
    }

    internal static class DictUtil
    {
        internal static KeyValuePair<K, V> CreateKeyValuePair<K, V>(K key, V value)
        {
            return new KeyValuePair<K, V>(key, value);
        }
    }
}
