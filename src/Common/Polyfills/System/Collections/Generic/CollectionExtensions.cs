#if !NET
using ModelContextProtocol;

namespace System.Collections.Generic;

internal static class CollectionExtensions
{
    public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key)
    {
        return dictionary.GetValueOrDefault(key, default!);
    }

    public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
    {
        Throw.IfNull(dictionary);

        return dictionary.TryGetValue(key, out TValue? value) ? value : defaultValue;
    }

    public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source) =>
        source.ToDictionary(kv => kv.Key, kv => kv.Value);
}
#endif