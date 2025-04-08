namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to request a list of prompts and prompt templates the server has.
/// Supports cursor-based pagination inherited from <see cref="PaginatedRequestParams"/>.
/// </summary>
/// <remarks>
/// <para>
/// This request is used to discover available prompts on the server that the client can use.
/// The server responds with a <see cref="ListPromptsResult"/> containing the available prompts.
/// </para>
/// <para>
/// Prompts can be used as templates for generating text with the large language model,
/// providing consistent formatting and behavior for specific types of requests.
/// </para>
/// <para>
/// After listing prompts, clients can use <see cref="GetPromptRequestParams"/> to fetch
/// details of a specific prompt by name.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Basic request to list all available prompts
/// var request = new ListPromptsRequestParams();
/// 
/// // Request with pagination 
/// var paginatedRequest = new ListPromptsRequestParams
/// {
///     Cursor = "previousPageCursor",
///     Limit = 10
/// };
/// 
/// var result = await client.SendRequestAsync&lt;ListPromptsRequestParams, ListPromptsResult&gt;(
///     RequestMethods.ListPrompts,
///     paginatedRequest,
///     cancellationToken
/// );
/// </code>
/// </example>
/// <seealso cref="ListPromptsResult"/>
/// <seealso cref="GetPromptRequestParams"/>
/// <seealso cref="PaginatedRequestParams"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</seealso>
public class ListPromptsRequestParams : PaginatedRequestParams;
