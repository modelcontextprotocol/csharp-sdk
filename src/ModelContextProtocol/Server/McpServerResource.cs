using Microsoft.Extensions.FileProviders;
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a resource that the server supports.
/// </summary>
public abstract class McpServerResource : IMcpServerPrimitive
{
    /// <summary>
    /// The resource instance.
    /// </summary>
    public abstract required Resource ProtocolResource { get; init; }

    /// <inheritdoc />
    public string Name => ProtocolResource.Name;

    /// <summary>
    /// Gets the resource URI.
    /// </summary>
    /// <param name="request">The request context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file info of the resource.</returns>
    public abstract Task<IFileInfo> GetFileInfoAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken = default);
}