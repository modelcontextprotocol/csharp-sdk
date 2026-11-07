namespace ModelContextProtocol.Server;

/// <summary>Provides a thread-safe collection of <see cref="McpServerResource"/> instances, indexed by their URI templates.</summary>
public sealed class McpServerResourceCollection : McpServerPrimitiveCollection<McpServerResource>
{
    /// <summary>Initializes a new instance of the <see cref="McpServerResourceCollection"/> class.</summary>
    public McpServerResourceCollection()
        : base(UriTemplate.UriTemplateComparer.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerResourceCollection"/> class that contains the same
    /// resources as <paramref name="collection"/>, in the same order.
    /// </summary>
    /// <param name="collection">The collection whose resources are copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The new collection is independent of <paramref name="collection"/>, and copying is inexpensive. For details, see
    /// <see cref="McpServerPrimitiveCollection{T}(McpServerPrimitiveCollection{T})"/>.
    /// </remarks>
    public McpServerResourceCollection(McpServerResourceCollection collection)
        : base(collection)
    {
    }
}