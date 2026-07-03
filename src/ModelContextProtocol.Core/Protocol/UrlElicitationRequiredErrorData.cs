using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the payload for the <c>URL_ELICITATION_REQUIRED</c> JSON-RPC error.
/// </summary>
public sealed class UrlElicitationRequiredErrorData
{
    /// <summary>
    /// Gets or sets the elicitations that must be completed before retrying the original request.
    /// </summary>
    [JsonPropertyName("elicitations")]
    public required IList<ElicitRequestParams> Elicitations { get; set; }
}
