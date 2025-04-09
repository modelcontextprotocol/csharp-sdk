namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Parameters for a request from the server to get a list of root URIs from the client.
/// </summary>
/// <remarks>
/// This class is used when the server wants to discover what root URIs are available on the client.
/// Root URIs are entry points for resource navigation in the Model Context Protocol.
/// The client responds with a <see cref="ListRootsResult"/> containing the available roots.
/// </remarks>
/// <example>
/// <code>
/// // Server requesting roots from a client
/// var result = await server.RequestRootsAsync(
///     new ListRootsRequestParams(),
///     CancellationToken.None);
///     
/// // Access the roots returned by the client
/// foreach (var root in result.Roots)
/// {
///     Console.WriteLine($"Root URI: {root.Uri}");
/// }
/// </code>
/// </example>
/// <seealso cref="ListRootsResult"/>
/// <seealso cref="RootsCapability"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">Model Context Protocol schema for details</seealso>
public class ListRootsRequestParams : RequestParams;
