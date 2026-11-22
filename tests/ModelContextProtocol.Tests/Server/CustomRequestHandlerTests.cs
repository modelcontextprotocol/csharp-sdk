#pragma warning disable MCPEXP002

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class CustomRequestHandlerTests(ITestOutputHelper output) : LoggedTest(output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CustomHandler_RespectsBorrowedScopeOwnership(bool scopeRequests)
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
                RequestHandlers =
                [
                    new McpServerRequestHandler
                    {
                        Method = "custom/scope",
                        Handler = (context, _) =>
                        {
                            requestDependency = context.Services!.GetRequiredService<AsyncDependency>();
                            return new ValueTask<JsonNode?>(new JsonObject());
                        },
                    },
                ],
            };
            await InvokeAsync(options, scope.ServiceProvider, async client =>
                await client.SendRequestAsync(new JsonRpcRequest { Method = "custom/scope" }, TestContext.Current.CancellationToken));

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
    public async Task CustomHandlers_IsolateConcurrentClientCapabilities()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        var options = new McpServerOptions
        {
            RequestHandlers =
            [
                new McpServerRequestHandler
                {
                    Method = "custom/context",
                    Handler = async (context, cancellationToken) =>
                    {
                        if (Interlocked.Increment(ref count) == 2)
                        {
                            entered.TrySetResult(true);
                        }

                        await release.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
                        return new JsonObject
                        {
                            ["client"] = context.Server.ClientInfo?.Name,
                            ["extension"] = context.Server.ClientCapabilities?.Extensions?.Keys.Single(),
                        };
                    },
                },
            ],
        };
        await InvokeAsync(options, services: null, async client =>
        {
            var first = client.SendRequestAsync(CreateRequest("first"), TestContext.Current.CancellationToken);
            var second = client.SendRequestAsync(CreateRequest("second"), TestContext.Current.CancellationToken);
            try
            {
                await entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            }
            finally
            {
                release.TrySetResult(true);
            }

            var responses = await Task.WhenAll(first, second);
            Assert.All(responses, response => Assert.Equal("custom-handler-client", response.Result!["client"]!.GetValue<string>()));
            Assert.Equal("example.com/first", responses[0].Result!["extension"]!.GetValue<string>());
            Assert.Equal("example.com/second", responses[1].Result!["extension"]!.GetValue<string>());
        });

        static JsonRpcRequest CreateRequest(string name) => new()
        {
            Method = "custom/context",
            Params = new JsonObject
            {
                ["_meta"] = new JsonObject
                {
                    [MetaKeys.ClientCapabilities] = new JsonObject
                    {
                        ["extensions"] = new JsonObject { [$"example.com/{name}"] = new JsonObject() },
                    },
                },
            },
        };
    }

    private async Task InvokeAsync(McpServerOptions options, IServiceProvider? services, Func<McpClient, Task> action)
    {
        Pipe input = new(), output = new();
        await using var server = McpServer.Create(new StreamServerTransport(
            input.Reader.AsStream(), output.Writer.AsStream()), options, LoggerFactory, services);
        var running = server.RunAsync(TestContext.Current.CancellationToken);
        try
        {
            var clientOptions = TestMcpClient.CreateOptions();
            clientOptions.ClientInfo = new() { Name = "custom-handler-client", Version = "1" };
            await using var client = await TestMcpClient.CreateAsync(new StreamClientTransport(
                input.Writer.AsStream(), output.Reader.AsStream()), clientOptions, LoggerFactory, TestContext.Current.CancellationToken);
            await action(client);
        }
        finally
        {
            await server.DisposeAsync();
            await running.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
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
