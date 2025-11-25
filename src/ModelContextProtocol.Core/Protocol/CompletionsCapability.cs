using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the completions capability for providing auto-completion suggestions
/// for prompt arguments and resource references.
/// </summary>
/// <remarks>
/// <para>
/// When enabled, this capability allows a Model Context Protocol server to provide
/// auto-completion suggestions. This capability is advertised to clients during the initialize handshake.
/// </para>
/// <para>
/// The primary function of this capability is to improve the user experience by offering
/// contextual suggestions for argument values or resource identifiers based on partial input.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// <para>
/// This class is intentionally empty as the Model Context Protocol specification does not
/// currently define additional properties for sampling capabilities. Future versions of the
/// specification might extend this capability with additional configuration options.
/// </para>
/// </remarks>
public sealed class CompletionsCapability
{
    /// <summary>
    /// Gets or sets the handler for completion requests.
    /// </summary>
    /// <remarks>
    /// This handler provides auto-completion suggestions for prompt arguments or resource references in the Model Context Protocol.
    /// The handler receives a reference type (e.g., "ref/prompt" or "ref/resource") and the current argument value,
    /// and should return appropriate completion suggestions.
    /// </remarks>
    [JsonIgnore]
    [Obsolete($"Use {nameof(McpServerOptions.Handlers.CompleteHandler)} instead. This member will be removed in a subsequent release.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    [EditorBrowsable(EditorBrowsableState.Never)]
    public McpRequestHandler<CompleteRequestParams, CompleteResult>? CompleteHandler { get; set; }
}
