// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace ModelContextProtocol.AspNetCore.Distributed;

/// <summary>
/// Endpoint filter that implements session affinity for MCP requests.
/// Routes requests to the server that owns the session, or handles locally if this is the owner.
/// </summary>
internal sealed class SessionAffinityEndpointFilter : IEndpointFilter
{
    private const string McpSessionIdHeaderName = "Mcp-Session-Id";

    private readonly ISessionStore _sessionStore;
    private readonly string _localOwnerId;
    private readonly IHttpForwarder _forwarder;
    private readonly HttpMessageInvoker _httpClient;
    private readonly ForwarderRequestConfig _forwarderRequestConfig;
    private readonly ILogger<SessionAffinityEndpointFilter> _logger;
    private readonly string _localAddress;

    public SessionAffinityEndpointFilter(
        ISessionStore sessionStore,
        IHttpForwarder forwarder,
        IForwarderHttpClientFactory httpClientFactory,
        IListeningEndpointResolver listeningEndpointResolver,
        IServer server,
        IOptions<SessionAffinityOptions> options,
        ILogger<SessionAffinityEndpointFilter> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var optionsValue = options.Value;

        _sessionStore = sessionStore;
        // IMPORTANT: The OwnerId (_localOwnerId) is regenerated as a new GUID each time the application restarts.
        // Session ownership data does not persist across restarts, so stale session entries are cleared when encountered.
        _localOwnerId = Guid.NewGuid().ToString();
        _forwarder = forwarder;
        _httpClient = httpClientFactory.CreateClient(
            new ForwarderHttpClientContext
            {
                NewConfig = optionsValue.HttpClientConfig ?? HttpClientConfig.Empty,
            }
        );
        _forwarderRequestConfig =
            optionsValue.ForwarderRequestConfig ?? ForwarderRequestConfig.Empty;
        _logger = logger;

        // Use the listening endpoint resolver to get the advertised address
        // IServerAddressesFeature is populated before endpoint filters are created
        // Note: LocalServerAddress can be set via IPostConfigureOptions for dynamic resolution
        _localAddress = listeningEndpointResolver.ResolveListeningEndpoint(server, optionsValue);
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var httpContext = context.HttpContext;
        var sessionId = ExtractSessionId(httpContext);

        // Resolve the owner of this session (if session ID exists)
        SessionOwnerInfo? ownerInfo = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            _logger.ResolvingSessionOwner(sessionId, _localOwnerId);

            ownerInfo = await _sessionStore.GetOrClaimOwnershipAsync(
                sessionId,
                CreateSessionOwnerInfo,
                httpContext.RequestAborted
            );

            if (ownerInfo.OwnerId != _localOwnerId)
            {
                if (
                    string.Equals(
                        ownerInfo.Address,
                        _localAddress,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    // Application restart detected - the session points to this host but has a different OwnerId
                    _logger.RemovingStaleLocalSession(sessionId, ownerInfo.OwnerId);
                    await _sessionStore.RemoveAsync(sessionId, httpContext.RequestAborted);
                    ownerInfo = null;
                    sessionId = null;
                }
                else
                {
                    _logger.SessionOwnedByOther(sessionId, ownerInfo.OwnerId);
                }
            }
        }

        // Handle locally if no session ID or this host owns the session
        if (ownerInfo is null || ownerInfo.OwnerId == _localOwnerId)
        {
            context.HttpContext.Response.OnStarting(async () =>
            {
                // Check if the server set a session ID different from the original session ID
                var responseSessionId = ExtractResponseSessionId(httpContext);
                if (
                    !string.IsNullOrEmpty(responseSessionId)
                    && !string.Equals(sessionId, responseSessionId, StringComparison.Ordinal)
                )
                {
                    // Update the new session to point to this host
                    await _sessionStore.GetOrClaimOwnershipAsync(
                        responseSessionId,
                        CreateSessionOwnerInfo,
                        httpContext.RequestAborted
                    );
                    _logger.SessionEstablished(responseSessionId);
                }
            });

            return await next(context);
        }

        // Forward to the owner - this writes directly to the response
        _logger.ForwardingRequest(ownerInfo.Address, sessionId ?? "(none)");
        var error = await _forwarder.SendAsync(
            httpContext,
            ownerInfo.Address,
            _httpClient,
            _forwarderRequestConfig
        );

        if (error == ForwarderError.None)
        {
            // Check if the remote server returned 404 - indicates session no longer exists
            // Only remove session if this is an MCP endpoint request (not a health check, metrics, etc.)
            if (
                httpContext.Response.StatusCode == StatusCodes.Status404NotFound
                && !string.IsNullOrEmpty(sessionId)
                && IsMcpEndpointRequest(httpContext)
            )
            {
                _logger.RemovingStaleSession(sessionId, ownerInfo.OwnerId);
                await _sessionStore.RemoveAsync(sessionId, httpContext.RequestAborted);
            }

            // The forwarder has already written the response, return null to indicate completion
            return null;
        }

        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    private static bool IsMcpEndpointRequest(HttpContext context)
    {
        // The session affinity filter is only applied to MCP endpoints
        // Check if the endpoint has MCP-related metadata or path patterns
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            return false;
        }

        // Check for MCP-specific endpoint metadata
        // The endpoint display name typically contains route pattern information
        var displayName = endpoint.DisplayName;
        if (
            !string.IsNullOrEmpty(displayName)
            && (
                displayName.Contains("mcp", StringComparison.OrdinalIgnoreCase)
                || displayName.Contains("sse", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return true;
        }

        return false;
    }

    private static string? ExtractSessionId(HttpContext context)
    {
        // Try header first (for Streamable HTTP POST/GET/DELETE)
        if (context.Request.Headers.TryGetValue(McpSessionIdHeaderName, out var header))
        {
            return header.ToString();
        }

        // Try query string (for legacy SSE /message endpoint)
        if (context.Request.Query.TryGetValue("sessionId", out var sessionId))
        {
            return sessionId.ToString();
        }

        return null;
    }

    private static string? ExtractResponseSessionId(HttpContext context)
    {
        // Check response headers for the session ID
        if (context.Response.Headers.TryGetValue(McpSessionIdHeaderName, out var header))
        {
            return header.ToString();
        }

        return null;
    }

    private Task<SessionOwnerInfo> CreateSessionOwnerInfo(CancellationToken cancellationToken)
    {
        return Task.FromResult(
            new SessionOwnerInfo
            {
                OwnerId = _localOwnerId,
                Address = _localAddress,
                ClaimedAt = DateTimeOffset.UtcNow,
            }
        );
    }
}
