using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Server;

/// <summary>
/// Represents a resource that the server supports.
/// </summary>
public class McpServerResource : IMcpServerPrimitive
{
    /// <summary>
    /// The resource instance.
    /// </summary>
    public required Resource ProtocolResource { get; init; }

    /// <inheritdoc />
    public string Name => ProtocolResource.Name;
}