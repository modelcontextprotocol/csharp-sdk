using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

#pragma warning disable MCPEXP002 // this collection is itself part of the experimental ResultOrAlternate seam

/// <summary>
/// Stores alternate-result filters keyed by JSON-RPC method name, so that augmenting a Core-dispatched
/// method to return an alternate <see cref="Result"/> subtype does not require a new
/// <c>&lt;Method&gt;WithAlternateHandler</c>/<c>&lt;Method&gt;WithAlternateFilters</c> pair per method.
/// </summary>
/// <remarks>
/// <para>
/// Each method may only be registered with a single (<c>TParams</c>, <c>TResult</c>) pair, matching the
/// types Core dispatches that method with. Registering the same method with different types throws, because
/// the mismatched filters could never be invoked.
/// </para>
/// <para>
/// <see cref="McpRequestFilters.CallToolWithAlternateFilters"/> is a typed view over the
/// <see cref="RequestMethods.ToolsCall"/> entry of this collection.
/// </para>
/// </remarks>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public sealed class AlternateResultFilterCollection
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the filter list registered for <paramref name="method"/>, creating an empty one if the method
    /// has not been registered yet.
    /// </summary>
    /// <typeparam name="TParams">The request parameter type Core dispatches <paramref name="method"/> with.</typeparam>
    /// <typeparam name="TResult">The result type Core dispatches <paramref name="method"/> with.</typeparam>
    /// <param name="method">The JSON-RPC method name, for example <see cref="RequestMethods.PromptsGet"/>.</param>
    /// <returns>The mutable filter list for <paramref name="method"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="method"/> was already registered with a different parameter or result type.
    /// </exception>
    public IList<McpRequestInvocationFilter<TParams, ResultOrAlternate<TResult>>> GetOrAdd<TParams, TResult>(string method)
        where TResult : Result
    {
        Throw.IfNullOrWhiteSpace(method);

        if (_entries.TryGetValue(method, out var entry))
        {
            return Cast<TParams, TResult>(entry, method);
        }

        var filters = new List<McpRequestInvocationFilter<TParams, ResultOrAlternate<TResult>>>();
        _entries[method] = new Entry(typeof(TParams), typeof(TResult), filters, () => filters.Count);
        return filters;
    }

    /// <summary>
    /// Replaces the filter list registered for <paramref name="method"/>.
    /// </summary>
    /// <typeparam name="TParams">The request parameter type Core dispatches <paramref name="method"/> with.</typeparam>
    /// <typeparam name="TResult">The result type Core dispatches <paramref name="method"/> with.</typeparam>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="filters">The filter list to store.</param>
    public void Set<TParams, TResult>(
        string method,
        IList<McpRequestInvocationFilter<TParams, ResultOrAlternate<TResult>>> filters)
        where TResult : Result
    {
        Throw.IfNullOrWhiteSpace(method);
        Throw.IfNull(filters);

        _entries[method] = new Entry(typeof(TParams), typeof(TResult), filters, () => filters.Count);
    }

    /// <summary>
    /// Gets the method names that currently have at least one registered filter.
    /// </summary>
    internal IEnumerable<string> NonEmptyMethods
    {
        get
        {
            foreach (var entry in _entries)
            {
                if (entry.Value.Count > 0)
                {
                    yield return entry.Key;
                }
            }
        }
    }

    internal bool TryGetNonEmpty<TParams, TResult>(
        string method,
        [NotNullWhen(true)] out IList<McpRequestInvocationFilter<TParams, ResultOrAlternate<TResult>>>? filters)
        where TResult : Result
    {
        if (_entries.TryGetValue(method, out var entry) && entry.Count > 0)
        {
            filters = Cast<TParams, TResult>(entry, method);
            return true;
        }

        filters = null;
        return false;
    }

    private static IList<McpRequestInvocationFilter<TParams, ResultOrAlternate<TResult>>> Cast<TParams, TResult>(
        Entry entry,
        string method)
        where TResult : Result
    {
        if (entry.Filters is IList<McpRequestInvocationFilter<TParams, ResultOrAlternate<TResult>>> typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Alternate-result filters for method '{method}' were registered with " +
            $"({entry.ParamsType}, {entry.ResultType}), but ({typeof(TParams)}, {typeof(TResult)}) was requested. " +
            $"A method can only carry alternate-result filters for the types the server dispatches it with.");
    }

    private sealed class Entry(Type paramsType, Type resultType, object filters, Func<int> count)
    {
        public Type ParamsType { get; } = paramsType;

        public Type ResultType { get; } = resultType;

        public object Filters { get; } = filters;

        public int Count => count();
    }
}
