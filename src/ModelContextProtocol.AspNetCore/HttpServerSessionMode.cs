using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Specifies how the Streamable HTTP transport tracks state between requests.
/// </summary>
/// <remarks>
/// Starting with the <c>2026-07-28</c> protocol revision, Streamable HTTP no longer supports sessions
/// (SEP-2567 removed <c>Mcp-Session-Id</c>, and SEP-2575 removed the <c>initialize</c> handshake), so requests
/// using that revision or later can only ever be served statelessly. This enumeration allows specification for
/// how the server reconciles that requirement with clients that still rely on the <c>initialize</c> handshake.
/// </remarks>
public enum HttpServerSessionMode
{
    /// <summary>
    /// The server never tracks state between requests, allowing for load balancing without session affinity.
    /// </summary>
    /// <remarks>
    /// <see cref="McpSession.SessionId"/> is <see langword="null"/>, the
    /// <c>Mcp-Session-Id</c> header is unused, <see cref="HttpServerTransportOptions.RunSessionHandler"/> and
    /// <see cref="HttpServerTransportOptions.ConfigureSessionOptions"/> are invoked once per request, and the
    /// GET, DELETE, and <c>/sse</c> endpoints are unavailable. Unsolicited server-to-client messages and all
    /// server-to-client requests are unsupported because any response might arrive at another ASP.NET Core
    /// application process. Client sampling, elicitation, and roots capabilities are disabled because the
    /// server cannot make requests; use <see href="https://csharp.sdk.modelcontextprotocol.io/v2/concepts/mrtr/mrtr.html">Multi Round-Trip Requests (MRTR)</see>
    /// instead.
    /// </remarks>
    Stateless,

    /// <summary>
    /// The server tracks a long-lived session for every client, which requires session affinity.
    /// </summary>
    /// <remarks>
    /// Requests using the <c>2026-07-28</c> or later protocol revision are refused with a
    /// <c>-32022 UnsupportedProtocolVersion</c> error so that a dual-path client downgrades to the
    /// <c>initialize</c> handshake and obtains the session the server was configured to provide. Use
    /// <see cref="StatefulForInitializeClients"/> to serve those clients natively instead of forcing a downgrade.
    /// </remarks>
    Stateful,

    /// <summary>
    /// The server tracks a long-lived session for clients that use the <c>initialize</c> handshake and serves
    /// clients using the <c>2026-07-28</c> or later protocol revision statelessly on the same endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This hybrid mode allows an application to adopt the latest protocol revision progressively rather than
    /// waiting for every client to migrate. Clients using the <c>2025-11-25</c> or earlier revisions get a full
    /// stateful session with an <c>Mcp-Session-Id</c> and continue to use the GET and DELETE endpoints, while
    /// clients using the <c>2026-07-28</c> or later revisions are served per request with no session ID minted
    /// or echoed, and receive <c>405 Method Not Allowed</c> for GET and DELETE, exactly as in
    /// <see cref="Stateless"/> mode.
    /// </para>
    /// <para>
    /// Because a <c>2026-07-28</c> request has no session, the session-only features listed on
    /// <see cref="Stateless"/> remain unavailable to those clients even though other clients on the same
    /// endpoint have sessions. <see cref="HttpServerTransportOptions.ConfigureSessionOptions"/> is invoked once
    /// per session for <c>initialize</c>-handshake clients and once per request for <c>2026-07-28</c> and later
    /// clients.
    /// </para>
    /// </remarks>
    StatefulForInitializeClients,
}
