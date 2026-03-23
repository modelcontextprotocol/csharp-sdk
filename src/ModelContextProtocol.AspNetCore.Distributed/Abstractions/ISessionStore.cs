// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ModelContextProtocol.AspNetCore.Distributed.Abstractions;

/// <summary>
/// Provides persistence for MCP session ownership.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Gets the current owner of a session, or claims ownership if unclaimed.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ownerInfoFactory">A factory function that creates the owner information if the session is unclaimed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current or newly claimed owner information for the session.</returns>
    Task<SessionOwnerInfo> GetOrClaimOwnershipAsync(
        string sessionId,
        Func<CancellationToken, Task<SessionOwnerInfo>> ownerInfoFactory,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Removes a session from the store.
    /// </summary>
    /// <param name="sessionId">The session identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}
