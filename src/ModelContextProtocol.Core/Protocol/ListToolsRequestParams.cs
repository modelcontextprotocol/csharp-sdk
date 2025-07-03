namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ToolsList"/> request from a client to request
/// a list of tools available from the server.
/// </summary>
/// <remarks>
/// The server responds with a <see cref="ListToolsResult"/> containing the available tools.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class ListToolsRequestParams : PaginatedRequestParams;
