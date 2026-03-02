// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.AspNetCore.Distributed;

/// <summary>
/// Defines semantic logging event IDs for the session state management module.
/// These IDs enable filtering, categorization, and structured logging across all session-related operations.
/// Uses a high base number (50000) to avoid conflicts with other libraries.
/// </summary>
internal enum LogEventId
{
    /// <summary>
    /// Resolving the owner of an existing session. Event ID: 50001
    /// </summary>
    ResolvingSessionOwner = 50001,

    /// <summary>
    /// A new session has been claimed by an owner. Event ID: 50002
    /// </summary>
    SessionClaimed = 50002,

    /// <summary>
    /// Session is already owned by another server instance. Event ID: 50003
    /// </summary>
    SessionOwnedByOther = 50003,

    /// <summary>
    /// Failed to deserialize session owner information from cache. Event ID: 50004
    /// </summary>
    FailedToDeserializeSessionOwner = 50004,

    /// <summary>
    /// Session has been established on the current host. Event ID: 50100
    /// </summary>
    SessionEstablished = 50100,

    /// <summary>
    /// Forwarding a request to another server that owns the session. Event ID: 50101
    /// </summary>
    ForwardingRequest = 50101,

    /// <summary>
    /// Session owner information retrieved from cache. Event ID: 50102
    /// </summary>
    SessionOwnerRetrieved = 50102,

    /// <summary>
    /// Failed to retrieve session owner information from cache. Event ID: 50103
    /// </summary>
    FailedToRetrieveSessionOwner = 50103,

    /// <summary>
    /// Removing stale session after receiving 404 from remote endpoint. Event ID: 50104
    /// </summary>
    RemovingStaleSession = 50104,

    /// <summary>
    /// Failed to remove session from cache. Event ID: 50105
    /// </summary>
    FailedToRemoveSession = 50105,

    /// <summary>
    /// Removing a stale session that points to the local address but has an outdated OwnerId. Event ID: 50106
    /// </summary>
    RemovingStaleLocalSession = 50106,
}

/// <summary>
/// Semantic logging methods for session state management operations.
/// Uses structured logging with compile-time code generation via LoggerMessage attributes.
/// </summary>
internal static partial class SemanticLogging
{
    /// <summary>
    /// Logs when resolving the owner of an existing session.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ownerId">The proposed owner identifier.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.ResolvingSessionOwner,
        Level = LogLevel.Debug,
        Message = "Resolving session owner for session {SessionId}, proposed owner: {OwnerId}"
    )]
    public static partial void ResolvingSessionOwner(
        this ILogger logger,
        string sessionId,
        string ownerId
    );

    /// <summary>
    /// Logs when a new session is claimed by the current server instance.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ownerId">The owner identifier of this server instance.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.SessionClaimed,
        Level = LogLevel.Debug,
        Message = "Session {SessionId} claimed by owner {OwnerId}"
    )]
    public static partial void SessionClaimed(
        this ILogger logger,
        string sessionId,
        string ownerId
    );

    /// <summary>
    /// Logs when an existing session is found to be owned by another server instance.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ownerId">The owner identifier of the server instance that owns the session.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.SessionOwnedByOther,
        Level = LogLevel.Debug,
        Message = "Session {SessionId} already owned by {OwnerId}"
    )]
    public static partial void SessionOwnedByOther(
        this ILogger logger,
        string sessionId,
        string ownerId
    );

    /// <summary>
    /// Logs when deserialization of session owner information fails.
    /// This can occur if the cached data is corrupted or in an unexpected format.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ex">The exception that occurred during deserialization.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.FailedToDeserializeSessionOwner,
        Level = LogLevel.Warning,
        Message = "Failed to deserialize session owner for session {SessionId}"
    )]
    public static partial void FailedToDeserializeSessionOwner(
        this ILogger logger,
        string sessionId,
        Exception ex
    );

    /// <summary>
    /// Logs when a session is established on the current server instance.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.SessionEstablished,
        Level = LogLevel.Information,
        Message = "Session established for session {SessionId} on this host"
    )]
    public static partial void SessionEstablished(this ILogger logger, string sessionId);

    /// <summary>
    /// Logs when a request is being forwarded to another server instance that owns the session.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="destinationPrefix">The destination server prefix (scheme://host:port).</param>
    /// <param name="sessionId">The session identifier.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.ForwardingRequest,
        Level = LogLevel.Information,
        Message = "Forwarding request to {DestinationPrefix} for session {SessionId}"
    )]
    public static partial void ForwardingRequest(
        this ILogger logger,
        string destinationPrefix,
        string sessionId
    );

    /// <summary>
    /// Logs when session owner information is retrieved from the session store.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ownerId">The owner identifier of the retrieved session.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.SessionOwnerRetrieved,
        Level = LogLevel.Debug,
        Message = "Retrieved session owner {OwnerId} for session {SessionId}"
    )]
    public static partial void SessionOwnerRetrieved(
        this ILogger logger,
        string sessionId,
        string ownerId
    );

    /// <summary>
    /// Logs when retrieval of session owner information fails.
    /// This can occur if there are cache connectivity issues or other errors.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ex">The exception that occurred during retrieval.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.FailedToRetrieveSessionOwner,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve session owner for session {SessionId}"
    )]
    public static partial void FailedToRetrieveSessionOwner(
        this ILogger logger,
        string sessionId,
        Exception ex
    );

    /// <summary>
    /// Logs when removing a stale session after receiving a 404 from the remote endpoint.
    /// This indicates the remote server no longer has the session, likely due to a process restart.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ownerId">The owner identifier of the server that returned 404.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.RemovingStaleSession,
        Level = LogLevel.Warning,
        Message = "Removing stale session {SessionId} after 404 response from owner {OwnerId}"
    )]
    public static partial void RemovingStaleSession(
        this ILogger logger,
        string sessionId,
        string ownerId
    );

    /// <summary>
    /// Logs when removal of a session from the cache fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ex">The exception that occurred during removal.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.FailedToRemoveSession,
        Level = LogLevel.Warning,
        Message = "Failed to remove session {SessionId} from cache"
    )]
    public static partial void FailedToRemoveSession(
        this ILogger logger,
        string sessionId,
        Exception ex
    );

    /// <summary>
    /// Logs when removing a stale session record that references this host with an outdated OwnerId.
    /// This occurs when the application restarts and generates a new OwnerId, making the previous session unusable.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="oldOwnerId">The stale owner identifier from the cache.</param>
    [LoggerMessage(
        EventId = (int)LogEventId.RemovingStaleLocalSession,
        Level = LogLevel.Warning,
        Message = "Removing stale session {SessionId} owned by previous instance {OldOwnerId}"
    )]
    public static partial void RemovingStaleLocalSession(
        this ILogger logger,
        string sessionId,
        string oldOwnerId
    );
}
