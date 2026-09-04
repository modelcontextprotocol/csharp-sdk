using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text.Json;

namespace ModelContextProtocol.TestHttpOverStdioServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("--exit-immediately", StringComparer.Ordinal))
        {
            for (int i = 0; i < 12; i++)
            {
                Console.Error.WriteLine($"intentional stderr line {i}");
            }

            Environment.ExitCode = 23;
            return;
        }

        bool stateful = args.Contains("--stateful", StringComparer.Ordinal);
        bool listenTcp = args.Contains("--listen-tcp", StringComparer.Ordinal);
        string? allowedHost = GetArgumentValue(args, "--allowed-host=");
        string? authorizationServer = GetArgumentValue(args, "--authorization-server=");

        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration["AllowedHosts"] = allowedHost ?? "*";

        if (listenTcp)
        {
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0));
        }

        builder.Services.AddHttpContextAccessor();

        IMcpServerBuilder mcpBuilder = builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "http-over-stdio-test-server",
                    Version = "1.0.0",
                };
            })
            .WithHttpOverStdioTransport(options =>
            {
                if (stateful)
                {
                    options.SessionMode = HttpServerSessionMode.Stateful;
                }
            })
            .WithTools<TestTools>()
            .WithPrompts<TestPrompts>()
            .WithResources<TestResources>();

        if (stateful)
        {
            mcpBuilder
                .WithSubscribeToResourcesHandler(static (_, _) =>
                    new ValueTask<EmptyResult>(new EmptyResult()))
                .WithUnsubscribeFromResourcesHandler(static (_, _) =>
                    new ValueTask<EmptyResult>(new EmptyResult()));
        }

        var app = builder.Build();

        if (authorizationServer is not null)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/.well-known/oauth-protected-resource")
                {
                    await next(context);
                    return;
                }

                if (context.Request.Path.StartsWithSegments("/mcp") &&
                    !string.Equals(
                        context.Request.Headers.Authorization,
                        "Bearer test-access-token",
                        StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate =
                        $"Bearer resource_metadata=\"http://{context.Request.Host}/.well-known/oauth-protected-resource\"";
                    return;
                }

                await next(context);
            });

            app.MapGet("/.well-known/oauth-protected-resource", (HttpContext context) =>
                Results.Json(new
                {
                    resource = $"http://{context.Request.Host}/mcp",
                    authorization_servers = new[] { authorizationServer },
                    scopes_supported = new[] { "mcp.read" },
                }));
        }

        app.MapGet("/health", () => "healthy");
        app.MapMcp("/mcp");

        await app.StartAsync();

        if (listenTcp)
        {
            string tcpAddress = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses
                .Single(address => address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
            Console.Error.WriteLine($"TCP_ADDRESS:{tcpAddress}");
        }

        Console.Error.WriteLine("HTTP_OVER_STDIO_READY");
        await app.WaitForShutdownAsync();
    }

    private static string? GetArgumentValue(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}

[McpServerToolType]
internal sealed class TestTools(IHttpContextAccessor httpContextAccessor)
{
    private static int s_cancellationCount;
    private static readonly ConcurrentDictionary<string, BarrierState> s_barriers = new(StringComparer.Ordinal);

    [McpServerTool(Name = "echo"), Description("Returns the supplied text.")]
    public static string Echo([Description("Text to return.")] string message) => message;

    [McpServerTool(Name = "inspect"), Description("Returns HTTP request and connection details.")]
    public string Inspect()
    {
        HttpContext context = httpContextAccessor.HttpContext ??
            throw new InvalidOperationException("No active HTTP context.");

        return JsonSerializer.Serialize(new
        {
            protocol = context.Request.Protocol,
            host = context.Request.Host.Value,
            path = context.Request.Path.Value,
            testHeader = context.Request.Headers["X-Transport-Test"].ToString(),
            connectionId = context.Connection.Id,
        });
    }

    [McpServerTool(Name = "delayed-echo"), Description("Returns text after a caller-selected delay.")]
    public static async Task<string> DelayedEcho(string message, int delayMilliseconds, CancellationToken cancellationToken)
    {
        await Task.Delay(delayMilliseconds, cancellationToken);
        return message;
    }

    [McpServerTool(Name = "concurrent-barrier"), Description("Waits until all calls in a batch are running.")]
    public async Task<string> ConcurrentBarrier(
        string batch,
        int expectedCount,
        string message,
        CancellationToken cancellationToken)
    {
        BarrierState state = s_barriers.GetOrAdd(batch, _ => new BarrierState(expectedCount));
        if (state.ExpectedCount != expectedCount)
        {
            throw new InvalidOperationException("All calls in a barrier batch must use the same expected count.");
        }

        if (Interlocked.Increment(ref state.ArrivedCount) == expectedCount)
        {
            state.AllArrived.TrySetResult();
        }

        await state.AllArrived.Task.WaitAsync(cancellationToken);

        HttpContext context = httpContextAccessor.HttpContext ??
            throw new InvalidOperationException("No active HTTP context.");
        return $"{message}|{context.Connection.Id}";
    }

    [McpServerTool(Name = "progress"), Description("Reports progress before returning.")]
    public static async Task<string> Progress(
        RequestContext<CallToolRequestParams> context,
        McpServer server,
        CancellationToken cancellationToken)
    {
        ProgressToken progressToken = context.Params?.ProgressToken ??
            throw new InvalidOperationException("The progress tool requires a progress token.");
        await server.NotifyProgressAsync(
            progressToken,
            new() { Progress = 1, Total = 2, Message = "halfway" },
            cancellationToken: cancellationToken);
        await server.NotifyProgressAsync(
            progressToken,
            new() { Progress = 2, Total = 2, Message = "complete" },
            cancellationToken: cancellationToken);
        return "progress-complete";
    }

    [McpServerTool(Name = "blocking"), Description("Blocks until its request is cancelled.")]
    public static async Task<string> Blocking(
        RequestContext<CallToolRequestParams> context,
        McpServer server,
        CancellationToken cancellationToken)
    {
        ProgressToken progressToken = context.Params?.ProgressToken ??
            throw new InvalidOperationException("The blocking tool requires a progress token.");
        await server.NotifyProgressAsync(
            progressToken,
            new() { Progress = 0, Message = "started" },
            cancellationToken: cancellationToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "unreachable";
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref s_cancellationCount);
            throw;
        }
    }

    [McpServerTool(Name = "cancellation-count"), Description("Returns the number of cancelled blocking calls.")]
    public static int CancellationCount() => Volatile.Read(ref s_cancellationCount);

    [McpServerTool(Name = "fail"), Description("Throws an application exception.")]
    public static string Fail() => throw new InvalidOperationException("intentional tool failure");

    [McpServerTool(Name = "mrtr-elicit"), Description("Exercises an MRTR elicitation round trip.")]
    public static string MrtrElicit(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("user_input", out InputResponse? response))
        {
            return $"elicit-ok:{response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)?.Action}";
        }

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "Please confirm",
                    RequestedSchema = new(),
                }),
            },
            requestState: "elicit-state");
    }

    [McpServerTool(Name = "notify-resource"), Description("Sends a resource update notification.")]
    public static async Task<string> NotifyResource(McpServer server, CancellationToken cancellationToken)
    {
        await server.SendNotificationAsync(
            NotificationMethods.ResourceUpdatedNotification,
            new ResourceUpdatedNotificationParams { Uri = "test://http-over-stdio/resource" },
            cancellationToken: cancellationToken);
        return "notified";
    }

    private sealed class BarrierState(int expectedCount)
    {
        public int ExpectedCount { get; } = expectedCount;

        public int ArrivedCount;

        public TaskCompletionSource AllArrived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

[McpServerPromptType]
internal sealed class TestPrompts
{
    [McpServerPrompt(Name = "greeting"), Description("Returns a greeting prompt.")]
    public static string Greeting(string name) => $"Hello, {name}.";
}

[McpServerResourceType]
internal sealed class TestResources
{
    [McpServerResource(
        UriTemplate = "test://http-over-stdio/resource",
        Name = "HTTP over stdio resource",
        MimeType = "text/plain")]
    [Description("A resource served through HTTP over stdio.")]
    public static string Read() => "resource-over-http2-stdio";
}
