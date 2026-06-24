using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol;

/// <summary>
/// Represents an exception used to signal that a request requires a client capability that was not declared
/// in the request's per-request <c>_meta/io.modelcontextprotocol/clientCapabilities</c> field.
/// </summary>
/// <remarks>
/// Introduced by the 2026-07-28 protocol revision (SEP-2575). Servers throw this exception when a handler cannot
/// proceed because the client did not declare a required capability for the request. The exception is converted
/// to a JSON-RPC error response with code <see cref="McpErrorCode.MissingRequiredClientCapability"/> (<c>-32021</c>)
/// and a <see cref="MissingRequiredClientCapabilityErrorData"/> payload.
/// </remarks>
public sealed class MissingRequiredClientCapabilityException : McpProtocolException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MissingRequiredClientCapabilityException"/> class.
    /// </summary>
    /// <param name="requiredCapabilities">The capabilities the server requires for the request.</param>
    /// <param name="message">A human-readable description of the error. If <see langword="null"/>, a default message is used.</param>
    public MissingRequiredClientCapabilityException(ClientCapabilities requiredCapabilities, string? message = null)
        : base(message ?? "The request requires client capabilities that were not declared in _meta/clientCapabilities.",
               McpErrorCode.MissingRequiredClientCapability)
    {
        Throw.IfNull(requiredCapabilities);
        RequiredCapabilities = requiredCapabilities;
    }

    /// <summary>Gets the client capabilities required for the request.</summary>
    public ClientCapabilities RequiredCapabilities { get; }

    internal JsonNode CreateErrorDataNode()
    {
        var payload = new MissingRequiredClientCapabilityErrorData
        {
            RequiredCapabilities = RequiredCapabilities,
        };

        return JsonSerializer.SerializeToNode(payload, McpJsonUtilities.JsonContext.Default.MissingRequiredClientCapabilityErrorData)!;
    }

    internal static bool TryCreateFromError(
        string formattedMessage,
        JsonRpcErrorDetail detail,
        [NotNullWhen(true)] out MissingRequiredClientCapabilityException? exception)
    {
        exception = null;

        if (detail.Data is not JsonElement dataElement || dataElement.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var payload = dataElement.Deserialize(McpJsonUtilities.JsonContext.Default.MissingRequiredClientCapabilityErrorData);
        if (payload?.RequiredCapabilities is null)
        {
            return false;
        }

        exception = new MissingRequiredClientCapabilityException(payload.RequiredCapabilities, formattedMessage);
        return true;
    }
}
