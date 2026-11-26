using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides grouped request-specific filter collections for outgoing client requests.
/// </summary>
[Experimental(Experimentals.Extensibility_DiagnosticId, UrlFormat = Experimentals.Extensibility_Url)]
public sealed class McpClientRequestFilters
{
    /// <summary>
    /// Gets or sets the filters for the <see cref="RequestMethods.ToolsCall"/> pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These filters wrap every tool call made through <see cref="McpClient.CallToolAsync(CallToolRequestParams, CancellationToken)"/>,
    /// which all other <c>CallToolAsync</c> overloads, <see cref="McpClientTool.CallAsync"/>, and <see cref="McpClientTool"/>
    /// invocations through an <c>IChatClient</c> route through. A filter can inspect or modify the request, return a
    /// <see cref="CallToolResult"/> without calling the next handler to block the call, or post-process the result.
    /// </para>
    /// <para>
    /// To block a call, prefer returning a <see cref="CallToolResult"/> with <see cref="CallToolResult.IsError"/> set to
    /// <see langword="true"/> over throwing: the result's content reaches the model, while a thrown exception's message is
    /// typically hidden from it.
    /// </para>
    /// <para>
    /// Filters run in the order they were added: the first filter is the outermost. The pipeline is built when the client
    /// is created, so changes to this list after that point are not observed. Requests sent directly with
    /// <see cref="McpSession.SendRequestAsync(JsonRpcRequest, CancellationToken)"/> bypass these filters.
    /// </para>
    /// </remarks>
    public IList<McpClientRequestFilter<CallToolRequestParams, CallToolResult>> CallToolFilters
    {
        get => field ??= [];
        set
        {
            Throw.IfNull(value);
            field = value;
        }
    }
}
