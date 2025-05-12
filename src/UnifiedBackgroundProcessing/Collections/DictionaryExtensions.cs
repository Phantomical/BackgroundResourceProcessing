using System.Collections.Generic;

namespace UnifiedBackgroundProcessing.Collections
{
    public static class DictionaryExtensions
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
        public static bool TryAdd<K, V>(this Dictionary<K, V> dict, K key, V value)
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

        public static V GetOrInsert<K, V>(this Dictionary<K, V> dict, K key, V insert)
        {
            if (dict.TryGetValue(key, out var value))
                return value;

            dict.Add(key, insert);
            return insert;
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
