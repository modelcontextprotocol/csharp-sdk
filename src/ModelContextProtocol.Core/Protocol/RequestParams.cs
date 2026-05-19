using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for all request parameters.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public abstract class RequestParams
{
    /// <summary>Prevent external derivations.</summary>
    private protected RequestParams()
    {
    }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Gets or sets the responses to server-initiated input requests from a previous <see cref="IncompleteResult"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is populated when retrying a request after receiving an <see cref="IncompleteResult"/>.
    /// Each key corresponds to a key from the <see cref="IncompleteResult.InputRequests"/> map, and
    /// the value is the client's response to that input request.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Mrtr_DiagnosticId, UrlFormat = Experimentals.Mrtr_Url)]
    [JsonIgnore]
    public IDictionary<string, InputResponse>? InputResponses
    {
        get => InputResponsesCore;
        set => InputResponsesCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("inputResponses")]
    internal IDictionary<string, InputResponse>? InputResponsesCore { get; set; }

    /// <summary>
    /// Gets or sets opaque request state echoed back from a previous <see cref="IncompleteResult"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is populated when retrying a request after receiving an <see cref="IncompleteResult"/>
    /// that included a <see cref="IncompleteResult.RequestState"/> value. The client must echo back the
    /// exact value without modification.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Mrtr_DiagnosticId, UrlFormat = Experimentals.Mrtr_Url)]
    [JsonIgnore]
    public string? RequestState
    {
        get => RequestStateCore;
        set => RequestStateCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("requestState")]
    internal string? RequestStateCore { get; set; }

    /// <summary>
    /// Gets the opaque token that will be attached to any subsequent progress notifications.
    /// </summary>
    [JsonIgnore]
    public ProgressToken? ProgressToken
    {
        get
        {
            if (Meta?["progressToken"] is JsonValue progressToken)
            {
                if (progressToken.TryGetValue(out string? stringValue))
                {
                    return new ProgressToken(stringValue);
                }

                if (progressToken.TryGetValue(out long longValue))
                {
                    return new ProgressToken(longValue);
                }
            }

            return null;
        }
    }
}
