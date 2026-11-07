using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Extracts the values that parameters of an AIFunction-based prompt or resource are restricted to, from the enum values
/// in the function's JSON schema, for use as completions.
/// </summary>
internal static class AllowedValueCompletions
{
    /// <summary>Marks a cache whose function was found to have no restricted parameters.</summary>
    private static readonly object s_none = new();

    /// <summary>
    /// Gets the parameter name → allowed values lookup for <paramref name="schema"/>, computing and caching it in
    /// <paramref name="cache"/> on first use. The returned dictionary is shared and must not be modified.
    /// </summary>
    /// <remarks>
    /// The schema of an AIFunction is immutable, so the result can be computed once per primitive rather than once per
    /// session. Concurrent first calls may each compute it; the results are equivalent.
    /// </remarks>
    public static Dictionary<string, string[]>? GetOrCreate(ref object? cache, JsonElement schema)
    {
        object? cached = Volatile.Read(ref cache);
        if (cached is null)
        {
            cached = (object?)Create(schema) ?? s_none;
            Volatile.Write(ref cache, cached);
        }

        return cached as Dictionary<string, string[]>;
    }

    private static Dictionary<string, string[]>? Create(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out JsonElement properties) ||
            properties.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, string[]>? paramValues = null;
        foreach (var param in properties.EnumerateObject())
        {
            if (param.Value.TryGetProperty("enum", out JsonElement enumValues) &&
                enumValues.ValueKind is JsonValueKind.Array)
            {
                List<string>? values = null;
                foreach (var item in enumValues.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.String && item.GetString() is { } str)
                    {
                        values ??= [];
                        values.Add(str);
                    }
                }

                if (values is not null)
                {
                    paramValues ??= new(StringComparer.Ordinal);
                    paramValues[param.Name] = [.. values];
                }
            }
        }

        return paramValues;
    }
}
