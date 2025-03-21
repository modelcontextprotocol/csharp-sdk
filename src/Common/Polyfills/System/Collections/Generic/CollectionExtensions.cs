using ModelContextProtocol.Utils;

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
}