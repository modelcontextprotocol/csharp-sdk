namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for result payloads that support cursor-based pagination.
/// </summary>
/// <remarks>
/// <para>
/// Pagination allows API responses to be broken into smaller, manageable chunks when
/// there are potentially many results to return or when dynamically computed results
/// might incur measurable latency.
/// </para>
/// <para>
/// Classes that inherit from <see cref="PaginatedResult"/> implement cursor-based pagination,
/// where the <see cref="NextCursor"/> property serves as an opaque token pointing to the next
/// set of results.
/// </para>
/// </remarks>
public abstract class PaginatedResult : Result
{
    private protected PaginatedResult()
    {
    }

    /// <summary>
    /// Gets or sets an opaque token representing the pagination position after the last returned result.
    /// </summary>
    /// <remarks>
    /// When a paginated result has more data available, the <see cref="NextCursor"/>
    /// property will contain a non-<see langword="null"/> token that can be used in subsequent requests
    /// to fetch the next page. When there are no more results to return, the <see cref="NextCursor"/> property
    /// will be <see langword="null"/>.
    /// </remarks>
    public string? NextCursor { get; set; }
}
