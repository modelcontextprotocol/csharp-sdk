using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore.Tests;

public class HttpTaskIntegrationTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    [Fact]
    public async Task WithTasks_CanCallToolOverHttp()
    {
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 })
            .WithTools<TestTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new("http://localhost:5000") },
            HttpClient,
            LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolWithPollingAsync(
            new CallToolRequestParams { Name = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello World!", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task WithTasks_AfterOrdinaryFilter_CanCallToolOverHttp()
    {
        var ordinaryFilterInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Builder.Services
            .AddMcpServer(options =>
            {
                options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
                {
                    ordinaryFilterInvoked.TrySetResult();
                    return await next(request, cancellationToken);
                });
            })
            .WithHttpTransport()
            .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 })
            .WithTools<TestTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new("http://localhost:5000") },
            HttpClient,
            LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolWithPollingAsync(
            new CallToolRequestParams { Name = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello World!", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        await ordinaryFilterInvoked.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithTasks_AuthorizedTool_Completes(bool registerTasksBeforeAuthorization)
    {
        var serverBuilder = Builder.Services
            .AddMcpServer()
            .WithHttpTransport();

        if (registerTasksBeforeAuthorization)
        {
            serverBuilder
                .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 })
                .AddAuthorizationFilters();
        }
        else
        {
            serverBuilder
                .AddAuthorizationFilters()
                .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 });
        }

        serverBuilder.WithTools<TestTools>();
        Builder.Services.AddAuthorization();

        await using var app = Builder.Build();
        app.Use(next => async context =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-user")],
                "TestAuthType"));
            await next(context);
        });
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new("http://localhost:5000") },
            HttpClient,
            LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolWithPollingAsync(
            new CallToolRequestParams { Name = "authorized-test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Authorized", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithTasks_UnauthorizedTool_DoesNotCreateTask(bool registerTasksBeforeAuthorization)
    {
        var taskStore = new Mock<IMcpTaskStore>(MockBehavior.Strict);
        var serverBuilder = Builder.Services
            .AddMcpServer()
            .WithHttpTransport();

        if (registerTasksBeforeAuthorization)
        {
            serverBuilder
                .WithTasks(taskStore.Object)
                .AddAuthorizationFilters();
        }
        else
        {
            serverBuilder
                .AddAuthorizationFilters()
                .WithTasks(taskStore.Object);
        }

        serverBuilder.WithTools<TestTools>();
        Builder.Services.AddAuthorization();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new("http://localhost:5000") },
            HttpClient,
            LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsTaskAsync(
                new CallToolRequestParams { Name = "authorized-test" },
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(McpErrorCode.InvalidRequest, exception.ErrorCode);
        taskStore.Verify(
            store => store.CreateTaskAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [McpServerToolType]
    private sealed class TestTools
    {
        [McpServerTool(Name = "test")]
        public static string Test() => "Hello World!";

        [McpServerTool(Name = "authorized-test")]
        [Authorize]
        public static string AuthorizedTest() => "Authorized";
    }
}
