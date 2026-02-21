using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerResource"/> that delegates all operations to an inner <see cref="McpServerResource"/>.</summary>
/// <remarks>
/// This is recommended as a base type when building resources that can be chained around an underlying <see cref="McpServerResource"/>.
/// The default implementation simply passes each call to the inner resource instance.
/// </remarks>
public abstract class DelegatingMcpServerResource : McpServerResource
{
    private readonly McpServerResource _innerResource;

    /// <summary>Initializes a new instance of the <see cref="DelegatingMcpServerResource"/> class around the specified <paramref name="innerResource"/>.</summary>
    /// <param name="innerResource">The inner resource wrapped by this delegating resource.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerResource"/> is <see langword="null"/>.</exception>
    protected DelegatingMcpServerResource(McpServerResource innerResource)
    {
        Throw.IfNull(innerResource);
        _innerResource = innerResource;
    }

    /// <inheritdoc />
    public override Resource? ProtocolResource => _innerResource.ProtocolResource;

    /// <inheritdoc />
    public override ResourceTemplate ProtocolResourceTemplate => _innerResource.ProtocolResourceTemplate;

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata => _innerResource.Metadata;

    /// <inheritdoc />
    public override bool IsMatch(string uri) => _innerResource.IsMatch(uri);

    /// <inheritdoc />
    public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default) =>
        _innerResource.ReadAsync(request, cancellationToken);

    /// <inheritdoc />
    public override string ToString() => _innerResource.ToString();
}
