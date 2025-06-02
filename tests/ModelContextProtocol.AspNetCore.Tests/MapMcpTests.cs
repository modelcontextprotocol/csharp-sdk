using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore.Tests;

public abstract class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    protected abstract bool UseStreamableHttp { get; }
    protected abstract bool Stateless { get; }

    protected void ConfigureStateless(HttpServerTransportOptions options)
    {
        options.Stateless = Stateless;
    }

    protected async Task<IMcpClient> ConnectAsync(string? path = null, SseClientTransportOptions? options = null)
    {
        // Default behavior when no options are provided
        path ??= UseStreamableHttp ? "/" : "/sse";

        await using var transport = new SseClientTransport(options ?? new SseClientTransportOptions()
        {
            Endpoint = new Uri($"http://localhost{path}"),
            TransportMode = UseStreamableHttp ? HttpTransportMode.StreamableHttp : HttpTransportMode.Sse,
        }, HttpClient, LoggerFactory);

        return await McpClientFactory.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MapMcp_ThrowsInvalidOperationException_IfWithHttpTransportIsNotCalled()
    {
        Builder.Services.AddMcpServer();
        await using var app = Builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => app.MapMcp());
        Assert.StartsWith("You must call WithHttpTransport()", exception.Message);
    }

    [Fact]
    public async Task Can_UseIHttpContextAccessor_InTool()
    {
        Assert.SkipWhen(UseStreamableHttp && !Stateless,
            """
            IHttpContextAccessor is not currently supported with non-stateless Streamable HTTP.
            TODO: Support it in stateless mode by manually capturing and flowing execution context.
            """);

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        Builder.Services.AddHttpContextAccessor();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            return async context =>
            {
                context.User = CreateUser("TestUser");
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var mcpClient = await ConnectAsync();

        var response = await mcpClient.CallToolAsync(
            "EchoWithUserName",
            new Dictionary<string, object?>() { ["message"] = "Hello world!" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(response.Content);
        Assert.Equal("TestUser: Hello world!", content.Text);
    }

    [Fact]
    public async Task Messages_FromNewUser_AreRejected()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        // Add an authentication scheme that will send a 403 Forbidden response.
        Builder.Services.AddAuthentication().AddBearerToken();
        Builder.Services.AddHttpContextAccessor();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            var i = 0;
            return async context =>
            {
                context.User = CreateUser($"TestUser{Interlocked.Increment(ref i)}");
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var httpRequestException = await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync());
        Assert.Equal(HttpStatusCode.Forbidden, httpRequestException.StatusCode);
    }

    protected ClaimsPrincipal CreateUser(string name)
        => new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("name", name), new Claim(ClaimTypes.NameIdentifier, name)],
            "TestAuthType", "name", "role"));

    [McpServerToolType]
    protected class EchoHttpContextUserTools(IHttpContextAccessor contextAccessor)
    {
        [McpServerTool, Description("Echoes the input back to the client with their user name.")]
        public string EchoWithUserName(string message)
        {
            var httpContext = contextAccessor.HttpContext ?? throw new Exception("HttpContext unavailable!");
            var userName = httpContext.User.Identity?.Name ?? "anonymous";
            return $"{userName}: {message}";
        }
    }
}
