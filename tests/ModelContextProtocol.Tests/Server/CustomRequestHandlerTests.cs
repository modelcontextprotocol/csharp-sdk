using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class CustomRequestHandlerTests(ITestOutputHelper output) : ClientServerTestBase(output, startServer: false)
{
#pragma warning disable MCPEXP002
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CustomHandler_ReceivesRawParametersAndHonorsScopeRequests(bool scopeRequests)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "reader")], "Test"));
        var item = new object();
        ServiceCollection.Configure<McpServerOptions>(options =>
        {
            options.ScopeRequests = scopeRequests;
            options.RequestHandlers =
            [
                new McpServerRequestHandler
                {
                    Method = "custom/echo",
                    Handler = (context, cancellationToken) =>
                    {
                        Assert.Same(context.JsonRpcRequest.Params, context.Params);
                        Assert.Equal("custom/echo", context.JsonRpcRequest.Method);
                        Assert.Same(user, context.User);
                        Assert.Same(item, context.Items["item"]);
                        Assert.Equal("original", context.Params!["_meta"]!["example.com/original"]!.GetValue<string>());
                        Assert.Equal("added", context.Params["_meta"]!["example.com/incoming"]!.GetValue<string>());
                        Assert.NotSame(Server, context.Server);
                        Assert.Same(Server.Services, context.Server.Services);
                        if (scopeRequests)
                        {
                            Assert.NotSame(Server.Services, context.Services);
                        }
                        else
                        {
                            Assert.Same(Server.Services, context.Services);
                        }
                        return new ValueTask<JsonNode?>(new JsonObject { ["echo"] = context.Params!["value"]!.DeepClone() });
                    },
                },
            ];
        });
        McpServerBuilder.WithMessageFilters(filters =>
        {
            filters.AddIncomingFilter(next => (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest { Method: "custom/echo" } request)
                {
                    context.User = user;
                    context.Items["item"] = item;
                    request.Params!["_meta"]!["example.com/incoming"] = "added";
                }
                return next(context, cancellationToken);
            });
            filters.AddOutgoingFilter(next => (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcResponse { Result: JsonObject result } && result.ContainsKey("echo"))
                {
                    result["filtered"] = true;
                }
                return next(context, cancellationToken);
            });
        });
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var response = await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = "custom/echo",
            Params = new JsonObject
            {
                ["value"] = 42,
                ["_meta"] = new JsonObject { ["example.com/original"] = "original" },
            },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(42, response.Result!["echo"]!.GetValue<int>());
        Assert.True(response.Result["filtered"]!.GetValue<bool>());
        Assert.Null(response.Result["resultType"]);
    }

    [Fact]
    public async Task CustomHandlers_ReceivePeerInfoAndIsolateConcurrentCapabilities()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        ServiceCollection.Configure<McpServerOptions>(options => options.RequestHandlers =
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
        ]);
        StartServer();
        var clientOptions = TestMcpClient.CreateOptions();
        clientOptions.ClientInfo = new() { Name = "custom-handler-client", Version = "1" };
        await using var client = await CreateMcpClientForServer(clientOptions);

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

    [Fact]
    public async Task CustomHandler_CanSendMessagesThroughTheRequestServer()
    {
        ServiceCollection.Configure<McpServerOptions>(options => options.RequestHandlers =
        [
            new McpServerRequestHandler
            {
                Method = "custom/notify",
                Handler = async (context, cancellationToken) =>
                {
                    await context.Server.SendMessageAsync(
                        new JsonRpcNotification { Method = "custom/notification" }, cancellationToken);
                    return new JsonObject();
                },
            },
        ]);
        StartServer();
        await using var client = await CreateMcpClientForServer();
        var notified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = client.RegisterNotificationHandler("custom/notification", (_, _) =>
        {
            notified.SetResult(true);
            return default;
        });

        await client.SendRequestAsync(
            new JsonRpcRequest { Method = "custom/notify" }, TestContext.Current.CancellationToken);

        await notified.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
    }
#pragma warning restore MCPEXP002
}
