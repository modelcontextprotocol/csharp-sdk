using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

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
/// </remarks>
public class CompletionsCapability
{
    // Currently empty in the spec, but may be extended in the future.

    /// <summary>
    /// Gets or sets the handler for completion requests.
    /// </summary>
    /// <remarks>
    /// This handler provides auto-completion suggestions for prompt arguments or resource references in the Model Context Protocol.
    /// The handler receives a reference type (e.g., "ref/prompt" or "ref/resource") and the current argument value,
    /// and should return appropriate completion suggestions.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>>? CompleteHandler { get; set; }
}