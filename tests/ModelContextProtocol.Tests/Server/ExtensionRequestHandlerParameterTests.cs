using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class ExtensionRequestHandlerParameterTests : ClientServerTestBase
{
    private const string AlphaUri = "skill://alpha/SKILL.md";
    private const string BetaUri = "skill://beta/SKILL.md";
    private readonly Mock<IMcpTaskStore> _store = new(MockBehavior.Strict);

    public ExtensionRequestHandlerParameterTests(ITestOutputHelper output)
        : base(output, startServer: false)
    {
        McpServerBuilder
            .WithSkills(
            [
                new McpServerSkill(new Skill
                {
                    Uri = AlphaUri,
                    Frontmatter = new JsonObject { ["name"] = "alpha", ["description"] = "Alpha" },
                    Resources = SkillResources.Dynamic,
                }),
                new McpServerSkill(new Skill
                {
                    Uri = BetaUri,
                    Frontmatter = new JsonObject { ["name"] = "beta", ["description"] = "Beta" },
                    Resources = SkillResources.Dynamic,
                }),
            ], options => options.PageSize = 1)
            .WithTasks(_store.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkillsList_UsesReplacementParameters(bool replaceWithNull)
    {
        ReplaceParameters(SkillsProtocol.MethodSkillsList, replaceWithNull ? null : new JsonObject
        {
            ["cursor"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(AlphaUri)),
        });
        await using var client = await CreateMcpClientForServer();

        var response = await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = SkillsProtocol.MethodSkillsList,
            Params = new JsonObject { ["cursor"] = "invalid-base64!" },
        }, TestContext.Current.CancellationToken);

        var skill = Assert.Single(response.Result!["skills"]!.AsArray());
        Assert.Equal(replaceWithNull ? AlphaUri : BetaUri, skill!["uri"]!.GetValue<string>());
    }

    [Fact]
    public async Task SkillsGet_UsesReplacementParameters()
    {
        ReplaceParameters(SkillsProtocol.MethodSkillsGet, new JsonObject { ["uri"] = BetaUri });
        await using var client = await CreateMcpClientForServer();

        var response = await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = SkillsProtocol.MethodSkillsGet,
            Params = new JsonObject { ["uri"] = AlphaUri },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(BetaUri, response.Result!["skill"]!["uri"]!.GetValue<string>());
    }

    [Fact]
    public async Task SkillsGet_ValidatesReplacementParameters()
    {
        ReplaceParameters(SkillsProtocol.MethodSkillsGet, parameters: null);
        await using var client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(() => client.SendRequestAsync(new JsonRpcRequest
        {
            Method = SkillsProtocol.MethodSkillsGet,
            Params = new JsonObject { ["uri"] = AlphaUri },
        }, TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
    }

    [Theory]
    [InlineData(TasksProtocol.MethodTasksGet)]
    [InlineData(TasksProtocol.MethodTasksUpdate)]
    [InlineData(TasksProtocol.MethodTasksCancel)]
    public async Task TaskMethods_UseReplacementParametersAndOriginalCapabilities(string method)
    {
        const string ReplacementTaskId = "replacement";
        switch (method)
        {
            case TasksProtocol.MethodTasksGet:
                var now = DateTimeOffset.UtcNow;
                _store.Setup(store => store.GetTaskAsync(ReplacementTaskId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new McpTaskInfo(ReplacementTaskId, McpTaskStatus.Working, now, now));
                break;
            case TasksProtocol.MethodTasksUpdate:
                _store.Setup(store => store.ResolveInputRequestsAsync(
                    ReplacementTaskId,
                    It.Is<IDictionary<string, InputResponse>>(responses =>
                        responses.Count == 1 && responses["answer"].RawValue.GetProperty("action").GetString() == "accept"),
                    It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                break;
            case TasksProtocol.MethodTasksCancel:
                _store.Setup(store => store.SetCancelledAsync(ReplacementTaskId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                break;
        }

        ReplaceParameters(method, new JsonObject
        {
            ["taskId"] = ReplacementTaskId,
            ["inputResponses"] = new JsonObject { ["answer"] = new JsonObject { ["action"] = "accept" } },
        });
        await using var client = await CreateMcpClientForServer();

        await client.SendRequestAsync(new JsonRpcRequest
        {
            Method = method,
            Params = new JsonObject
            {
                ["taskId"] = "original",
                ["inputResponses"] = new JsonObject { ["original-answer"] = new JsonObject { ["action"] = "cancel" } },
                ["_meta"] = new JsonObject
                {
                    [MetaKeys.ClientCapabilities] = new JsonObject
                    {
                        ["extensions"] = new JsonObject { [TasksProtocol.ExtensionId] = new JsonObject() },
                    },
                },
            },
        }, TestContext.Current.CancellationToken);

        _store.VerifyAll();
    }

#pragma warning disable MCPEXP002 // Compose the experimental custom handlers before the server is created.
    private void ReplaceParameters(string method, JsonNode? parameters)
    {
        ServiceCollection.PostConfigure<McpServerOptions>(options =>
        {
            var handlers = Assert.IsAssignableFrom<IList<McpServerRequestHandler>>(options.RequestHandlers);
            int index = Enumerable.Range(0, handlers.Count).Single(index => handlers[index].Method == method);
            var registration = handlers[index];
            handlers[index] = new McpServerRequestHandler
            {
                Method = registration.Method,
                RoutingNameParameter = registration.RoutingNameParameter,
                Handler = (context, cancellationToken) =>
                {
                    var originalParameters = context.JsonRpcRequest.Params;
                    Assert.Same(originalParameters, context.Params);
                    context.Params = parameters?.DeepClone();
                    Assert.Same(originalParameters, context.JsonRpcRequest.Params);
                    Assert.NotSame(context.JsonRpcRequest.Params, context.Params);
                    return registration.Handler(context, cancellationToken);
                },
            };
        });
        StartServer();
    }
#pragma warning restore MCPEXP002
}
