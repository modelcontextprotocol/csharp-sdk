#pragma warning disable MCPEXP001 // MRTR (SEP-2322) is experimental.

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ConformanceServer.Tools;

[McpServerToolType]
public sealed class ConformanceTaskTools
{
    [McpServerTool(Name = "greet")]
    [Description("Returns a synchronous greeting.")]
    public static string Greet(string name) => $"Hello, {name}!";

    [McpServerTool(Name = "slow_compute")]
    [Description("Completes after the requested number of seconds.")]
    public static async Task<string> SlowCompute(int seconds, string? label, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        return $"Computed {label ?? "result"}";
    }

    [McpServerTool(Name = "failing_job")]
    [Description("Produces a tool execution error.")]
    public static async Task<string> FailingJob(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        throw new Exception("The conformance task failed.");
    }

    [McpServerTool(Name = "protocol_error_job")]
    [Description("Produces a protocol-level error.")]
    public static string ProtocolErrorJob() =>
        throw new McpProtocolException("The conformance task encountered a protocol error.", McpErrorCode.InternalError);

    [McpServerTool(Name = "confirm_delete")]
    [Description("Waits for elicitation before confirming deletion.")]
    public static async Task<string> ConfirmDelete(
        McpServer server,
        string filename,
        CancellationToken cancellationToken)
    {
        var result = await server.ElicitAsync(CreateConfirmationRequest($"Delete {filename}?"), cancellationToken);
        return result.Action == "accept" ? $"Deleted {filename}" : $"Did not delete {filename}";
    }

    [McpServerTool(Name = "multi_input")]
    [Description("Waits for two independent elicitation responses.")]
    public static async Task<string> MultiInput(McpServer server, CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            server.ElicitAsync(CreateConfirmationRequest("Confirm the first operation."), cancellationToken).AsTask(),
            server.ElicitAsync(CreateConfirmationRequest("Confirm the second operation."), cancellationToken).AsTask());
        return "Both inputs received.";
    }

    [McpServerTool(Name = "test_tool_with_task")]
    [Description("Collects input synchronously, then completes through a task.")]
    public static string ToolWithTask(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("user_name", out var response))
        {
            var elicitation = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            var name = elicitation?.Content?["name"].GetString() ?? "world";
            return $"Hello, {name}!";
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                ["user_name"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What is your name?",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties =
                        {
                            ["name"] = new ElicitRequestParams.StringSchema(),
                        },
                        Required = ["name"],
                    },
                }),
            });
    }

    private static ElicitRequestParams CreateConfirmationRequest(string message) =>
        new()
        {
            Message = message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["confirm"] = new ElicitRequestParams.BooleanSchema(),
                },
                Required = ["confirm"],
            },
        };
}
