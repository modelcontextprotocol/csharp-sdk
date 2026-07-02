// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Distributed;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;
using Moq;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace ModelContextProtocol.AspNetCore.Distributed.Tests;

public sealed class SessionAffinityEndpointFilterTests
{
    [Fact]
    public async Task InvokeAsync_WithoutSessionId_CallsNextAndSkipsStore()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        using var forwarder = new TestHttpForwarder();
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }
        );

        Assert.Null(result);
        Assert.True(nextCalled);
        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Never());
        Assert.Empty(forwarder.Calls);
    }

    [Fact]
    public async Task InvokeAsync_WhenSessionClaimedLocally_CallsNext()
    {
        const string sessionId = "session-1";

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        // The session store will call the factory to create owner info
        // We need to capture and return whatever owner info the filter creates
        SessionOwnerInfo? capturedOwnerInfo = null;

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<SessionOwnerInfo>> factory, CancellationToken ct) =>
            {
                // Call the factory to get the owner info that the filter would create
                capturedOwnerInfo = factory(ct).GetAwaiter().GetResult();
                return Task.FromResult(capturedOwnerInfo);
            });

        using var forwarder = new TestHttpForwarder();
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://localhost:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }
        );

        // When the session is claimed locally, the filter should call next
        Assert.Null(result);
        Assert.True(nextCalled);
        Assert.NotNull(capturedOwnerInfo);
        Assert.Equal("http://localhost:5000", capturedOwnerInfo.Address);

        // Verify the session ownership was checked/claimed
        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            sessionId,
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Once());

        Assert.Empty(forwarder.Calls);
    }

    [Fact]
    public async Task InvokeAsync_WhenSessionOwnedElsewhere_ForwardsRequest()
    {
        const string sessionId = "session-remote";
        var remoteOwner = new SessionOwnerInfo
        {
            OwnerId = "remote-owner",
            Address = "http://remotehost:8080",
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(remoteOwner));

        using var forwarder = new TestHttpForwarder();
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }
        );

        Assert.Null(result);
        Assert.False(nextCalled);
        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            sessionId,
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Once());
        Assert.Single(forwarder.Calls);
        Assert.Equal("http://remotehost:8080", forwarder.Calls[0].Destination);
    }

    [Fact]
    public async Task InvokeAsync_WhenForwarderFails_ReturnsBadGatewayResult()
    {
        const string sessionId = "session-error";

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(
                new SessionOwnerInfo
                {
                    OwnerId = "remote",
                    Address = "http://remotehost:8080",
                    ClaimedAt = DateTimeOffset.UtcNow,
                }));

        using var forwarder = new TestHttpForwarder { NextResult = ForwarderError.RequestCanceled };
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>(null)
        );

        Assert.NotNull(result);
        Assert.IsAssignableFrom<IResult>(result);

        await ((IResult)result!).ExecuteAsync(httpContext);
        Assert.Equal(StatusCodes.Status502BadGateway, httpContext.Response.StatusCode);

        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            sessionId,
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Once());
        Assert.Single(forwarder.Calls);
        Assert.Equal("http://remotehost:8080", forwarder.Calls[0].Destination);
    }

    [Fact]
    public async Task InvokeAsync_When404FromMcpEndpoint_RemovesStaleSession()
    {
        const string sessionId = "session-stale";
        var remoteOwner = new SessionOwnerInfo
        {
            OwnerId = "remote-owner",
            Address = "http://remotehost:8080",
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Path = "/mcp";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;

        // Set up endpoint with MCP-related display name
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(),
            displayName: "POST /mcp"
        );
        httpContext.SetEndpoint(endpoint);

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(remoteOwner));

        mockSessionStore
            .Setup(x => x.RemoveAsync(sessionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var forwarder = new TestHttpForwarder
        {
            NextStatusCode = StatusCodes.Status404NotFound,
        };
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>(null)
        );

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);

        // Verify session was removed
        mockSessionStore.Verify(x => x.RemoveAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once());

        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            sessionId,
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task InvokeAsync_When404FromSseEndpoint_RemovesStaleSession()
    {
        const string sessionId = "session-stale-sse";
        var remoteOwner = new SessionOwnerInfo
        {
            OwnerId = "remote-owner",
            Address = "http://remotehost:8080",
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Path = "/sse";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;

        // Set up endpoint with SSE-related display name
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(),
            displayName: "GET /sse"
        );
        httpContext.SetEndpoint(endpoint);

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(remoteOwner));

        mockSessionStore
            .Setup(x => x.RemoveAsync(sessionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var forwarder = new TestHttpForwarder
        {
            NextStatusCode = StatusCodes.Status404NotFound,
        };
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>(null)
        );

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);

        // Verify session was removed
        mockSessionStore.Verify(x => x.RemoveAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task InvokeAsync_When404FromNonMcpEndpoint_DoesNotRemoveSession()
    {
        const string sessionId = "session-health";
        var remoteOwner = new SessionOwnerInfo
        {
            OwnerId = "remote-owner",
            Address = "remotehost:8080",
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Path = "/health";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;

        // Set up endpoint with non-MCP display name
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(),
            displayName: "GET /health"
        );
        httpContext.SetEndpoint(endpoint);

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(remoteOwner));

        using var forwarder = new TestHttpForwarder
        {
            NextStatusCode = StatusCodes.Status404NotFound,
        };
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>(null)
        );

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);

        // Verify session was NOT removed (only GetOrClaimOwnershipAsync was called)
        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            sessionId,
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Once());
        mockSessionStore.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task InvokeAsync_When200FromMcpEndpoint_DoesNotRemoveSession()
    {
        const string sessionId = "session-success";
        var remoteOwner = new SessionOwnerInfo
        {
            OwnerId = "remote-owner",
            Address = "http://remotehost:8080",
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Path = "/mcp";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;

        // Set up endpoint with MCP-related display name
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(),
            displayName: "POST /mcp"
        );
        httpContext.SetEndpoint(endpoint);

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(remoteOwner));

        using var forwarder = new TestHttpForwarder { NextStatusCode = StatusCodes.Status200OK };
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>(null)
        );

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);

        // Verify session was NOT removed (only GetOrClaimOwnershipAsync was called)
        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            sessionId,
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Once());
        mockSessionStore.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task InvokeAsync_When404WithoutSessionId_DoesNotRemoveSession()
    {
        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Path = "/mcp";

        // Set up endpoint with MCP-related display name
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(),
            displayName: "POST /mcp"
        );
        httpContext.SetEndpoint(endpoint);

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();

        using var forwarder = new TestHttpForwarder
        {
            NextStatusCode = StatusCodes.Status404NotFound,
        };
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://127.0.0.1:5000");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return ValueTask.FromResult<object?>(null);
            }
        );

        Assert.Null(result);
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);

        // Verify no session store operations were performed
        mockSessionStore.Verify(x => x.GetOrClaimOwnershipAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
            It.IsAny<CancellationToken>()), Times.Never());
        mockSessionStore.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task InvokeAsync_WhenServerUsesHttps_PrefersHttpForServiceMesh()
    {
        const string sessionId = "session-https-test";

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        // The session store will call the factory to create owner info
        SessionOwnerInfo? capturedOwnerInfo = null;

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<SessionOwnerInfo>> factory, CancellationToken ct) =>
            {
                capturedOwnerInfo = factory(ct).GetAwaiter().GetResult();
                return Task.FromResult(capturedOwnerInfo);
            });

        using var forwarder = new TestHttpForwarder();
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        // Server listening on both HTTP and HTTPS
        using var server = new TestServer("http://localhost:5000", "https://localhost:5001");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }
        );

        Assert.Null(result);
        Assert.True(nextCalled);
        Assert.NotNull(capturedOwnerInfo);
        // Should prefer HTTP for internal service mesh routing
        Assert.Equal("http://localhost:5000", capturedOwnerInfo.Address);
    }

    [Fact]
    public async Task InvokeAsync_WhenServerUsesOnlyHttps_UsesHttpsScheme()
    {
        const string sessionId = "session-https-only";

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        SessionOwnerInfo? capturedOwnerInfo = null;

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<SessionOwnerInfo>> factory, CancellationToken ct) =>
            {
                capturedOwnerInfo = factory(ct).GetAwaiter().GetResult();
                return Task.FromResult(capturedOwnerInfo);
            });

        using var forwarder = new TestHttpForwarder();
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        // Server listening only on HTTPS
        using var server = new TestServer("https://localhost:5001");

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }
        );

        Assert.Null(result);
        Assert.True(nextCalled);
        Assert.NotNull(capturedOwnerInfo);
        // Should use HTTPS when that's the only available scheme
        Assert.Equal("https://localhost:5001", capturedOwnerInfo.Address);
    }

    [Fact]
    public async Task InvokeAsync_WhenLocalServerAddressConfigured_UsesConfiguredAddress()
    {
        const string sessionId = "session-explicit-address";
        const string configuredAddress = "http://pod-1.mcp-service.default.svc.cluster.local:8080";

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        SessionOwnerInfo? capturedOwnerInfo = null;

        var mockSessionStore = new Mock<ISessionStore>();
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<SessionOwnerInfo>> factory, CancellationToken ct) =>
            {
                capturedOwnerInfo = factory(ct).GetAwaiter().GetResult();
                return Task.FromResult(capturedOwnerInfo);
            });

        using var forwarder = new TestHttpForwarder();
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer("http://localhost:5000");

        var options = new SessionAffinityOptions { LocalServerAddress = configuredAddress };

        var filter = CreateFilter(
            mockSessionStore.Object,
            forwarder,
            httpClientFactory,
            server,
            options
        );

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }
        );

        Assert.Null(result);
        Assert.True(nextCalled);
        Assert.NotNull(capturedOwnerInfo);
        // Should use the explicitly configured address, not the server binding
        Assert.Equal(configuredAddress, capturedOwnerInfo.Address);
    }

    [Fact]
    public async Task InvokeAsync_WithStaleSessionOwnership_ReclaimsAndHandlesLocally()
    {
        // Simulates application restart scenario:
        // Session exists in cache with same Address but different OwnerId (stale)
        const string sessionId = "session123";
        const string localAddress = "http://localhost:5000";
        const string staleOwnerId = "old-guid";

        var httpContext = CreateHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Headers["Mcp-Session-Id"] = sessionId;

        // Set up endpoint with MCP-related display name
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(),
            displayName: "POST /mcp/v1/sse"
        );
        httpContext.SetEndpoint(endpoint);

        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var mockSessionStore = new Mock<ISessionStore>();
        var getOrClaimCallCount = 0;

        // First call returns stale ownership info
        mockSessionStore
            .Setup(x => x.GetOrClaimOwnershipAsync(
                sessionId,
                It.IsAny<Func<CancellationToken, Task<SessionOwnerInfo>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<SessionOwnerInfo>> factory, CancellationToken ct) =>
            {
                getOrClaimCallCount++;
                if (getOrClaimCallCount == 1)
                {
                    // Return stale ownership with old OwnerId but same address
                    return Task.FromResult(
                        new SessionOwnerInfo
                        {
                            OwnerId = staleOwnerId,
                            Address = localAddress,
                            ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                        }
                    );
                }
                else
                {
                    // Second call after RemoveAsync - return new ownership
                    return factory(ct);
                }
            });

        // Expect RemoveAsync to be called to clear stale entry
        mockSessionStore
            .Setup(x => x.RemoveAsync(sessionId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var forwarder = new TestHttpForwarder();
        using var httpClientFactory = new TestForwarderHttpClientFactory();
        using var server = new TestServer(localAddress);

        var filter = CreateFilter(mockSessionStore.Object, forwarder, httpClientFactory, server);

        var nextCalled = false;
        var result = await filter.InvokeAsync(
            invocationContext,
            ctx =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }
        );

        Assert.Null(result);
        Assert.True(nextCalled);

        // Verify interactions
        Assert.Equal(1, getOrClaimCallCount);
        mockSessionStore.Verify(x => x.RemoveAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once());

        // Should NOT forward since we reclaimed locally
        Assert.Empty(forwarder.Calls);
    }

    private static SessionAffinityEndpointFilter CreateFilter(
        ISessionStore sessionStore,
        IHttpForwarder forwarder,
        IForwarderHttpClientFactory httpClientFactory,
        IServer server,
        SessionAffinityOptions? options = null
    )
    {
        return new SessionAffinityEndpointFilter(
            sessionStore,
            forwarder,
            httpClientFactory,
            new ListeningEndpointResolver(),
            server,
            Options.Create(options ?? new SessionAffinityOptions()),
            NullLogger<SessionAffinityEndpointFilter>.Instance
        );
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        private readonly List<object?> _arguments;

        public TestEndpointFilterInvocationContext(
            HttpContext httpContext,
            IEnumerable<object?>? arguments = null
        )
        {
            HttpContext = httpContext;
            _arguments = arguments?.ToList() ?? [];
        }

        public override HttpContext HttpContext { get; }

        public override T GetArgument<T>(int index)
        {
            return (T)_arguments[index]!;
        }

        public override IList<object?> Arguments => _arguments;
    }

    private sealed class TestServer : IServer
    {
        public TestServer(params string[] addresses)
        {
            var addressesFeature = new TestServerAddressesFeature(addresses);
            Features = new FeatureCollection();
            Features.Set<IServerAddressesFeature>(addressesFeature);
        }

        public IFeatureCollection Features { get; }

        public void Dispose()
        {
            if (Features.Get<IServerAddressesFeature>() is TestServerAddressesFeature feature)
            {
                feature.Addresses.Clear();
            }
        }

        public Task StartAsync<TContext>(
            IHttpApplication<TContext> application,
            CancellationToken cancellationToken
        )
            where TContext : notnull
        {
            throw new NotSupportedException();
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestServerAddressesFeature : IServerAddressesFeature
    {
        public TestServerAddressesFeature(IEnumerable<string> addresses)
        {
            foreach (var address in addresses)
            {
                Addresses.Add(address);
            }
        }

        public ICollection<string> Addresses { get; } = [];

        public bool PreferHostingUrls { get; set; }
    }

    private sealed class TestForwarderHttpClientFactory : IForwarderHttpClientFactory, IDisposable
    {
        private readonly HttpMessageInvoker _invoker = new(new TestHttpMessageHandler());

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => _invoker;

        public void Dispose()
        {
            _invoker.Dispose();
        }

        private sealed class TestHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                throw new NotSupportedException();
            }
        }
    }

    private sealed class TestHttpForwarder : IHttpForwarder, IDisposable
    {
        private readonly List<ForwarderCall> _calls = [];

        public List<ForwarderCall> Calls => _calls;

        public ForwarderError NextResult { get; set; } = ForwarderError.None;

        public int NextStatusCode { get; set; } = StatusCodes.Status200OK;

        public void Dispose()
        {
            _calls.Clear();
        }

        // Explicit interface implementation for IHttpForwarder.SendAsync
        // The 4-parameter extension method calls this internally, so we need to track the call
        ValueTask<ForwarderError> IHttpForwarder.SendAsync(
            HttpContext context,
            string destinationPrefix,
            HttpMessageInvoker httpClient,
            ForwarderRequestConfig requestConfig,
            HttpTransformer transformer
        )
        {
            _calls.Add(new ForwarderCall(context, destinationPrefix));

            // Set the response status code if forwarder succeeds
            if (NextResult == ForwarderError.None)
            {
                context.Response.StatusCode = NextStatusCode;
            }

            return new ValueTask<ForwarderError>(NextResult);
        }

        public readonly record struct ForwarderCall(HttpContext Context, string Destination);
    }
}
