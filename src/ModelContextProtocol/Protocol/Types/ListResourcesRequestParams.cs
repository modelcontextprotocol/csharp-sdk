namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Parameters for a request to list resources available on the server.
/// This is sent from the client to request a list of resources the server has.
/// </summary>
/// <remarks>
/// This class inherits from <see cref="PaginatedRequestParams"/>, which means it supports cursor-based
/// pagination through the <see cref="PaginatedRequestParams.Cursor"/> property. The server responds with
/// a <see cref="ListResourcesResult"/> containing available resources and pagination information.
/// </remarks>
/// <example>
/// <code>
/// // Simple request for resources (first page only)
/// var result = await client.SendRequestAsync(
///     RequestMethods.ResourcesList,
///     new ListResourcesRequestParams(),
///     McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
///     McpJsonUtilities.JsonContext.Default.ListResourcesResult);
/// 
/// // Get all resources with pagination (using the extension method)
/// var allResources = await client.ListResourcesAsync();
/// 
/// // Manually implement pagination
/// var params = new ListResourcesRequestParams { Cursor = "previousPageCursor" };
/// var nextPage = await client.SendRequestAsync(
///     RequestMethods.ResourcesList,
///     params,
///     McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
///     McpJsonUtilities.JsonContext.Default.ListResourcesResult);
/// </code>
/// </example>
/// <seealso cref="ListResourcesResult"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">Model Context Protocol schema for details</seealso>
public class ListResourcesRequestParams : PaginatedRequestParams;
