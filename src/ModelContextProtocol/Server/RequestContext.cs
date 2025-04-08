namespace ModelContextProtocol.Server;

/// <summary>
/// A context container that provides access to both the server instance and the client request parameters.
/// Used throughout the Model Context Protocol (MCP) handler system to process client requests.
/// </summary>
/// <typeparam name="TParams">Type of the request parameters specific to each MCP operation</typeparam>
/// <remarks>
/// <para>
/// The <see cref="RequestContext{TParams}"/> encapsulates all contextual information for handling an MCP request.
/// It gives handler implementations access to both:
/// <list type="bullet">
///   <item>The <see cref="Server"/> property - providing access to the server instance and its facilities</item>
///   <item>The <see cref="Params"/> property - containing the client's request parameters</item>
/// </list>
/// </para>
/// <para>
/// This type is typically received as a parameter in handler delegates registered with <see cref="IMcpServerBuilder"/>.
/// </para>
/// <para>
/// Example usage in a tool handler:
/// <code>
/// [McpServerToolType]
/// public class MyTools
/// {
///     [McpServerTool(Name = "operationWithContext"), Description("Example tool using request context")]
///     public static async Task&lt;string&gt; OperationWithContext(
///         RequestContext&lt;CallToolRequestParams&gt; context,
///         string input)
///     {
///         // Access server instance to send notifications
///         await context.Server.SendNotificationAsync("notification/event", new { message = "Processing" });
///         
///         // Access client request metadata from params
///         var progressToken = context.Params?.Meta?.ProgressToken;
///         
///         // Use standard parameter values
///         return $"Processed: {input}";
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public record RequestContext<TParams>(IMcpServer Server, TParams? Params)
{

}
