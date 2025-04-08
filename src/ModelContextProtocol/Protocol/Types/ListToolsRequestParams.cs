namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Request parameters for listing tools available on the MCP server.
/// This class is used in tool discovery operations to get information about tools the server can provide.
/// </summary>
/// <remarks>
/// <para>
/// This class inherits from <see cref="PaginatedRequestParams"/> which provides cursor-based pagination.
/// When the server has many tools, pagination allows clients to retrieve them in batches.
/// </para>
/// <para>
/// For most client scenarios, you should use the high-level <c>ListToolsAsync</c> method which handles
/// pagination automatically rather than constructing this class directly.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // High-level approach (recommended):
/// var tools = await mcpClient.ListToolsAsync();
/// 
/// // Lower-level approach using the request params directly:
/// var request = new ListToolsRequestParams();
/// // If continuing pagination from a previous request:
/// // request.Cursor = previousResponse.NextCursor;
/// var toolsResult = await client.SendRequestAsync(
///     RequestMethods.ToolsList,
///     request,
///     cancelationToken: cancellationToken);
/// </code>
/// </example>
/// <seealso cref="PaginatedRequestParams"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">MCP Schema Documentation</seealso>
public class ListToolsRequestParams : PaginatedRequestParams;
