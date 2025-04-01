namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to request a list of prompts and prompt templates the server has.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ListPromptsRequestParams : PaginatedRequestParams;
