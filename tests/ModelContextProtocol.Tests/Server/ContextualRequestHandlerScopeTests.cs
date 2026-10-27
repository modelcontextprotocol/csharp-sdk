using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class ContextualRequestHandlerScopeTests(ITestOutputHelper output) : LoggedTest(output)
{
#pragma warning disable MCPEXP002
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextualHandler_RespectsBorrowedScopeOwnership(bool scopeRequests)
    {
        var services = new ServiceCollection().AddScoped<AsyncDependency>();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        AsyncDependency? requestDependency = null;
        AsyncDependency borrowedDependency;
        await using (var scope = provider.CreateAsyncScope())
        {
            borrowedDependency = scope.ServiceProvider.GetRequiredService<AsyncDependency>();
            var options = new McpServerOptions
            {
                ScopeRequests = scopeRequests,
                ContextualRequestHandlers =
                [
                    new McpServerContextualRequestHandler
                    {
                        Method = "custom/scope",
                        Handler = (context, cancellationToken) =>
                        {
                            requestDependency = context.Services!.GetRequiredService<AsyncDependency>();
                            return new ValueTask<JsonNode?>(new JsonObject());
                        },
                    },
                ],
            };
            await InvokeAsync(options, scope.ServiceProvider);

            Assert.NotNull(requestDependency);
            Assert.Equal(0, borrowedDependency.DisposeCount);
            if (scopeRequests)
            {
                Assert.NotSame(borrowedDependency, requestDependency);
                Assert.Equal(1, requestDependency.DisposeCount);
            }
            else
            {
                Assert.Same(borrowedDependency, requestDependency);
                Assert.Equal(0, requestDependency.DisposeCount);
            }
        }
        Assert.Equal(1, borrowedDependency.DisposeCount);
        Assert.Equal(1, requestDependency.DisposeCount);
    }

    [Fact]
    public async Task ContextualHandler_WorksWithoutAServiceProvider()
    {
        var options = new McpServerOptions
        {
            ContextualRequestHandlers =
            [
                new McpServerContextualRequestHandler
                {
                    Method = "custom/scope",
                    Handler = (context, cancellationToken) =>
                    {
                        Assert.Null(context.Services);
                        Assert.Null(context.Server.Services);
                        return new ValueTask<JsonNode?>(new JsonObject());
                    },
                },
            ],
        };

        await InvokeAsync(options, services: null);
    }
#pragma warning restore MCPEXP002

    private async Task InvokeAsync(McpServerOptions options, IServiceProvider? services)
    {
        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, options, LoggerFactory, services);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);
        try
        {
            await SendAsync(new JsonRpcRequest
            {
                Id = new RequestId(1),
                Method = RequestMethods.Initialize,
                Params = JsonSerializer.SerializeToNode(new InitializeRequestParams
                {
                    ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
                    Capabilities = new(),
                    ClientInfo = new() { Name = "test-client", Version = "1" },
                }, McpJsonUtilities.DefaultOptions),
            });
            await SendAsync(new JsonRpcRequest { Id = new RequestId(2), Method = "custom/scope" });
        }
        finally
        {
            await transport.DisposeAsync();
            await runTask.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }

        async Task SendAsync(JsonRpcRequest request)
        {
            var response = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.OnMessageSent = message =>
            {
                if (message is JsonRpcResponse result && result.Id == request.Id ||
                    message is JsonRpcError error && error.Id == request.Id)
                {
                    response.TrySetResult(message);
                }
            };
            await transport.SendClientMessageAsync(request, TestContext.Current.CancellationToken);
            Assert.IsType<JsonRpcResponse>(
                await response.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
        }
    }

    private sealed class AsyncDependency : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposeCount++;
        }
    }
}
