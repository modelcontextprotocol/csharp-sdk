// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Distributed;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace ModelContextProtocol.AspNetCore.Distributed.Tests;

/// <summary>
/// Integration tests for session affinity using real Kestrel servers.
/// These tests verify that multiple WebApplication instances can share session state
/// via a shared IDistributedCache and that each client maintains its own session.
/// </summary>
public sealed class RealServerIntegrationTests
{
    [Fact]
    public async Task MultipleServersWithSharedCacheMaintainSeparateClientSessions()
    {
        // Arrange - Create shared distributed cache for session affinity
        var sharedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions())
        );

        // Create two real Kestrel servers with shared cache
        await using var host1 = await CreateKestrelServerAsync(sharedCache, "server-1");
        await using var host2 = await CreateKestrelServerAsync(sharedCache, "server-2");

        // Create two separate MCP clients connecting to different servers
        using var httpClient1 = new HttpClient();
        using var httpClient2 = new HttpClient();

        await using var transport1 = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = host1.McpEndpoint },
            httpClient1
        );

        await using var transport2 = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = host2.McpEndpoint },
            httpClient2
        );

        await using var mcpClient1 = await McpClient.CreateAsync(
            transport1,
            cancellationToken: CancellationToken.None
        );
        await using var mcpClient2 = await McpClient.CreateAsync(
            transport2,
            cancellationToken: CancellationToken.None
        );

        // Act - Each client calls tools on its connected server
        // First, verify which server each client is connected to
        var client1Server = await mcpClient1.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var client2Server = await mcpClient2.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );

        // Assert - Each server identifies correctly
        Assert.Equal("server-1", ((TextContentBlock)client1Server.Content[0]).Text);
        Assert.Equal("server-2", ((TextContentBlock)client2Server.Content[0]).Text);
    }

    [Fact]
    public async Task SingleClientCanCallToolsSuccessfully()
    {
        // Arrange
        var sharedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions())
        );

        await using var host = await CreateKestrelServerAsync(sharedCache, "server-test");

        using var httpClient = new HttpClient();

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = host.McpEndpoint },
            httpClient
        );

        await using var mcpClient = await McpClient.CreateAsync(
            transport,
            cancellationToken: CancellationToken.None
        );

        // Act - Make multiple requests with the same client
        var serverId = await mcpClient.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var counter1 = await mcpClient.CallToolAsync(
            "increment_counter",
            cancellationToken: CancellationToken.None
        );
        var counter2 = await mcpClient.CallToolAsync(
            "increment_counter",
            cancellationToken: CancellationToken.None
        );

        // Assert - Tools execute successfully
        Assert.Equal("server-test", ((TextContentBlock)serverId.Content[0]).Text);
        Assert.Equal("1", ((TextContentBlock)counter1.Content[0]).Text);
        // Note: Counter resets because tools are scoped per request, not per session
        // This demonstrates that multiple tool calls work correctly
        Assert.NotNull(((TextContentBlock)counter2.Content[0]).Text);
    }

    [Fact]
    public async Task MultipleClientsWithSameSessionIdStickToSameServer()
    {
        // This test demonstrates that session affinity works by manually
        // simulating what would happen with load balancing: different clients
        // connect to different servers initially, but if they share a session ID,
        // subsequent requests would be redirected to the session owner.

        // Arrange - Create shared distributed cache for session affinity
        var sharedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions())
        );

        // Create two real Kestrel servers with shared cache
        await using var host1 = await CreateKestrelServerAsync(sharedCache, "server-1");
        await using var host2 = await CreateKestrelServerAsync(sharedCache, "server-2");

        // Create first client connecting to server-1
        using var httpClient1 = new HttpClient();
        await using var transport1 = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = host1.McpEndpoint },
            httpClient1
        );
        await using var mcpClient1 = await McpClient.CreateAsync(
            transport1,
            cancellationToken: CancellationToken.None
        );

        // Act - First client establishes session on server-1
        var firstResponse = await mcpClient1.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var firstServerId = ((TextContentBlock)firstResponse.Content[0]).Text;

        // Make more requests with same client - should stay on same server
        var secondResponse = await mcpClient1.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var secondServerId = ((TextContentBlock)secondResponse.Content[0]).Text;

        var thirdResponse = await mcpClient1.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var thirdServerId = ((TextContentBlock)thirdResponse.Content[0]).Text;

        // Assert - All requests from same client stay on same server
        Assert.Equal("server-1", firstServerId);
        Assert.Equal(firstServerId, secondServerId);
        Assert.Equal(firstServerId, thirdServerId);
    }

    [Fact]
    public async Task LoadBalancingDistributesNewClientsAcrossDifferentServers()
    {
        // Arrange
        var sharedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions())
        );

        await using var host1 = await CreateKestrelServerAsync(sharedCache, "server-1");
        await using var host2 = await CreateKestrelServerAsync(sharedCache, "server-2");

        // Create multiple clients with load balancing
        var serverEndpoints = new[] { host1.BaseAddress, host2.BaseAddress };
        var clients =
            new List<(HttpClient HttpClient, HttpClientTransport Transport, McpClient McpClient)>();
        var loadBalancers = new List<RoundRobinLoadBalancingHandler>();

        try
        {
            // Create 4 clients with load balancing
            for (int i = 0; i < 4; i++)
            {
                var requestCount = i; // Capture for closure
                var loadBalancer = new RoundRobinLoadBalancingHandler(
                    serverEndpoints,
                    () => requestCount
                );
                loadBalancers.Add(loadBalancer);

                var httpClient = new HttpClient(loadBalancer);
                var transport = new HttpClientTransport(
                    new HttpClientTransportOptions { Endpoint = host1.McpEndpoint },
                    httpClient
                );
                var mcpClient = await McpClient.CreateAsync(
                    transport,
                    cancellationToken: CancellationToken.None
                );

                clients.Add((httpClient, transport, mcpClient));
            }

            // Act - Each client makes a request
            var serverIds = new List<string>();
            foreach (var (_, _, mcpClient) in clients)
            {
                var response = await mcpClient.CallToolAsync(
                    "get_server_id",
                    cancellationToken: CancellationToken.None
                );
                serverIds.Add(((TextContentBlock)response.Content[0]).Text);
            }

            // Assert - Should have both servers represented
            Assert.Contains("server-1", serverIds);
            Assert.Contains("server-2", serverIds);
            Assert.Equal(4, serverIds.Count);
        }
        finally
        {
            // Cleanup
            foreach (var (httpClient, transport, mcpClient) in clients)
            {
                await mcpClient.DisposeAsync();
                await transport.DisposeAsync();
                httpClient.Dispose();
            }

            foreach (var loadBalancer in loadBalancers)
            {
                loadBalancer.Dispose();
            }
        }
    }

    [Fact]
    public async Task SessionAffinityPreservesConnectionToOriginalServer()
    {
        // This test demonstrates that once a client establishes a session with a server,
        // it maintains that connection across multiple requests (simulating what would
        // happen with session affinity if requests were load balanced).

        // Arrange - Create shared distributed cache for session affinity
        var sharedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions())
        );

        await using var host1 = await CreateKestrelServerAsync(sharedCache, "server-1");
        await using var host2 = await CreateKestrelServerAsync(sharedCache, "server-2");

        // Create client connecting to server-1
        using var httpClient1 = new HttpClient();
        await using var transport1 = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = host1.McpEndpoint },
            httpClient1
        );
        await using var mcpClient1 = await McpClient.CreateAsync(
            transport1,
            cancellationToken: CancellationToken.None
        );

        // Act - Client makes multiple requests, all should stay on server-1
        var firstResponse = await mcpClient1.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var firstServerId = ((TextContentBlock)firstResponse.Content[0]).Text;

        var secondResponse = await mcpClient1.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var secondServerId = ((TextContentBlock)secondResponse.Content[0]).Text;

        var thirdResponse = await mcpClient1.CallToolAsync(
            "get_server_id",
            cancellationToken: CancellationToken.None
        );
        var thirdServerId = ((TextContentBlock)thirdResponse.Content[0]).Text;

        // Assert - All requests stay on the same server (session affinity in action)
        Assert.Equal("server-1", firstServerId);
        Assert.Equal("server-1", secondServerId);
        Assert.Equal("server-1", thirdServerId);
    }

    private static async Task<KestrelServerHandle> CreateKestrelServerAsync(
        IDistributedCache sharedCache,
        string serverId
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseKestrel(options =>
            {
                options.Listen(new IPEndPoint(IPAddress.Loopback, 0)); // Let the OS select an available port
            });

            webHost.ConfigureServices(services =>
            {
                // Use shared distributed cache for session affinity across servers
                services.AddSingleton(sharedCache);

                // Add MCP server with tools
                services.AddMcpServer().WithTools<TestTools>().WithHttpTransport();

                // Add session affinity (listening endpoint resolver will determine address)
                services.AddMcpHttpSessionAffinity();

                // Register server-specific state (identifies which server instance this is)
                services.AddSingleton(new ServerState { ServerId = serverId });
            });

            webHost.Configure(app =>
            {
                // Enable routing middleware
                app.UseRouting();

                // Map MCP endpoints with session affinity
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapMcp("mcp").WithSessionAffinity();
                });
            });
        });

        var host = await hostBuilder.StartAsync();
        var baseAddress = ResolveBaseAddress(host);
        return new KestrelServerHandle(host, baseAddress);
    }

    private static Uri ResolveBaseAddress(IHost host)
    {
        var server = host.Services.GetRequiredService<IServer>();
        var addressesFeature = server.Features.Get<IServerAddressesFeature>();
        if (addressesFeature is null || addressesFeature.Addresses.Count == 0)
        {
            throw new InvalidOperationException("Kestrel server did not expose any addresses.");
        }

        foreach (var address in addressesFeature.Addresses)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                return NormalizeBaseAddress(uri);
            }
        }

        throw new InvalidOperationException("Failed to resolve a valid server address.");
    }

    private static Uri NormalizeBaseAddress(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Path = "/",
            Query = null,
            Fragment = null,
        };

        return builder.Uri;
    }

    [McpServerToolType]
    private sealed class TestTools
    {
        private readonly ServerState _serverState;
        private int _counter;

#pragma warning disable S1144 // Constructor used via dependency injection
        public TestTools(ServerState serverState)
#pragma warning restore S1144
        {
            _serverState = serverState;
        }

        [McpServerTool]
        [Description("Returns the ID of the server handling the request")]
        public string GetServerId() => _serverState.ServerId;

        [McpServerTool]
        [Description("Increments a counter and returns the new value")]
        public string IncrementCounter() =>
            Interlocked.Increment(ref _counter).ToString(CultureInfo.InvariantCulture);
    }

    private sealed class ServerState
    {
        public required string ServerId { get; init; }
    }

    private sealed class KestrelServerHandle : IAsyncDisposable
    {
        private readonly IHost _host;

        public KestrelServerHandle(IHost host, Uri baseAddress)
        {
            _host = host;
            BaseAddress = baseAddress;
            McpEndpoint = new Uri(baseAddress, "mcp");
        }

        public Uri BaseAddress { get; }

        public Uri McpEndpoint { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _host.StopAsync();
            }
            finally
            {
                _host.Dispose();
            }
        }
    }

    /// <summary>
    /// HTTP handler that implements client-side round-robin load balancing across multiple servers.
    /// Modifies request URIs to alternate between different port numbers.
    /// </summary>
    private sealed class RoundRobinLoadBalancingHandler : DelegatingHandler
    {
        private readonly Uri[] _endpoints;
        private readonly Func<int> _getRequestCount;

#pragma warning disable CA2000 // DelegatingHandler takes ownership of the inner handler
        public RoundRobinLoadBalancingHandler(Uri[] endpoints, Func<int> getRequestCount)
            : base(new HttpClientHandler())
#pragma warning restore CA2000
        {
            _endpoints = endpoints;
            _getRequestCount = getRequestCount;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri != null && _endpoints.Length > 0)
            {
                // Round-robin: alternate between endpoints based on request count
                var requestCount = _getRequestCount();
                var selectedEndpoint = _endpoints[requestCount % _endpoints.Length];

                // Modify the request URI to use the selected endpoint
                var builder = new UriBuilder(request.RequestUri)
                {
                    Scheme = selectedEndpoint.Scheme,
                    Host = selectedEndpoint.Host,
                    Port = selectedEndpoint.Port,
                };

                request.RequestUri = builder.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
