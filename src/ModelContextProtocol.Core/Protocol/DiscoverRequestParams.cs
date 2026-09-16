namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ServerDiscover"/> request.
/// </summary>
/// <remarks>
/// <para>
/// The discover RPC takes no payload of its own. Per-request metadata
/// (protocol version, client info, client capabilities) flows through the
/// inherited <see cref="RequestParams.Meta"/> property under the
/// <c>io.modelcontextprotocol/*</c> keys defined by the 2026-07-28 protocol revision (SEP-2575).
/// </para>
/// </remarks>
public sealed class DiscoverRequestParams : RequestParams
{
}
