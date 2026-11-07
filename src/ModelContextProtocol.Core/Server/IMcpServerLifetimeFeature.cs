using System.ComponentModel;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides server-lifetime services used by MCP extension infrastructure.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMcpServerLifetimeFeature
{
    /// <summary>Gets the token that is cancelled when this server starts disposing.</summary>
    /// <remarks>
    /// The token is <see cref="CancellationToken.None"/> when work intentionally outlives the server,
    /// as it does for per-request servers in stateless HTTP mode.
    /// </remarks>
    CancellationToken ServerCancellationToken { get; }

    /// <summary>Registers an asynchronously disposable resource that server disposal must await.</summary>
    /// <param name="disposable">The resource to dispose when this server is disposed.</param>
    /// <returns>A handle that unregisters the resource without disposing it.</returns>
    /// <remarks>
    /// Dispose the returned handle when the resource completes independently so the server does not
    /// retain it until shutdown. Registration is a no-op when the server does not own the resource.
    /// </remarks>
    IDisposable RegisterForDisposeAsync(IAsyncDisposable disposable);
}
