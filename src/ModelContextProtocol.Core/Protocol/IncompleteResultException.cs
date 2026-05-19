using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// The exception that is thrown by a server handler to return an <see cref="Protocol.IncompleteResult"/>
/// to the client, signaling that additional input is needed before the request can be completed.
/// </summary>
/// <remarks>
/// <para>
/// This exception is part of the low-level Multi Round-Trip Requests (MRTR) API. Tool handlers
/// throw this exception to directly control the incomplete result payload, including
/// <see cref="Protocol.IncompleteResult.InputRequests"/> and <see cref="Protocol.IncompleteResult.RequestState"/>.
/// </para>
/// <para>
/// For stateless servers, this enables multi-round-trip flows without requiring the handler to stay
/// alive between round trips. The server encodes its state in <see cref="Protocol.IncompleteResult.RequestState"/>
/// and receives it back on retry via <see cref="RequestParams.RequestState"/>.
/// </para>
/// <para>
/// To return a <c>requestState</c>-only response (e.g., for load shedding), omit
/// <see cref="Protocol.IncompleteResult.InputRequests"/> and set only <see cref="Protocol.IncompleteResult.RequestState"/>.
/// The client will retry the request with the state echoed back.
/// </para>
/// <para>
/// This exception can only be used when MRTR is supported by the client. Check
/// <see cref="Server.McpServer.IsMrtrSupported"/> before throwing. If thrown when MRTR is not
/// supported, the exception will propagate as a JSON-RPC internal error.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [McpServerTool, Description("A stateless tool using low-level MRTR")]
/// public static string MyTool(McpServer server, RequestContext&lt;CallToolRequestParams&gt; context)
/// {
///     if (context.Params.RequestState is { } state)
///     {
///         // Retry: process accumulated state and input responses
///         var responses = context.Params.InputResponses;
///         return "Final result";
///     }
///
///     if (!server.IsMrtrSupported)
///     {
///         return "This tool requires MRTR support.";
///     }
///
///     throw new IncompleteResultException(
///         inputRequests: new Dictionary&lt;string, InputRequest&gt;
///         {
///             ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams { ... })
///         },
///         requestState: "encoded-state");
/// }
/// </code>
/// </example>
[Experimental(Experimentals.Mrtr_DiagnosticId, UrlFormat = Experimentals.Mrtr_Url)]
public class IncompleteResultException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IncompleteResultException"/> class
    /// with the specified <see cref="Protocol.IncompleteResult"/>.
    /// </summary>
    /// <param name="incompleteResult">The incomplete result to return to the client.</param>
    public IncompleteResultException(IncompleteResult incompleteResult)
        : base("The server returned an incomplete result requiring additional client input.")
    {
        Throw.IfNull(incompleteResult);
        IncompleteResult = incompleteResult;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IncompleteResultException"/> class
    /// with the specified input requests and/or request state.
    /// </summary>
    /// <param name="inputRequests">
    /// Server-initiated requests that the client must fulfill before retrying.
    /// Keys are server-assigned identifiers.
    /// </param>
    /// <param name="requestState">
    /// Opaque state to be echoed back by the client when retrying. The client must
    /// treat this as an opaque blob and must not inspect or modify it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Both <paramref name="inputRequests"/> and <paramref name="requestState"/> are <see langword="null"/>.
    /// At least one must be provided.
    /// </exception>
    public IncompleteResultException(
        IDictionary<string, InputRequest>? inputRequests = null,
        string? requestState = null)
        : base("The server returned an incomplete result requiring additional client input.")
    {
        if (inputRequests is null && requestState is null)
        {
            throw new ArgumentException("At least one of inputRequests or requestState must be provided.");
        }

        IncompleteResult = new IncompleteResult
        {
            InputRequests = inputRequests,
            RequestState = requestState,
        };
    }

    /// <summary>
    /// Gets the incomplete result to return to the client.
    /// </summary>
    public IncompleteResult IncompleteResult { get; }
}
