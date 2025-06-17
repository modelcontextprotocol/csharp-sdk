using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the tools capability configuration.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </summary>
public sealed class ToolsCapability
{
    /// <summary>
    /// Gets or sets whether this server supports notifications for changes to the tool list.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the server will send notifications using 
    /// <see cref="NotificationMethods.ToolListChangedNotification"/> when tools are added, 
    /// removed, or modified. Clients can register handlers for these notifications to
    /// refresh their tool cache. This capability enables clients to stay synchronized with server-side 
    /// changes to available tools.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsList"/> requests.
    /// </summary>
    /// <remarks>
    /// The handler should return a list of available tools when requested by a client.
    /// It supports pagination through the cursor mechanism, where the client can make
    /// repeated calls with the cursor returned by the previous call to retrieve more tools.
    /// When used in conjunction with <see cref="ToolCollection"/>, both the tools from this handler
    /// and the tools from the collection will be combined to form the complete list of available tools.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<ListToolsRequestParams>, CancellationToken, ValueTask<ListToolsResult>>? ListToolsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.ToolsCall"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client makes a call to a tool that isn't found in the <see cref="ToolCollection"/>.
    /// The handler should implement logic to execute the requested tool and return appropriate results. 
    /// It receives a <see cref="RequestContext{CallToolRequestParams}"/> containing information about the tool 
    /// being called and its arguments, and should return a <see cref="CallToolResult"/> with the execution results.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>? CallToolHandler { get; set; }

    /// <summary>
    /// Gets or sets a collection of tools served by the server.
    /// </summary>
    /// <remarks>
    /// Tools will specified via <see cref="ToolCollection"/> augment the <see cref="ListToolsHandler"/> and
    /// <see cref="CallToolHandler"/>, if provided. ListTools requests will output information about every tool
    /// in <see cref="ToolCollection"/> and then also any tools output by <see cref="ListToolsHandler"/>, if it's
    /// non-<see langword="null"/>. CallTool requests will first check <see cref="ToolCollection"/> for the tool
    /// being requested, and if the tool is not found in the <see cref="ToolCollection"/>, any specified <see cref="CallToolHandler"/>
    /// will be invoked as a fallback.
    /// </remarks>
    [JsonIgnore]
    public McpServerPrimitiveCollection<McpServerTool>? ToolCollection { get; set; }
}