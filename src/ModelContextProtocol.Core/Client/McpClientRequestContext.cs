using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides the context for an outgoing client request as it flows through a
/// <see cref="McpClientRequestFilter{TParams, TResult}"/> pipeline.
/// </summary>
/// <typeparam name="TParams">Type of the request parameters specific to each MCP operation.</typeparam>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public sealed class McpClientRequestContext<TParams>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientRequestContext{TParams}"/> class.
    /// </summary>
    /// <param name="client">The client sending the request.</param>
    /// <param name="parameters">The parameters of the request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public McpClientRequestContext(McpClient client, TParams parameters)
    {
        Throw.IfNull(client);

        Client = client;
        Params = parameters;
    }

    /// <summary>Gets the client sending the request.</summary>
    public McpClient Client { get; }

    /// <summary>Gets or sets the parameters of the request.</summary>
    /// <remarks>
    /// Filters can replace or mutate the parameters, for example to redact arguments, before invoking the next handler.
    /// The parameters observed by the innermost handler are the ones sent to the server.
    /// </remarks>
    public TParams Params { get; set; }

    /// <summary>
    /// Gets or sets the tool definition the client knows for the tool being called by a <see cref="RequestMethods.ToolsCall"/> request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The definition, including its <see cref="Protocol.Tool.Annotations"/>, comes from the client's tool cache, which is populated
    /// by <see cref="McpClient.ListToolsAsync(RequestOptions?, CancellationToken)"/> and <see cref="McpClient.AddKnownTools"/>.
    /// It is <see langword="null"/> when the tool is not in that cache and for requests other than <see cref="RequestMethods.ToolsCall"/>.
    /// </para>
    /// <para>
    /// A filter that enforces policy based on this definition should treat <see langword="null"/> as unknown and fail closed.
    /// Annotations are hints supplied by the server; only rely on them for servers you trust.
    /// </para>
    /// </remarks>
    public Tool? Tool { get; set; }
}
