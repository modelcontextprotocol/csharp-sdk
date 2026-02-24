// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;

namespace ModelContextProtocol.AspNetCore.Distributed;

/// <summary>
/// HybridCache-backed implementation of <see cref="ISessionStore"/>.
/// This implementation provides distributed session ownership across multiple servers
/// using HybridCache, which combines in-memory and distributed caching for optimal performance.
/// Sessions are stored with a configurable expiration time (default: 15 minutes).
/// </summary>
/// <remarks>
/// HybridCache provides several advantages over IDistributedCache:
/// - Automatic serialization/deserialization
/// - Built-in stampede protection
/// - L1 (in-memory) + L2 (distributed) caching for better performance
/// - Tag-based cache invalidation support
/// </remarks>
internal sealed class HybridCacheSessionStore : ISessionStore
{
    private static readonly TimeSpan DefaultSessionTimeout = TimeSpan.FromMinutes(15);
    private readonly HybridCache _cache;
    private readonly ILogger<HybridCacheSessionStore> _logger;
    private readonly HybridCacheEntryOptions _cacheEntryOptions;

    public HybridCacheSessionStore(
        HybridCache cache,
        ILogger<HybridCacheSessionStore> logger,
        TimeSpan? sessionTimeout = null
    )
    {
        _cache = cache;
        _logger = logger;
        var resolvedSessionTimeout = sessionTimeout ?? DefaultSessionTimeout;
        _cacheEntryOptions = new()
        {
            Expiration = resolvedSessionTimeout,
            // Allow L1 cache to expire sooner for better memory management
            LocalCacheExpiration = TimeSpan.FromMinutes(
                Math.Min(resolvedSessionTimeout.TotalMinutes / 2, 5)
            ),
        };
    }

    public async Task<SessionOwnerInfo> GetOrClaimOwnershipAsync(
        string sessionId,
        Func<CancellationToken, Task<SessionOwnerInfo>> ownerInfoFactory,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(ownerInfoFactory);

        var key = $"mcp:session:{sessionId}";

        try
        {
            // Track whether we created a new entry or retrieved an existing one
            var wasCreated = false;

            // HybridCache.GetOrCreateAsync will check L1 (memory) first, then L2 (distributed)
            // If not found, it will call the factory to create and cache the value
            var owner = await _cache.GetOrCreateAsync(
                key,
                async cancel =>
                {
                    wasCreated = true;

                    // Call the provided factory to create the owner info
                    var ownerInfo = await ownerInfoFactory(cancel);

                    _logger.SessionClaimed(sessionId, ownerInfo.OwnerId);

                    return ownerInfo;
                },
                options: _cacheEntryOptions,
                cancellationToken: cancellationToken
            );

            // HybridCache uses absolute expiration. We need to implement sliding expiration manually
            // by re-setting the value with a new expiration time on each access.
            // Only refresh if we retrieved an existing entry (not if we just created it).
            if (!wasCreated)
            {
                await _cache.SetAsync(
                    key,
                    owner,
                    _cacheEntryOptions,
                    cancellationToken: cancellationToken
                );
            }

            _logger.SessionOwnerRetrieved(sessionId, owner.OwnerId);

            return owner;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.FailedToRetrieveSessionOwner(sessionId, ex);
            throw;
        }
    }

    public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var key = $"mcp:session:{sessionId}";

        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.FailedToRemoveSession(sessionId, ex);
            // Don't rethrow - session removal is a best-effort cleanup operation
            // The session will expire naturally if removal fails
        }
    }
}
