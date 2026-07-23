#pragma warning disable MCPEXP003

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Server;

public class McpAppElicitationTests
{
    [Fact]
    public void AddClientCapabilities_AddsCoreAndAppsElicitationCapabilities()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());

        Assert.NotNull(capabilities.Elicitation?.Form);
        Assert.True(capabilities.Extensions?.ContainsKey("io.modelcontextprotocol/ui"));
        Assert.Single(capabilities.Extensions!);
        Assert.True(McpAppElicitation.IsSupported(capabilities));
        Assert.NotNull(McpApps.GetUiCapability(capabilities)?.Elicitation);
    }

    [Fact]
    public void SetAppUi_RoundTripsResourceUri()
    {
        var request = McpAppElicitation.SetAppUi(CreateRequest(), "ui://portfolio/assign-manager");

        Assert.Equal("ui://portfolio/assign-manager", McpAppElicitation.GetAppUi(request)?.ResourceUri);
        Assert.Equal("ui://portfolio/assign-manager", request.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
    }

    [Fact]
    public void SetAppUiIfSupported_FormOnlyClientLeavesCoreRequestUnchanged()
    {
        var capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        };

        var request = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            capabilities,
            "ui://portfolio/assign-manager");

        Assert.Null(request.Meta);
        Assert.NotNull(request.RequestedSchema);
    }

    [Fact]
    public void SetAppUiIfSupported_AppElicitationClientAddsResourceUri()
    {
        var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());

        var request = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            capabilities,
            "ui://portfolio/assign-manager");

        Assert.Equal("ui://portfolio/assign-manager", McpAppElicitation.GetAppUi(request)?.ResourceUri);
    }

    [Fact]
    public void SetAppUiIfSupported_RequestCapabilitiesTakePrecedenceOverServerCapabilities()
    {
        var server = new Mock<McpServer>();
        server.SetupGet(s => s.ClientCapabilities)
            .Returns(McpAppElicitation.AddClientCapabilities(new ClientCapabilities()));
        var requestCapabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        };
        var context = new RequestContext<CallToolRequestParams>(
            server.Object,
            new JsonRpcRequest
            {
                Id = new RequestId(1),
                Method = RequestMethods.ToolsCall,
                Context = new JsonRpcMessageContext
                {
                    ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
                    ClientCapabilities = requestCapabilities,
                },
            },
            new CallToolRequestParams { Name = "assign_account_manager" });

        var request = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            context,
            "ui://portfolio/assign-manager");

        Assert.Null(request.Meta);
    }

    [Fact]
    public void ResolveOrRequest_FirstRoundReturnsInputRequiredResult()
    {
        var server = new Mock<McpServer>();
        server.SetupGet(s => s.IsMrtrSupported).Returns(true);
        var requestParams = new CallToolRequestParams { Name = "assign_account_manager" };
        var elicitation = McpAppElicitation.SetAppUi(CreateRequest(), "ui://portfolio/assign-manager");

        var exception = Assert.Throws<InputRequiredException>(() => McpAppElicitation.ResolveOrRequest(
            server.Object,
            requestParams,
            "manager-assignment",
            elicitation,
            TestJsonContext.Default.AssignmentResponse,
            "state-v1"));

        Assert.Equal("state-v1", exception.Result.RequestState);
        var input = Assert.Single(exception.Result.InputRequests!);
        Assert.Equal("manager-assignment", input.Key);
        Assert.Equal(RequestMethods.ElicitationCreate, input.Value.Method);
    }

    [Fact]
    public void ResolveOrRequest_RetryReturnsTypedContent()
    {
        var server = new Mock<McpServer>();
        server.SetupGet(s => s.IsMrtrSupported).Returns(true);
        var requestParams = new CallToolRequestParams
        {
            Name = "assign_account_manager",
            RequestState = "state-v1",
            InputResponses = new Dictionary<string, InputResponse>
            {
                ["manager-assignment"] = InputResponse.FromElicitResult(new ElicitResult
                {
                    Action = "accept",
                    Content = new Dictionary<string, JsonElement>
                    {
                        ["confirmed"] = JsonSerializer.SerializeToElement(true),
                        ["selectedManagerId"] = JsonSerializer.SerializeToElement("mgr-priya"),
                    },
                }),
            },
        };

        var result = McpAppElicitation.ResolveOrRequest(
            server.Object,
            requestParams,
            "manager-assignment",
            CreateRequest(),
            TestJsonContext.Default.AssignmentResponse);

        Assert.True(result.IsAccepted);
        Assert.True(result.Content?.Confirmed);
        Assert.Equal("mgr-priya", result.Content?.SelectedManagerId);
    }

    private static ElicitRequestParams CreateRequest() => new()
    {
        Message = "Choose a manager.",
        RequestedSchema = new ElicitRequestParams.RequestSchema(),
    };
}

public sealed class AssignmentResponse
{
    public bool Confirmed { get; set; }

    public string SelectedManagerId { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AssignmentResponse))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}

#if !NET472
public sealed class McpAppElicitationCompatibilityTests : ClientServerTestBase
{
    public McpAppElicitationCompatibilityTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(
        Microsoft.Extensions.DependencyInjection.ServiceCollection services,
        IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options => options.ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion);
        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                CompleteAssignment,
                new McpServerToolCreateOptions
                {
                    Name = "complete-assignment",
                    Description = "Completes an assignment using app-enhanced or native form elicitation.",
                })
        ]);
    }

    [Fact]
    public async Task FormOnlyClient_UsesNativeElicitationAndCompletesMrtrRetry()
    {
        ElicitRequestParams? observedRequest = null;
        var options = CreateClientOptions(new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() },
        });
        options.Handlers.ElicitationHandler = (request, _) =>
        {
            observedRequest = request;
            return new ValueTask<ElicitResult>(CreateAcceptedResult());
        };

        await using var client = await CreateMcpClientForServer(options);
        var result = await client.CallToolAsync(
            "complete-assignment",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(observedRequest);
        Assert.Null(McpAppElicitation.GetAppUi(observedRequest));
        Assert.NotNull(observedRequest.RequestedSchema);
        Assert.Equal("native:mgr-priya", GetText(result));
    }

    [Fact]
    public async Task AppElicitationClient_ReceivesResourceHintAndCompletesMrtrRetry()
    {
        ElicitRequestParams? observedRequest = null;
        var options = CreateClientOptions(
            McpAppElicitation.AddClientCapabilities(new ClientCapabilities()));
        options.Handlers.ElicitationHandler = (request, _) =>
        {
            observedRequest = request;
            return new ValueTask<ElicitResult>(CreateAcceptedResult());
        };

        await using var client = await CreateMcpClientForServer(options);
        var result = await client.CallToolAsync(
            "complete-assignment",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(observedRequest);
        Assert.Equal(
            "ui://portfolio/assign-manager",
            McpAppElicitation.GetAppUi(observedRequest)?.ResourceUri);
        Assert.Equal("app:mgr-priya", GetText(result));
    }

    private static string CompleteAssignment(
        McpServer server,
        RequestContext<CallToolRequestParams> context)
    {
        var elicitation = McpAppElicitation.SetAppUiIfSupported(
            CreateRequest(),
            context,
            "ui://portfolio/assign-manager");
        var presentation = McpAppElicitation.GetAppUi(elicitation) is null ? "native" : "app";
        var response = McpAppElicitation.ResolveOrRequest(
            server,
            context.Params,
            "manager-assignment",
            elicitation,
            TestJsonContext.Default.AssignmentResponse,
            "state-v1");

        return $"{presentation}:{response.Content?.SelectedManagerId}";
    }

    private static McpClientOptions CreateClientOptions(ClientCapabilities capabilities) => new()
    {
        ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        Capabilities = capabilities,
    };

    private static ElicitResult CreateAcceptedResult() => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement>
        {
            ["confirmed"] = JsonSerializer.SerializeToElement(true),
            ["selectedManagerId"] = JsonSerializer.SerializeToElement("mgr-priya"),
        },
    };

    private static string GetText(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static ElicitRequestParams CreateRequest() => new()
    {
        Message = "Choose a manager.",
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
            {
                ["confirmed"] = new ElicitRequestParams.BooleanSchema(),
                ["selectedManagerId"] = new ElicitRequestParams.StringSchema(),
            },
            Required = ["confirmed", "selectedManagerId"],
        },
    };
}
#endif
