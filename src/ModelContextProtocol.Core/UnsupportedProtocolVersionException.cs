using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol;

/// <summary>
/// Represents an exception used to signal that a request's declared protocol version is not supported by the server.
/// </summary>
/// <remarks>
/// Introduced by the 2026-07-28 protocol revision (SEP-2575). Servers throw this exception when they cannot process
/// a request because the per-request <c>_meta/io.modelcontextprotocol/protocolVersion</c> (or the equivalent
/// transport-level header) names a version the server does not implement. The exception is converted to a
/// JSON-RPC error response with code <see cref="McpErrorCode.UnsupportedProtocolVersion"/> (<c>-32022</c>) and
/// a <see cref="UnsupportedProtocolVersionErrorData"/> payload.
/// </remarks>
public sealed class UnsupportedProtocolVersionException : McpProtocolException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedProtocolVersionException"/> class.
    /// </summary>
    /// <param name="requested">The protocol version the client requested.</param>
    /// <param name="supported">The protocol versions the server supports.</param>
    /// <param name="message">A human-readable description of the error. If <see langword="null"/>, a default message is used.</param>
    public UnsupportedProtocolVersionException(string requested, IEnumerable<string> supported, string? message = null)
        : base(message ?? $"Unsupported protocol version '{requested}'.", McpErrorCode.UnsupportedProtocolVersion)
    {
        Throw.IfNull(requested);
        Throw.IfNull(supported);

        Requested = requested;
        Supported = new List<string>(supported);
    }

    /// <summary>Gets the protocol version the client requested.</summary>
    public string Requested { get; }

    /// <summary>Gets the protocol versions the server supports.</summary>
    public IReadOnlyList<string> Supported { get; }

    internal JsonNode CreateErrorDataNode()
    {
        var payload = new UnsupportedProtocolVersionErrorData
        {
            Requested = Requested,
            Supported = (IList<string>)Supported,
        };

        return JsonSerializer.SerializeToNode(payload, McpJsonUtilities.JsonContext.Default.UnsupportedProtocolVersionErrorData)!;
    }

    internal static bool TryCreateFromError(
        string formattedMessage,
        JsonRpcErrorDetail detail,
        [NotNullWhen(true)] out UnsupportedProtocolVersionException? exception)
    {
        exception = null;

        if (detail.Data is not JsonElement dataElement || dataElement.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var payload = dataElement.Deserialize(McpJsonUtilities.JsonContext.Default.UnsupportedProtocolVersionErrorData);
        if (payload is null)
        {
            return false;
        }

        exception = new UnsupportedProtocolVersionException(payload.Requested, payload.Supported, formattedMessage);
        return true;
    }
}
