using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

[Collection(nameof(HttpOverStdioIntegrationCollection))]
public sealed class HttpOverStdioIntegrationTests(ITestOutputHelper outputHelper) : LoggedTest(outputHelper)
{
    private static readonly Uri s_endpoint = new("http://stdio.mcp.test/mcp");

    [Fact]
    public async Task ModernProtocol_UsesExistingHttpFeaturesOverExactHttp2()
    {
        await using McpClient client = await ConnectAsync(
            serverArguments: ["--allowed-host=stdio.mcp.test"],
            configureHttp: options =>
                options.AdditionalHeaders = new Dictionary<string, string>
                {
                    ["X-Transport-Test"] = "header-value",
                });

        Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);
        Assert.Null(client.SessionId);

        CallToolResult inspection = await client.CallToolAsync(
            "inspect",
            cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument inspectionJson = JsonDocument.Parse(GetText(inspection));
        JsonElement root = inspectionJson.RootElement;
        Assert.Equal("HTTP/2", root.GetProperty("protocol").GetString());
        Assert.Equal("stdio.mcp.test", root.GetProperty("host").GetString());
        Assert.Equal("/mcp", root.GetProperty("path").GetString());
        Assert.Equal("header-value", root.GetProperty("testHeader").GetString());

        IList<McpClientTool> tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools, tool => tool.Name == "echo");

        CallToolResult echo = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "hello over HTTP/2" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("hello over HTTP/2", GetText(echo));

        IList<McpClientPrompt> prompts = await client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(prompts, prompt => prompt.Name == "greeting");
        GetPromptResult prompt = await client.GetPromptAsync(
            "greeting",
            new Dictionary<string, object?> { ["name"] = "Ada" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(
            "Hello, Ada.",
            prompt.Messages.Select(message => message.Content)
                .OfType<TextContentBlock>()
                .Select(content => content.Text));

        IList<McpClientResource> resources = await client.ListResourcesAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(resources, resource => resource.Uri == "test://http-over-stdio/resource");
        ReadResourceResult resource = await client.ReadResourceAsync(
            "test://http-over-stdio/resource",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            "resource-over-http2-stdio",
            Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents)).Text);

        var progressMessages = new ConcurrentQueue<string>();
        CallToolResult progressResult = await client.CallToolAsync(
            "progress",
            progress: new SynchronousProgress(value =>
            {
                if (value.Message is not null)
                {
                    progressMessages.Enqueue(value.Message);
                }
            }),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("progress-complete", GetText(progressResult));
        Assert.Equal(["complete", "halfway"], progressMessages.Order());

        CallToolResult failure = await client.CallToolAsync(
            "fail",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(failure.IsError);
        Assert.Equal("An error occurred invoking 'fail'.", GetText(failure));
    }

    [Fact]
    public async Task ConcurrentRequests_MultiplexOverOnePhysicalConnection()
    {
        await using McpClient client = await ConnectAsync();

        const int RequestCount = 16;
        string batch = Guid.NewGuid().ToString("N");
        Task<CallToolResult>[] calls = Enumerable.Range(0, RequestCount)
            .Select(index => client.CallToolAsync(
                "concurrent-barrier",
                new Dictionary<string, object?>
                {
                    ["batch"] = batch,
                    ["expectedCount"] = RequestCount,
                    ["message"] = index.ToString(),
                },
                cancellationToken: TestContext.Current.CancellationToken).AsTask())
            .ToArray();

        CallToolResult[] results = await Task.WhenAll(calls)
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        string[] values = results.Select(GetText).ToArray();
        Assert.Equal(RequestCount, values.Select(value => value.Split('|')[0]).Distinct().Count());
        Assert.Single(values.Select(value => value.Split('|')[1]).Distinct());
    }

    [Fact]
    public async Task CancellingOneHttp2Stream_DoesNotCloseConnection()
    {
        await using McpClient client = await ConnectAsync();

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<CallToolResult> blockingCall = client.CallToolAsync(
            "blocking",
            progress: new SynchronousProgress(value =>
            {
                if (value.Message == "started")
                {
                    started.TrySetResult();
                }
            }),
            cancellationToken: callCts.Token).AsTask();

        await started.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        callCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockingCall);

        CallToolResult cancellationCount = await client.CallToolAsync(
            "cancellation-count",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("1", GetText(cancellationCount));

        CallToolResult echo = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "still connected" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("still connected", GetText(echo));
    }

    [Fact]
    public async Task Mrtr_RoundTripUsesStreamableHttpImplementation()
    {
        McpClientOptions clientOptions = CreateModernClientOptions();
        clientOptions.Handlers.ElicitationHandler = static (_, _) =>
            new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>(),
            });

        await using McpClient client = await ConnectAsync(clientOptions: clientOptions);
        CallToolResult result = await client.CallToolAsync(
            "mrtr-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("elicit-ok:accept", GetText(result));
    }

    [Fact]
    public async Task LegacyStatefulProtocol_UsesGetPostSubscriptionAndDeleteOnSamePipe()
    {
        var notification = new TaskCompletionSource<ResourceUpdatedNotificationParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using McpClient client = await ConnectAsync(
            serverArguments: ["--stateful"],
            clientOptions: new McpClientOptions { ProtocolVersion = "2025-11-25" });

        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
        Assert.NotNull(client.SessionId);

        await using IAsyncDisposable subscription = await client.SubscribeToResourceAsync(
            new Uri("test://http-over-stdio/resource"),
            (value, _) =>
            {
                notification.TrySetResult(value);
                return default;
            },
            cancellationToken: TestContext.Current.CancellationToken);

        CallToolResult notifyResult = await client.CallToolAsync(
            "notify-resource",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("notified", GetText(notifyResult));

        ResourceUpdatedNotificationParams update = await notification.Task.WaitAsync(
            TestConstants.DefaultTimeout,
            TestContext.Current.CancellationToken);
        Assert.Equal("test://http-over-stdio/resource", update.Uri);
    }

    [Fact]
    public async Task HostFiltering_RejectsMismatchedSyntheticAuthority()
    {
        Task connectTask = ConnectAsync(
            serverArguments: ["--allowed-host=expected.mcp.test"],
            endpoint: new Uri("http://unexpected.mcp.test/mcp"));

        await Assert.ThrowsAsync<McpException>(() => connectTask);
    }

    [Fact]
    public async Task ConfiguredTcpListener_CoexistsWithStdioListener()
    {
        var tcpAddress = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using McpClient client = await ConnectAsync(
            serverArguments: ["--listen-tcp"],
            standardErrorLine: line =>
            {
                const string Prefix = "TCP_ADDRESS:";
                if (line.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    tcpAddress.TrySetResult(line[Prefix.Length..]);
                }
            });

        string address = await tcpAddress.Task.WaitAsync(
            TestConstants.DefaultTimeout,
            TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        Assert.Equal(
            "healthy",
            await httpClient.GetStringAsync(
                new Uri(new Uri(address), "/health"),
                TestContext.Current.CancellationToken));

        CallToolResult echo = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "stdio healthy" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("stdio healthy", GetText(echo));
    }

    [Fact]
    public async Task UnexpectedIdleProcessExit_ReportsStdioCompletionDetails()
    {
        var transport = CreateTransport(["--exit-immediately"]);
        await using ITransport session = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        ClientTransportClosedException exception = await Assert.ThrowsAsync<ClientTransportClosedException>(
            () => session.MessageReader.Completion.WaitAsync(
                TestConstants.DefaultTimeout,
                TestContext.Current.CancellationToken));

        StdioClientCompletionDetails details =
            Assert.IsType<StdioClientCompletionDetails>(exception.Details);
        Assert.Equal(23, details.ExitCode);
        IReadOnlyList<string> stderrTail = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            details.StandardErrorTail);
        Assert.Equal(10, stderrTail.Count);
        Assert.Contains("intentional stderr line 11", stderrTail);
    }

    [Fact]
    public async Task ClientDisposal_ClosesStdinAndServerExitsCleanly()
    {
        McpClient client = await ConnectAsync();
        Task<ClientCompletionDetails> completion = client.Completion;

        await client.DisposeAsync();

        StdioClientCompletionDetails details =
            Assert.IsType<StdioClientCompletionDetails>(await completion.WaitAsync(
                TestConstants.DefaultTimeout,
                TestContext.Current.CancellationToken));
        Assert.Equal(0, details.ExitCode);
        Assert.Null(details.Exception);
    }

    [Fact]
    public async Task OAuth_UsesExplicitBackchannelForCompleteAuthorizationFlow()
    {
        List<Uri> backchannelRequests = [];
        List<string> grantTypes = [];
        using var handler = new DelegateHttpMessageHandler(async request =>
        {
            backchannelRequests.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath switch
            {
                "/.well-known/oauth-authorization-server" => JsonResponse(
                    """
                    {
                      "issuer":"https://auth.example",
                      "authorization_endpoint":"https://auth.example/authorize",
                      "token_endpoint":"https://auth.example/token",
                      "registration_endpoint":"https://auth.example/register",
                      "code_challenge_methods_supported":["S256"]
                    }
                    """),
                "/register" => JsonResponse(
                    """{"client_id":"dynamic-client","client_secret":"dynamic-secret","token_endpoint_auth_method":"client_secret_post"}"""),
                "/token" => await CreateTokenResponseAsync(request, grantTypes),
                _ => throw new InvalidOperationException(
                    $"Resource traffic incorrectly used the OAuth backchannel: {request.RequestUri}"),
            };
        });
        using var backchannel = new HttpClient(handler);

        ClientOAuthOptions oauth = CreateOAuthOptions(backchannel);
        await using McpClient client = await ConnectAsync(
            serverArguments: ["--authorization-server=https://auth.example"],
            configureHttp: options => options.OAuth = oauth);

        CallToolResult first = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "authorized" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("authorized", GetText(first));

        CallToolResult second = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "refreshed" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("refreshed", GetText(second));

        Assert.Equal(
            [
                "https://auth.example/.well-known/oauth-authorization-server",
                "https://auth.example/register",
                "https://auth.example/token",
                "https://auth.example/token",
            ],
            backchannelRequests.Select(uri => uri.OriginalString));
        Assert.Equal(["authorization_code", "refresh_token"], grantTypes);
    }

    [Fact]
    public async Task OAuth_CreatesDefaultNetworkBackchannelWhenNoneIsConfigured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        string? issuer = null;

        WebApplication authorizationServer = builder.Build();
        authorizationServer.MapGet(
            "/.well-known/oauth-authorization-server",
            () => Results.Text(
                $$"""
                {
                  "issuer":"{{issuer}}",
                  "authorization_endpoint":"{{issuer}}/authorize",
                  "token_endpoint":"{{issuer}}/token",
                  "registration_endpoint":"{{issuer}}/register",
                  "code_challenge_methods_supported":["S256"]
                }
                """,
                "application/json"));
        authorizationServer.MapPost(
            "/register",
            () => Results.Text(
                """{"client_id":"dynamic-client","client_secret":"dynamic-secret","token_endpoint_auth_method":"client_secret_post"}""",
                "application/json"));
        authorizationServer.MapPost(
            "/token",
            () => Results.Text(
                """{"access_token":"test-access-token","token_type":"Bearer","expires_in":3600}""",
                "application/json"));

        await authorizationServer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            issuer = authorizationServer.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single()
                .TrimEnd('/');

            ClientOAuthOptions oauth = CreateOAuthOptions(backchannel: null);
            await using McpClient client = await ConnectAsync(
                serverArguments: [$"--authorization-server={issuer}"],
                configureHttp: options => options.OAuth = oauth);

            CallToolResult result = await client.CallToolAsync(
                "echo",
                new Dictionary<string, object?> { ["message"] = "default backchannel" },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("default backchannel", GetText(result));
        }
        finally
        {
            await authorizationServer.DisposeAsync();
        }
    }

    private async Task<McpClient> ConnectAsync(
        string[]? serverArguments = null,
        Uri? endpoint = null,
        Action<HttpClientTransportOptions>? configureHttp = null,
        McpClientOptions? clientOptions = null,
        Action<string>? standardErrorLine = null)
    {
        HttpOverStdioClientTransport transport = CreateTransport(
            serverArguments ?? [],
            endpoint,
            configureHttp,
            standardErrorLine);
        return await McpClient.CreateAsync(
            transport,
            clientOptions ?? CreateModernClientOptions(),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private HttpOverStdioClientTransport CreateTransport(
        string[] serverArguments,
        Uri? endpoint = null,
        Action<HttpClientTransportOptions>? configureHttp = null,
        Action<string>? standardErrorLine = null)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "TestHttpOverStdioServer.exe");
        string assembly = Path.Combine(AppContext.BaseDirectory, "TestHttpOverStdioServer.dll");
        StdioClientTransportOptions stdioOptions = new()
        {
            Name = "HTTP over stdio integration server",
            Command = OperatingSystem.IsWindows() ? executable : "dotnet",
            Arguments = OperatingSystem.IsWindows()
                ? [.. serverArguments]
                : [assembly, .. serverArguments],
            StandardErrorLines = standardErrorLine,
            ShutdownTimeout = TestConstants.DefaultTimeout,
        };
        HttpClientTransportOptions httpOptions = new()
        {
            Endpoint = endpoint ?? s_endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        };
        configureHttp?.Invoke(httpOptions);

        return new HttpOverStdioClientTransport(stdioOptions, httpOptions, LoggerFactory);
    }

    private static McpClientOptions CreateModernClientOptions() =>
        new() { ProtocolVersion = "2026-07-28" };

    private static ClientOAuthOptions CreateOAuthOptions(HttpClient? backchannel) =>
        new()
        {
            RedirectUri = new Uri("http://127.0.0.1/callback"),
            Backchannel = backchannel,
            AuthorizationCallbackHandler = (context, _) =>
                Task.FromResult<AuthorizationResult?>(new AuthorizationResult
                {
                    Code = "authorization-code",
                    State = GetQueryParameter(context.AuthorizationUri, "state"),
                }),
        };

    private static async Task<HttpResponseMessage> CreateTokenResponseAsync(
        HttpRequestMessage request,
        List<string> grantTypes)
    {
        string form = await request.Content!.ReadAsStringAsync();
        bool refresh = form.Contains("grant_type=refresh_token", StringComparison.Ordinal);
        grantTypes.Add(refresh ? "refresh_token" : "authorization_code");
        return JsonResponse(refresh
            ? """{"access_token":"test-access-token","token_type":"Bearer","expires_in":3600}"""
            : """{"access_token":"test-access-token","refresh_token":"refresh-token","token_type":"Bearer","expires_in":0}""");
    }

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

    private static string GetText(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string? GetQueryParameter(Uri uri, string name)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&'))
        {
            int separatorIndex = pair.IndexOf('=');
            if (separatorIndex > 0 &&
                string.Equals(pair[..separatorIndex], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[(separatorIndex + 1)..].Replace("+", " "));
            }
        }

        return null;
    }

    private sealed class SynchronousProgress(Action<ProgressNotificationValue> callback)
        : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) => callback(value);
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return callback(request);
        }
    }
}

[CollectionDefinition(nameof(HttpOverStdioIntegrationCollection), DisableParallelization = true)]
public sealed class HttpOverStdioIntegrationCollection;
