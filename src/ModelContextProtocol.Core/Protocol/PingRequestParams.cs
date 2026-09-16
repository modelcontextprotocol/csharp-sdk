namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.Ping"/> request to verify
/// server connectivity.
/// </summary>
/// <remarks>
/// The server responds with a <see cref="PingResult"/>.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class PingRequestParams : RequestParams;