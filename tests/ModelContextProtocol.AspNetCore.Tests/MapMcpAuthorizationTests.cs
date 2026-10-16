using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// End-to-end coverage for protecting the MCP endpoint itself with <c>RequireAuthorization()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AuthorizeAttributeTests"/> covers tool-level <c>[Authorize]</c>: the request reaches the MCP
/// handler and the tool collection is filtered per user. This file covers the endpoint-level challenge
/// instead, which has to happen before the request reaches the handler at all — for the initialize request,
/// for follow-up requests inside an established session, and in either session mode.
/// </para>
/// <para>
/// The last test covers the OAuth interaction: the protected-resource metadata document must stay reachable
/// while the MCP endpoint itself requires credentials, and the challenge must point clients at it.
/// </para>
/// </remarks>
[McpServerToolType]
public class MapMcpAuthorizationTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private const string TestScheme = "TestScheme";
    private const string UserHeaderName = "X-Test-User";
    private const string McpEndpointUrl = "http://localhost:5000";
    private const string AuthorizationServerUrl = "https://localhost:7029";

    private WebApplication? _app;

    [McpServerTool(Name = "echo")]
    [Description("Echoes the supplied message back to the caller.")]
    public static string Echo(string message) => $"Echo: {message}";

    private async Task StartAsync(
        HttpServerSessionMode sessionMode = HttpServerSessionMode.Stateful,
        bool advertiseResourceMetadata = false,
        string path = "")
    {
        Builder.Services.AddMcpServer()
            .WithTools<MapMcpAuthorizationTests>()
            .WithHttpTransport(options => options.SessionMode = sessionMode);

        var authentication = Builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = TestScheme;
            options.DefaultAuthenticateScheme = TestScheme;

            if (advertiseResourceMetadata)
            {
                // A real OAuth-protected server challenges with the MCP scheme so that the 401 advertises
                // the protected-resource metadata document, while authentication itself is done per token.
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            }
        });

        authentication.AddScheme<AuthenticationSchemeOptions, HeaderUserAuthenticationHandler>(TestScheme, options => { });

        if (advertiseResourceMetadata)
        {
            authentication.AddScheme<McpAuthenticationOptions, McpAuthenticationHandler>(
                McpAuthenticationDefaults.AuthenticationScheme,
                options => options.ResourceMetadata = new ProtectedResourceMetadata
                {
                    AuthorizationServers = { AuthorizationServerUrl },
                    ScopesSupported = { "mcp:tools" },
                });
        }

        Builder.Services.AddAuthorization();

        _app = Builder.Build();

        _app.MapMcp(path).RequireAuthorization();

        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        base.Dispose();
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    [InlineData(HttpServerSessionMode.Stateless)]
    public async Task Initialize_WithoutCredentials_IsChallenged(HttpServerSessionMode sessionMode)
    {
        await StartAsync(sessionMode);

        using var response = await PostAsync(InitializeRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    [InlineData(HttpServerSessionMode.Stateless)]
    public async Task Initialize_WithCredentials_Succeeds(HttpServerSessionMode sessionMode)
    {
        await StartAsync(sessionMode);

        using var response = await PostAsync(InitializeRequest, user: "alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Stateless mode keeps the endpoint free of server state, so only the session-based modes mint an id.
        if (sessionMode is HttpServerSessionMode.Stateless)
        {
            Assert.False(response.Headers.Contains("mcp-session-id"));
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(GetSessionId(response)));
        }
    }

    [Fact]
    public async Task StatefulSession_FollowUpRequests_RequireCredentials()
    {
        await StartAsync(HttpServerSessionMode.Stateful);

        using var initializeResponse = await PostAsync(InitializeRequest, user: "alice");
        var sessionId = GetSessionId(initializeResponse);
        Assert.False(string.IsNullOrEmpty(sessionId));

        using var authorizedResponse = await PostAsync(ListToolsRequest, user: "alice", sessionId: sessionId);
        var result = AssertType<ListToolsResult>((await ReadSingleSseResponseAsync(authorizedResponse)).Result);
        Assert.Contains(result.Tools, tool => tool.Name == "echo");

        // The session id identifies the session; it must not authenticate the caller. Without credentials
        // the authorization middleware has to reject the request before the MCP handler sees the session.
        using var unauthenticatedResponse = await PostAsync(ListToolsRequest, sessionId: sessionId);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task ResourceMetadata_RemainsReachable_WhileTheMcpEndpointIsChallenged()
    {
        // Mount the endpoint at /mcp, the shape a deployment behind an ingress typically uses: the metadata
        // document then mirrors the resource path, and the challenge advertises that mirrored document.
        await StartAsync(HttpServerSessionMode.Stateful, advertiseResourceMetadata: true, path: "/mcp");

        // Discovery is what an unauthenticated client does first, so the metadata document has to be served
        // without credentials even though every request to the MCP endpoint is challenged.
        using var metadataResponse = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource/mcp", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.Equal($"{McpEndpointUrl}/mcp", metadata.Resource);
        Assert.Equal([AuthorizationServerUrl], metadata.AuthorizationServers);

        // The challenge has to tell the client where the metadata document lives, otherwise it cannot
        // discover the authorization server from the 401 alone.
        using var challengedResponse = await PostAsync(InitializeRequest, path: "/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, challengedResponse.StatusCode);
        var challenge = Assert.Single(challengedResponse.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains(
            $"resource_metadata=\"{McpEndpointUrl}/.well-known/oauth-protected-resource/mcp\"",
            challenge.Parameter);
    }

    private Task<HttpResponseMessage> PostAsync(string json, string? user = null, string? sessionId = null, string path = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (user is not null)
        {
            request.Headers.Add(UserHeaderName, user);
        }

        if (sessionId is not null)
        {
            request.Headers.Add("mcp-session-id", sessionId);
        }

        return HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string? GetSessionId(HttpResponseMessage response)
        => response.Headers.TryGetValues("mcp-session-id", out var values) ? Assert.Single(values) : null;

    private static async Task<JsonRpcResponse> ReadSingleSseResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var responseStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var payloads = new List<string>();

        await foreach (var sseItem in SseParser.Create(responseStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal("message", sseItem.EventType);
            payloads.Add(sseItem.Data);
        }

        var jsonRpcResponse = JsonSerializer.Deserialize(
            Assert.Single(payloads),
            (JsonTypeInfo<JsonRpcResponse>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse)));

        Assert.NotNull(jsonRpcResponse);
        return jsonRpcResponse;
    }

    private static T AssertType<T>(JsonNode? jsonNode)
    {
        var value = JsonSerializer.Deserialize(
            jsonNode, (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T)));

        Assert.NotNull(value);
        return value;
    }

    private static string InitializeRequest => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"MapMcpAuthorizationTests","version":"1.0.0"}}}
        """;

    private static string ListToolsRequest => """
        {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
        """;

    /// <summary>
    /// Authenticates a caller from a header, so tests can assert both the challenged and the authorized path
    /// without standing up a token issuer.
    /// </summary>
    private sealed class HeaderUserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeaderName, out var user) || string.IsNullOrEmpty(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
