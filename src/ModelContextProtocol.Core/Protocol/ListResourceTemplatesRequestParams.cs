namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ResourcesTemplatesList"/> request from a client to request
/// a list of resource templates available from the server.
/// </summary>
/// <remarks>
/// The server responds with a <see cref="ListResourceTemplatesResult"/> containing the available resource templates.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class ListResourceTemplatesRequestParams : PaginatedRequestParams;