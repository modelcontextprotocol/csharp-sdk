using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsMessageFilterTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper, startServer: false)
{
    private static ILogger GetLogger(IServiceProvider? services, string categoryName)
    {
        var loggerFactory = services?.GetRequiredService<ILoggerFactory>() ?? throw new InvalidOperationException("LoggerFactory not available");
        return loggerFactory.CreateLogger(categoryName);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Logs_For_Request()
    {
        List<string> messageTypes = [];

        McpServerBuilder
            .WithMessageFilters(filters =>
            {
                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    var logger = GetLogger(context.Services, "MessageFilter1");
                    logger.LogInformation("MessageFilter1 before");

                    var messageTypeName = context.JsonRpcMessage.GetType().Name;
                    messageTypes.Add(messageTypeName);

                    await next(context, cancellationToken);

                    logger.LogInformation("MessageFilter1 after");
                });

                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    var logger = GetLogger(context.Services, "MessageFilter2");
                    logger.LogInformation("MessageFilter2 before");
                    await next(context, cancellationToken);
                    logger.LogInformation("MessageFilter2 after");
                });
            })
            .WithTools<TestTool>()
            .WithPrompts<TestPrompt>()
            .WithResources<TestResource>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var beforeMessages = MockLoggerProvider.LogMessages.Where(m => m.Message == "MessageFilter1 before").ToList();
        Assert.True(beforeMessages.Count > 0);
        Assert.Equal(LogLevel.Information, beforeMessages[0].LogLevel);
        Assert.Equal("MessageFilter1", beforeMessages[0].Category);

        var afterMessages = MockLoggerProvider.LogMessages.Where(m => m.Message == "MessageFilter1 after").ToList();
        Assert.True(afterMessages.Count > 0);
        Assert.Equal(LogLevel.Information, afterMessages[0].LogLevel);
        Assert.Equal("MessageFilter1", afterMessages[0].Category);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Intercepts_Request_Messages()
    {
        List<string> messageTypes = [];

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                var messageTypeName = context.JsonRpcMessage.GetType().Name;
                messageTypes.Add(messageTypeName);
                await next(context, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The message filter should intercept JsonRpcRequest messages
        Assert.Contains("JsonRpcRequest", messageTypes);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Multiple_Filters_Execute_In_Order()
    {
        McpServerBuilder
            .WithMessageFilters(filters =>
            {
                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    var logger = GetLogger(context.Services, "MessageFilter1");
                    logger.LogInformation("MessageFilter1 before");
                    await next(context, cancellationToken);
                    logger.LogInformation("MessageFilter1 after");
                });

                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    var logger = GetLogger(context.Services, "MessageFilter2");
                    logger.LogInformation("MessageFilter2 before");
                    await next(context, cancellationToken);
                    logger.LogInformation("MessageFilter2 after");
                });
            })
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var logMessages = MockLoggerProvider.LogMessages
            .Where(m => m.Category.StartsWith("MessageFilter"))
            .Select(m => m.Message)
            .ToList();

        // First filter registered is outermost
        // We should see this pattern for each message: MessageFilter1 before -> MessageFilter2 before -> MessageFilter2 after -> MessageFilter1 after
        int idx1Before = logMessages.IndexOf("MessageFilter1 before");
        int idx2Before = logMessages.IndexOf("MessageFilter2 before");
        int idx2After = logMessages.IndexOf("MessageFilter2 after");
        int idx1After = logMessages.IndexOf("MessageFilter1 after");

        Assert.True(idx1Before >= 0);
        Assert.True(idx2Before >= 0);
        Assert.True(idx2After >= 0);
        Assert.True(idx1After >= 0);

        // Verify ordering within a single request
        Assert.True(idx1Before < idx2Before);
        Assert.True(idx2Before < idx2After);
        Assert.True(idx2After < idx1After);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Has_Access_To_Server()
    {
        McpServer? capturedServer = null;

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                capturedServer = context.Server;
                await next(context, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The captured server is a per-destination wrapper that provides the same functionality
        Assert.NotNull(capturedServer);
        Assert.NotNull(capturedServer.ServerOptions);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Items_Dictionary_Can_Be_Used()
    {
        string? capturedValue = null;

        McpServerBuilder
            .WithMessageFilters(filters =>
            {
                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    context.Items["testKey"] = "testValue";
                    await next(context, cancellationToken);
                });

                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    if (context.Items.TryGetValue("testKey", out var value))
                    {
                        capturedValue = value as string;
                    }
                    await next(context, cancellationToken);
                });
            })
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("testValue", capturedValue);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Can_Access_JsonRpcMessage_Details()
    {
        string? capturedMethod = null;

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                {
                    capturedMethod = request.Method;
                }
                await next(context, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RequestMethods.ToolsList, capturedMethod);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Exception_Propagates_Properly()
    {
        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                // Only throw for tools/list, not for initialize/initialized
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                {
                    throw new InvalidOperationException("Filter exception");
                }
                await next(context, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
        {
            await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        });

        Assert.Contains("error", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Runs_Before_Request_Specific_Filters()
    {
        var executionOrder = new List<string>();

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                {
                    executionOrder.Add("MessageFilter");
                }
                await next(context, cancellationToken);
            }))
            .WithRequestFilters(filters => filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
            {
                executionOrder.Add("ListToolsFilter");
                return await next(request, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Message filter should run before the request-specific filter
        Assert.Equal(2, executionOrder.Count);
        Assert.Equal("MessageFilter", executionOrder[0]);
        Assert.Equal("ListToolsFilter", executionOrder[1]);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Can_Skip_Default_Handlers()
    {
        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                // Skip calling next for tools/list
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                {
                    // Don't call next - this will skip the default handler
                    return;
                }
                await next(context, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        // When default handlers are skipped, the request should time out
        // because no response will be sent
        using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.ListToolsAsync(cancellationToken: requestCts.Token);
        });
    }

    [Fact]
    public async Task AddOutgoingMessageFilter_Sees_Initialize_Progress_And_Response()
    {
        var observedMessages = new List<string>();

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddOutgoingFilter((next) => async (context, cancellationToken) =>
            {
                switch (context.JsonRpcMessage)
                {
                    case JsonRpcResponse response when response.Result is JsonObject result:
                        if (result.ContainsKey("protocolVersion"))
                        {
                            observedMessages.Add("initialize");
                        }
                        else if (result.ContainsKey("content"))
                        {
                            observedMessages.Add("response");
                        }
                        break;
                    case JsonRpcNotification notification when notification.Method == NotificationMethods.ProgressNotification:
                        observedMessages.Add("progress");
                        break;
                }

                await next(context, cancellationToken);
            }))
            .WithTools<ProgressTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        IProgress<ProgressNotificationValue> progress = new Progress<ProgressNotificationValue>(_ => { });
        await client.CallToolAsync("progress-tool", progress: progress, cancellationToken: TestContext.Current.CancellationToken);

        int initializeIndex = observedMessages.IndexOf("initialize");
        int progressIndex = observedMessages.IndexOf("progress");
        int responseIndex = observedMessages.LastIndexOf("response");

        Assert.True(initializeIndex >= 0);
        Assert.True(progressIndex > initializeIndex);
        Assert.True(responseIndex > progressIndex);
    }

    [Fact]
    public async Task AddOutgoingMessageFilter_Can_Skip_Sending_Messages()
    {
        McpServerBuilder
            .WithMessageFilters(filters => filters.AddOutgoingFilter((next) => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcResponse response && response.Result is JsonObject result && result.ContainsKey("tools"))
                {
                    return;
                }

                await next(context, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.ListToolsAsync(cancellationToken: requestCts.Token);
        });
    }

    [Fact]
    public async Task AddOutgoingMessageFilter_Can_Send_Additional_Messages()
    {
        McpServerBuilder
            .WithMessageFilters(filters => filters.AddOutgoingFilter((next) => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcResponse response && response.Result is JsonObject result && result.ContainsKey("tools"))
                {
                    var extraNotification = new JsonRpcNotification
                    {
                        Method = "test/extra",
                        Params = new JsonObject { ["message"] = "extra" },
                        Context = new JsonRpcMessageContext { RelatedTransport = context.JsonRpcMessage.Context?.RelatedTransport },
                    };

                    await next(new MessageContext(context.Server, extraNotification), cancellationToken);
                }

                await next(context, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        var extraNotificationReceived = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = client.RegisterNotificationHandler("test/extra", (notification, _) =>
        {
            extraNotificationReceived.TrySetResult(notification.Params?["message"]?.GetValue<string>());
            return default;
        });

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var extraMessage = await extraNotificationReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal("extra", extraMessage);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Items_Flow_To_Request_Filters()
    {
        string? capturedValue = null;

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                // Set an item in the message filter
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                {
                    context.Items["messageFilterKey"] = "messageFilterValue";
                }
                await next(context, cancellationToken);
            }))
            .WithRequestFilters(filters => filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
            {
                // Read the item in the request-specific filter
                if (request.Items.TryGetValue("messageFilterKey", out var value))
                {
                    capturedValue = value as string;
                }
                return await next(request, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("messageFilterValue", capturedValue);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Items_Flow_To_CallTool_Handler()
    {
        object? capturedValue = null;

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                // Set an item in the message filter for CallTool requests
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsCall)
                {
                    context.Items["toolContextKey"] = 42;
                }
                await next(context, cancellationToken);
            }))
            .WithRequestFilters(filters => filters.AddCallToolFilter((next) => async (request, cancellationToken) =>
            {
                // Read the item in the call tool filter
                if (request.Items.TryGetValue("toolContextKey", out var value))
                {
                    capturedValue = value;
                }
                return await next(request, cancellationToken);
            }))
            .WithTools<SimpleTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.CallToolAsync("simple-tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, capturedValue);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_User_Flows_To_CallTool_Handler()
    {
        ClaimsPrincipal? capturedUser = null;

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                // Set a custom user in the message filter for CallTool requests
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsCall)
                {
                    var claims = new[] { new Claim(ClaimTypes.Name, "TestUser"), new Claim(ClaimTypes.Role, "Admin") };
                    var identity = new ClaimsIdentity(claims, "TestAuth");
                    context.User = new ClaimsPrincipal(identity);
                }
                await next(context, cancellationToken);
            }))
            .WithRequestFilters(filters => filters.AddCallToolFilter((next) => async (request, cancellationToken) =>
            {
                // Read the user in the call tool filter
                capturedUser = request.User;
                return await next(request, cancellationToken);
            }))
            .WithTools<SimpleTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.CallToolAsync("simple-tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedUser);
        Assert.Equal("TestUser", capturedUser.Identity?.Name);
        Assert.True(capturedUser.IsInRole("Admin"));
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Items_Preserved_When_Context_Replaced()
    {
        object? firstFilterValue = null;
        object? secondFilterValue = null;

        McpServerBuilder
            .WithMessageFilters(filters =>
            {
                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    // First filter sets an item
                    if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                    {
                        context.Items["firstFilterKey"] = "firstFilterValue";
                    }
                    await next(context, cancellationToken);
                });

                filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
                {
                    // Second filter creates a new context with a new JsonRpcRequest and adds an item
                    if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                    {
                        var newRequest = new JsonRpcRequest
                        {
                            Id = request.Id,
                            Method = RequestMethods.ToolsList,
                            Params = request.Params,
                            Context = new JsonRpcMessageContext { RelatedTransport = request.Context?.RelatedTransport },
                        };

                        var newContext = new MessageContext(context.Server, newRequest);
                        newContext.Items["secondFilterKey"] = "secondFilterValue";

                        await next(newContext, cancellationToken);
                        return;
                    }
                    await next(context, cancellationToken);
                });
            })
            .WithRequestFilters(filters => filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
            {
                // Request filter should see items from message filters
                request.Items.TryGetValue("firstFilterKey", out firstFilterValue);
                request.Items.TryGetValue("secondFilterKey", out secondFilterValue);
                return await next(request, cancellationToken);
            }))
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(firstFilterValue);
        Assert.Equal("secondFilterValue", secondFilterValue);
    }

    [Fact]
    public async Task AddIncomingMessageFilter_Items_Flow_Through_Multiple_Request_Filters()
    {
        var observedValues = new List<string>();

        McpServerBuilder
            .WithMessageFilters(filters => filters.AddIncomingFilter((next) => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.ToolsList)
                {
                    context.Items["sharedKey"] = "fromMessageFilter";
                }
                await next(context, cancellationToken);
            }))
            .WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
                {
                    // First request filter reads and modifies
                    if (request.Items.TryGetValue("sharedKey", out var value))
                    {
                        observedValues.Add((string)value!);
                        request.Items["sharedKey"] = "modifiedByFilter1";
                    }
                    return await next(request, cancellationToken);
                });

                filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
                {
                    // Second request filter should see modified value
                    if (request.Items.TryGetValue("sharedKey", out var value))
                    {
                        observedValues.Add((string)value!);
                    }
                    return await next(request, cancellationToken);
                });
            })
            .WithTools<TestTool>();

        StartServer();

        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, observedValues.Count);
        Assert.Equal("fromMessageFilter", observedValues[0]);
        Assert.Equal("modifiedByFilter1", observedValues[1]);
    }

    [McpServerToolType]
    public sealed class TestTool
    {
        [McpServerTool]
        public static string TestToolMethod()
        {
            return "test result";
        }
    }

    [McpServerPromptType]
    public sealed class TestPrompt
    {
        [McpServerPrompt]
        public static Task<GetPromptResult> TestPromptMethod()
        {
            return Task.FromResult(new GetPromptResult
            {
                Description = "Test prompt",
                Messages = [new() { Role = Role.User, Content = new TextContentBlock { Text = "Test" } }]
            });
        }
    }

    [McpServerResourceType]
    public sealed class TestResource
    {
        [McpServerResource(UriTemplate = "test://resource/{id}")]
        public static string TestResourceMethod(string id)
        {
            return $"Test resource for ID: {id}";
        }
    }

    [McpServerToolType]
    public sealed class ProgressTool
    {
        [McpServerTool(Name = "progress-tool")]
        public static async Task<string> ReportProgress(
            McpServer server,
            RequestContext<CallToolRequestParams> context,
            CancellationToken cancellationToken)
        {
            if (context.Params?.ProgressToken is { } token)
            {
                await server.NotifyProgressAsync(token, new ProgressNotificationValue
                {
                    Progress = 0,
                    Total = 2,
                    Message = "starting",
                }, cancellationToken: cancellationToken);

                await server.NotifyProgressAsync(token, new ProgressNotificationValue
                {
                    Progress = 1,
                    Total = 2,
                    Message = "running",
                }, cancellationToken: cancellationToken);
            }

            return "done";
        }
    }

    [McpServerToolType]
    public sealed class SimpleTool
    {
        [McpServerTool(Name = "simple-tool")]
        public static string Execute()
        {
            return "success";
        }
    }
}
