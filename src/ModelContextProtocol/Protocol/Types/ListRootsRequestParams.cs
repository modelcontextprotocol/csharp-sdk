using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ResourcesTemplatesList"/> request from a server to request
/// a list of roots available from the client.
/// </summary>
/// <remarks>
/// The client responds with a <see cref="ListRootsResult"/> containing the client's roots.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public class ListRootsRequestParams : RequestParams;
