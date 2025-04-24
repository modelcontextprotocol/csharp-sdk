using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore.Tests;

public abstract class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    protected abstract bool UseStreamableHttp { get; }

    protected async Task<IMcpClient> ConnectAsync(string? path = null)
    {
        path ??= UseStreamableHttp ? "/" : "/sse";

        var sseClientTransportOptions = new SseClientTransportOptions()
        {
            Endpoint = new Uri($"http://localhost{path}"),
            UseStreamableHttp = UseStreamableHttp,
        };
        await using var transport = new SseClientTransport(sseClientTransportOptions, HttpClient, LoggerFactory);
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

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/secondary")]
    public async Task Allows_Customizing_Route(string pattern)
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.MapMcp(pattern);

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await HttpClient.GetAsync($"http://localhost{pattern}/sse", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var sseStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var sseStreamReader = new StreamReader(sseStream, System.Text.Encoding.UTF8);
        var eventLine = await sseStreamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        var dataLine = await sseStreamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(eventLine);
        Assert.Equal("event: endpoint", eventLine);
        Assert.NotNull(dataLine);
        Assert.Equal($"data: {pattern}/message", dataLine[..dataLine.IndexOf('?')]);
    }

    [Fact]
    public async Task Messages_FromNewUser_AreRejected()
    {
        Builder.Services.AddMcpServer().WithHttpTransport().WithTools<EchoHttpContextUserTools>();

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
