namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ResourcesList"/> request from a client to request
/// a list of resources available from the server.
/// </summary>
/// <remarks>
/// The server responds with a <see cref="ListResourcesResult"/> containing the available resources.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class ListResourcesRequestParams : PaginatedRequestParams;
