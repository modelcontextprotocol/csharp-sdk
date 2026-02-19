using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Provides hooks for persisting and restoring MCP session initialization data,
/// enabling session migration across server instances.
/// </summary>
/// <remarks>
/// <para>
/// When an MCP server is horizontally scaled, stateful sessions are bound to a single process.
/// If that process restarts or scales down, the session is lost. By implementing this interface
/// and registering it with DI, you can persist the initialization handshake data and restore it
/// when a client reconnects to a different server instance with its existing <c>Mcp-Session-Id</c>.
/// </para>
/// <para>
/// This does <strong>not</strong> solve the session-affinity problem for in-flight server-to-client
/// requests (such as sampling or elicitation). Responses to those requests must still be routed to
/// the process that created the request. This interface only enables migration of idle sessions
/// by persisting the data established during the initialization handshake.
/// </para>
/// </remarks>
public interface ISessionMigrationHandler
{
    /// <summary>
    /// Called after a session has been successfully initialized via the MCP initialization handshake.
    /// </summary>
    /// <remarks>
    /// Use this to persist the <paramref name="initializeParams"/> (which includes client capabilities,
    /// client info, and protocol version) to an external store so the session can be migrated to
    /// another server instance later via <see cref="AllowSessionMigrationAsync"/>.
    /// </remarks>
    /// <param name="context">The <see cref="HttpContext"/> for the initialization request.</param>
    /// <param name="sessionId">The unique identifier for the session.</param>
    /// <param name="initializeParams">The initialization parameters sent by the client during the handshake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask OnSessionInitializedAsync(HttpContext context, string sessionId, InitializeRequestParams initializeParams, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a request arrives with an <c>Mcp-Session-Id</c> that the current server doesn't recognize.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Return the original <see cref="InitializeRequestParams"/> to allow the session to be migrated
    /// to this server instance, or <see langword="null"/> to reject the request (returning a 404 to the client).
    /// </para>
    /// <para>
    /// Implementations should validate that the request is authorized, for example by checking
    /// <see cref="HttpContext.User"/>, to ensure the caller is permitted to migrate the session.
    /// </para>
    /// </remarks>
    /// <param name="context">The <see cref="HttpContext"/> for the request with the unrecognized session ID.</param>
    /// <param name="sessionId">The session ID from the request that was not found on this server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The original <see cref="InitializeRequestParams"/> if migration is allowed,
    /// or <see langword="null"/> to reject the request.
    /// </returns>
    ValueTask<InitializeRequestParams?> AllowSessionMigrationAsync(HttpContext context, string sessionId, CancellationToken cancellationToken);
}
