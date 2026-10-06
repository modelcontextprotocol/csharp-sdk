using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Server;

#pragma warning disable MCPEXP002 // exercises the experimental alternate-result seam

/// <summary>
/// An alternate result a filter can return in place of a prompt result, standing in for the kind of
/// result a future task-enabled method would produce.
/// </summary>
internal sealed class AlternatePromptResult : Result
{
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

[JsonSerializable(typeof(AlternatePromptResult))]
internal sealed partial class AlternateResultJsonContext : JsonSerializerContext;

/// <summary>
/// Verifies that the alternate-result seam is method-keyed rather than <c>tools/call</c>-shaped, by
/// driving it through <see cref="RequestMethods.PromptsGet"/> and <see cref="RequestMethods.PromptsList"/>.
/// </summary>
public class AlternateResultFilterPromptTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
    private static readonly JsonSerializerOptions s_serializerOptions = CreateSerializerOptions();

    private readonly List<string> _promptsListFilterOrder = [];

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities = new() { Prompts = new() };

            options.Handlers.ListPromptsHandler = static (_, _) => new(new ListPromptsResult
            {
                Prompts = [new Prompt { Name = "ordinary-prompt" }],
            });

            options.Handlers.GetPromptHandler = static (request, _) => new(new GetPromptResult
            {
                Description = $"ordinary:{request.Params?.Name}",
            });

            options.AddAlternateResultFilter<GetPromptRequestParams, GetPromptResult>(
                RequestMethods.PromptsGet,
                async (request, next, cancellationToken) =>
                {
                    if (request.Params?.Name == "alternate-prompt")
                    {
                        return ResultOrAlternate<GetPromptResult>.FromAlternate(
                            new AlternatePromptResult { Note = "replaced by filter" },
                            AlternateResultJsonContext.Default.AlternatePromptResult);
                    }

                    return await next(request, cancellationToken);
                });

            options.AddAlternateResultFilter<ListPromptsRequestParams, ListPromptsResult>(
                RequestMethods.PromptsList,
                async (request, next, cancellationToken) =>
                {
                    _promptsListFilterOrder.Add("outer");
                    return await next(request, cancellationToken);
                });

            options.AddAlternateResultFilter<ListPromptsRequestParams, ListPromptsResult>(
                RequestMethods.PromptsList,
                async (request, next, cancellationToken) =>
                {
                    _promptsListFilterOrder.Add("inner");
                    return await next(request, cancellationToken);
                });
        });
    }

    [Fact]
    public async Task PromptsGet_AlternateFilter_ReturnsAlternateResultShape()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.SendRequestAsync<GetPromptRequestParams, AlternatePromptResult>(
            RequestMethods.PromptsGet,
            new GetPromptRequestParams { Name = "alternate-prompt" },
            serializerOptions: s_serializerOptions,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("replaced by filter", result.Note);
    }

    [Fact]
    public async Task PromptsGet_AlternateFilterDelegatingToNext_ReturnsOrdinaryResult()
    {
        await using var client = await CreateMcpClientForServer();

        var result = await client.GetPromptAsync(
            "ordinary-prompt",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ordinary:ordinary-prompt", result.Description);
    }

    [Fact]
    public async Task PromptsList_AlternateFilters_RunInRegistrationOrder()
    {
        await using var client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ordinary-prompt", Assert.Single(prompts).Name);
        Assert.Equal(["outer", "inner"], _promptsListFilterOrder);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                AlternateResultJsonContext.Default,
                McpJsonUtilities.DefaultOptions.TypeInfoResolver),
        };

        options.MakeReadOnly();
        return options;
    }
}

/// <summary>
/// Covers registration semantics of the method-keyed alternate-result store, including the
/// <c>tools/call</c> view and the errors raised for unusable registrations.
/// </summary>
public class AlternateResultFilterRegistrationTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private static McpRequestInvocationFilter<CallToolRequestParams, ResultOrAlternate<CallToolResult>> PassThroughToolFilter =>
        static (context, next, cancellationToken) => next(context, cancellationToken);

    [Fact]
    public void CallToolWithAlternateFilters_IsViewOverToolsCallEntry()
    {
        var options = new McpServerOptions();

        options.AddAlternateResultFilter(RequestMethods.ToolsCall, PassThroughToolFilter);

        Assert.Same(
            options.Filters.Request.AlternateResultFilters.GetOrAdd<CallToolRequestParams, CallToolResult>(RequestMethods.ToolsCall),
            options.Filters.Request.CallToolWithAlternateFilters);
        Assert.Single(options.Filters.Request.CallToolWithAlternateFilters);
    }

    [Fact]
    public void GetOrAdd_WithMismatchedResultType_Throws()
    {
        var options = new McpServerOptions();
        options.AddAlternateResultFilter<GetPromptRequestParams, GetPromptResult>(
            RequestMethods.PromptsGet,
            static (context, next, cancellationToken) => next(context, cancellationToken));

        var ex = Assert.Throws<InvalidOperationException>(
            () => options.AddAlternateResultFilter<GetPromptRequestParams, ListPromptsResult>(
                RequestMethods.PromptsGet,
                static (context, next, cancellationToken) => next(context, cancellationToken)));

        Assert.Contains(RequestMethods.PromptsGet, ex.Message);
        Assert.Contains(nameof(GetPromptResult), ex.Message);
    }

    [Fact]
    public async Task Filters_ForMethodTheServerDoesNotDispatch_ThrowActionableError()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };

        // No prompts capability is configured, so prompts/get is never registered.
        options.AddAlternateResultFilter<GetPromptRequestParams, GetPromptResult>(
            RequestMethods.PromptsGet,
            static (context, next, cancellationToken) => next(context, cancellationToken));

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(RequestMethods.PromptsGet, ex.Message);
        Assert.Contains("does not", ex.Message);
    }

    [Fact]
    public async Task EmptyRegistration_ForUndispatchedMethod_DoesNotThrow()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions { Capabilities = new() { Tools = new() } };

        // Touching the tools/call view without adding filters must not trip the validation.
        _ = options.Filters.Request.CallToolWithAlternateFilters;
        _ = options.Filters.Request.AlternateResultFilters.GetOrAdd<GetPromptRequestParams, GetPromptResult>(RequestMethods.PromptsGet);

        await using var server = McpServer.Create(transport, options, LoggerFactory);

        Assert.NotNull(server);
    }
}
