using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents an incomplete result sent by the server to indicate that additional input is needed
/// before the request can be completed.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="IncompleteResult"/> is returned in response to a client-initiated request (such as
/// <see cref="RequestMethods.ToolsCall"/> or <see cref="RequestMethods.PromptsGet"/>) when the server
/// needs the client to fulfill one or more server-initiated requests before it can produce a final result.
/// </para>
/// <para>
/// At least one of <see cref="InputRequests"/> or <see cref="RequestState"/> must be present.
/// </para>
/// <para>
/// This type is part of the Multi Round-Trip Requests (MRTR) mechanism defined in SEP-2322.
/// </para>
/// </remarks>
[Experimental(Experimentals.Mrtr_DiagnosticId, UrlFormat = Experimentals.Mrtr_Url)]
public sealed class IncompleteResult : Result
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IncompleteResult"/> class.
    /// </summary>
    public IncompleteResult()
    {
        ResultType = "incomplete";
    }

    /// <summary>
    /// Gets or sets the server-initiated requests that the client must fulfill before retrying the original request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys are server-assigned identifiers. The client must include a response for each key in the
    /// <see cref="RequestParams.InputResponses"/> map when retrying the original request.
    /// </para>
    /// </remarks>
    [JsonPropertyName("inputRequests")]
    public IDictionary<string, InputRequest>? InputRequests { get; set; }

    /// <summary>
    /// Gets or sets opaque state to be echoed back by the client when retrying the original request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client must treat this as an opaque blob and must not inspect, parse, modify, or make
    /// any assumptions about the contents. If present, the client must include this value in the
    /// <see cref="RequestParams.RequestState"/> property when retrying the original request.
    /// </para>
    /// <para>
    /// Servers may encode request state in any format (e.g., plain JSON, base64-encoded JSON,
    /// encrypted JWT, serialized binary). If the state contains sensitive data, servers should
    /// encrypt it to ensure confidentiality and integrity.
    /// </para>
    /// </remarks>
    [JsonPropertyName("requestState")]
    public string? RequestState { get; set; }
}
