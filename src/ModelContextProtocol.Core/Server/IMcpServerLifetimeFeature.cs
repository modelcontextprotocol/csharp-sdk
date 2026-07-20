using System.ComponentModel;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides server-lifetime services used by MCP extension infrastructure.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMcpServerLifetimeFeature
{
    /// <summary>Gets the token that should cancel background work owned by this server.</summary>
    /// <remarks>
    /// The token is <see cref="CancellationToken.None"/> when background work intentionally outlives
    /// the server instance, as it does for per-request servers in stateless HTTP mode.
    /// </remarks>
    CancellationToken BackgroundTaskCancellationToken { get; }

    /// <summary>Registers background work that server disposal must await.</summary>
    /// <param name="backgroundTask">The background work to track.</param>
    /// <remarks>This is a no-op when background work intentionally outlives the server instance.</remarks>
    void RegisterBackgroundTask(Task backgroundTask);
}
