using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class ContextualRequestHandlerTests(ITestOutputHelper output) : ClientServerTestBase(output, startServer: false)
{
#pragma warning disable MCPEXP002
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextualHandler_ReceivesRawParametersAndHonorsScopeRequests(bool scopeRequests)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "reader")], "Test"));
        var item = new object();
        ServiceCollection.Configure<McpServerOptions>(options =>
        {
            options.ScopeRequests = scopeRequests;
            options.ContextualRequestHandlers =
            [
                new McpServerContextualRequestHandler
                {
                    Method = "custom/echo",
                    Handler = (context, cancellationToken) =>
                    {
                        Assert.Same(context.JsonRpcRequest.Params, context.Params);
                        Assert.Equal("custom/echo", context.JsonRpcRequest.Method);
                        Assert.Same(user, context.User);
                        Assert.Same(item, context.Items["item"]);
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
        McpServerBuilder.WithMessageFilters(filters => filters.AddIncomingFilter(next => (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest { Method: "custom/echo" })
            {
                context.User = user;
                context.Items["item"] = item;
            }
            return next(context, cancellationToken);
        }));
        StartServer();
        await using var client = await CreateMcpClientForServer();

        var response = await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = "custom/echo",
            Params = new JsonObject { ["value"] = 42 },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(42, response.Result!["echo"]!.GetValue<int>());
        Assert.Null(response.Result["resultType"]);
    }

    [Fact]
    public async Task ContextualHandler_CanSendMessagesThroughTheRequestServer()
    {
        ServiceCollection.Configure<McpServerOptions>(options => options.ContextualRequestHandlers =
        [
            new McpServerContextualRequestHandler
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
