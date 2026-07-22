#pragma warning disable MCPEXP001 // MRTR (SEP-2322) is experimental.

using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ReleasePlannerServer;

[McpServerToolType]
public sealed class ReleasePlannerTools(IOptions<McpServerOptions> serverOptions)
{
    private const string DetailsResponseKey = "release_details";
    private const string ApprovalResponseKey = "release_approval";

    [McpServerTool(
        Name = "plan_release",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Build a production release plan through two interactive rounds without retaining server-side conversation state.")]
    public static string PlanRelease(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        [Description("Application or service to release")] string application,
        [Description("Version or build identifier to release")] string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        string? encodedState = context.Params!.RequestState;
        if (encodedState is null)
        {
            if (!server.IsMrtrSupported)
            {
                return "This client cannot complete the interactive release plan. Use MCP 2026-07-28 or a stateful client with elicitation support.";
            }

            if (server.ClientCapabilities?.Elicitation?.Form is null)
            {
                return "This client cannot complete the interactive release plan because it does not support form elicitation.";
            }

            throw RequestReleaseDetails(application, version);
        }

        ReleasePlanState state = DecodeState(encodedState);
        if (!string.Equals(state.Application, application, StringComparison.Ordinal) ||
            !string.Equals(state.Version, version, StringComparison.Ordinal))
        {
            throw new McpException("The retried tool arguments do not match the release stored in requestState.");
        }

        return state.Stage switch
        {
            ReleasePlanStage.Details => ProcessDetails(context.Params.InputResponses, state),
            ReleasePlanStage.Approval => ProcessApproval(context.Params.InputResponses, state, encodedState.Length),
            _ => throw new McpException($"Unexpected release-plan stage '{state.Stage}'."),
        };
    }

    [McpServerTool(
        Name = "unlock_production_deploy",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Add a simulated production deployment tool while the server is running and notify the client that the tool list changed.")]
    public string UnlockProductionDeploy()
    {
        McpServerPrimitiveCollection<McpServerTool> tools =
            serverOptions.Value.ToolCollection ?? throw new McpException("The server tool collection is unavailable.");

        McpServerTool deploy = McpServerTool.Create(
            (
                [Description("Application or service to deploy")] string application,
                [Description("Version or build identifier to deploy")] string version) =>
                $"""
                SIMULATED PRODUCTION DEPLOYMENT
                Application: {application}
                Version: {version}
                Result: Approval accepted and deployment queued.

                No real environment was changed by this demo tool.
                """,
            new McpServerToolCreateOptions
            {
                Name = "deploy_release",
                Title = "Deploy Release",
                Description = "Simulate queueing an approved application version for production deployment.",
                ReadOnly = false,
                Destructive = true,
                Idempotent = false,
                OpenWorld = true,
            });

        return tools.TryAdd(deploy)
            ? "Production deployment unlocked. The client was sent notifications/tools/list_changed; refresh the tool picker to see deploy_release."
            : "Production deployment was already unlocked.";
    }

    private static InputRequiredException RequestReleaseDetails(string application, string version)
    {
        return new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                [DetailsResponseKey] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = $"Release controls for {application} {version}",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["environment"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                            {
                                Title = "Target environment",
                                Enum = ["staging", "production"],
                                Default = "production",
                            },
                            ["strategy"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                            {
                                Title = "Rollout strategy",
                                Enum = ["canary", "blue-green", "rolling"],
                                Default = "canary",
                            },
                            ["initialRolloutPercent"] = new ElicitRequestParams.NumberSchema
                            {
                                Type = "integer",
                                Title = "Initial rollout percentage",
                                Minimum = 1,
                                Maximum = 100,
                                Default = 10,
                            },
                            ["maxErrorRatePercent"] = new ElicitRequestParams.NumberSchema
                            {
                                Title = "Rollback error-rate threshold",
                                Description = "Abort the rollout when the observed error rate exceeds this percentage",
                                Minimum = 0,
                                Maximum = 100,
                                Default = 1.0,
                            },
                        },
                        Required = ["environment", "strategy", "initialRolloutPercent", "maxErrorRatePercent"],
                    },
                }),
            },
            requestState: EncodeState(new ReleasePlanState(ReleasePlanStage.Details, application, version)));
    }

    private static string ProcessDetails(
        IDictionary<string, InputResponse>? responses,
        ReleasePlanState state)
    {
        ElicitResult result = GetElicitationResult(responses, DetailsResponseKey);
        if (!result.IsAccepted)
        {
            return $"Release planning {result.Action}.";
        }

        IDictionary<string, JsonElement> content =
            result.Content ?? throw new McpException("The accepted release-details form was empty.");

        var nextState = state with
        {
            Stage = ReleasePlanStage.Approval,
            TargetEnvironment = GetRequiredChoice(content, "environment", "staging", "production"),
            Strategy = GetRequiredChoice(content, "strategy", "canary", "blue-green", "rolling"),
            InitialRolloutPercent = GetRequiredInt32(content, "initialRolloutPercent", minimum: 1, maximum: 100),
            MaxErrorRatePercent = GetRequiredDouble(content, "maxErrorRatePercent", minimum: 0, maximum: 100),
        };

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                [ApprovalResponseKey] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message =
                        $"Approve a {nextState.Strategy} release of {nextState.Application} {nextState.Version} " +
                        $"to {nextState.TargetEnvironment}, starting at {nextState.InitialRolloutPercent}% traffic " +
                        $"with rollback above {nextState.MaxErrorRatePercent:0.##}% errors?",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["approved"] = new ElicitRequestParams.BooleanSchema
                            {
                                Title = "Approve release plan",
                                Default = true,
                            },
                        },
                        Required = ["approved"],
                    },
                }),
            },
            requestState: EncodeState(nextState));
    }

    private static string ProcessApproval(
        IDictionary<string, InputResponse>? responses,
        ReleasePlanState state,
        int requestStateLength)
    {
        ValidateApprovalState(state);

        ElicitResult result = GetElicitationResult(responses, ApprovalResponseKey);
        if (!result.IsAccepted)
        {
            return $"Release-plan approval {result.Action}.";
        }

        IDictionary<string, JsonElement> content =
            result.Content ?? throw new McpException("The accepted approval form was empty.");
        if (!GetRequiredBoolean(content, "approved"))
        {
            return "Release plan was not approved.";
        }

        return $"""
            RELEASE PLAN: {state.Application} {state.Version}
            Target: {state.TargetEnvironment}
            Strategy: {state.Strategy}
            Initial rollout: {state.InitialRolloutPercent}%
            Automatic rollback threshold: {state.MaxErrorRatePercent:0.##}% errors

            00:00  Verify signed artifact, configuration, and database compatibility
            00:05  Route {state.InitialRolloutPercent}% of traffic to {state.Version}
            00:15  Evaluate latency, errors, and saturation against health gates
            00:20  Continue the {state.Strategy} rollout when all gates pass

            Rollback: restore the previous version when errors exceed {state.MaxErrorRatePercent:0.##}%.

            Stateless proof: the handler reconstructed this plan from {requestStateLength} characters of opaque requestState.
            """;
    }

    private static ElicitResult GetElicitationResult(
        IDictionary<string, InputResponse>? responses,
        string key)
    {
        if (responses is null || !responses.TryGetValue(key, out InputResponse? response))
        {
            throw new McpException($"The client retry did not include the '{key}' input response.");
        }

        return response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)
            ?? throw new McpException($"The '{key}' input response was invalid.");
    }

    private static string GetRequiredChoice(
        IDictionary<string, JsonElement> content,
        string name,
        params string[] allowedValues)
    {
        if (!content.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new McpException($"The elicitation response field '{name}' must be a string.");
        }

        string result = value.GetString()!;
        if (!allowedValues.Contains(result, StringComparer.Ordinal))
        {
            throw new McpException($"The elicitation response field '{name}' has an unsupported value.");
        }

        return result;
    }

    private static int GetRequiredInt32(
        IDictionary<string, JsonElement> content,
        string name,
        int minimum,
        int maximum)
    {
        if (!content.TryGetValue(name, out JsonElement value) || !value.TryGetInt32(out int result))
        {
            throw new McpException($"The elicitation response field '{name}' must be an integer.");
        }

        if (result < minimum || result > maximum)
        {
            throw new McpException($"The elicitation response field '{name}' must be between {minimum} and {maximum}.");
        }

        return result;
    }

    private static double GetRequiredDouble(
        IDictionary<string, JsonElement> content,
        string name,
        double minimum,
        double maximum)
    {
        if (!content.TryGetValue(name, out JsonElement value) || !value.TryGetDouble(out double result))
        {
            throw new McpException($"The elicitation response field '{name}' must be a number.");
        }

        if (result < minimum || result > maximum)
        {
            throw new McpException($"The elicitation response field '{name}' must be between {minimum} and {maximum}.");
        }

        return result;
    }

    private static bool GetRequiredBoolean(IDictionary<string, JsonElement> content, string name)
    {
        if (!content.TryGetValue(name, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new McpException($"The elicitation response field '{name}' must be a Boolean.");
        }

        return value.GetBoolean();
    }

    private static void ValidateApprovalState(ReleasePlanState state)
    {
        if (state.TargetEnvironment is not ("staging" or "production") ||
            state.Strategy is not ("canary" or "blue-green" or "rolling") ||
            state.InitialRolloutPercent is not int rolloutPercent ||
            rolloutPercent is < 1 or > 100 ||
            state.MaxErrorRatePercent is not double errorRatePercent ||
            !double.IsFinite(errorRatePercent) ||
            errorRatePercent is < 0 or > 100)
        {
            throw new McpException("The client returned invalid release-plan requestState.");
        }
    }

    private static string EncodeState(ReleasePlanState state) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(state, JsonSerializerOptions.Web));

    private static ReleasePlanState DecodeState(string encodedState)
    {
        try
        {
            return JsonSerializer.Deserialize<ReleasePlanState>(
                Convert.FromBase64String(encodedState),
                JsonSerializerOptions.Web) ?? throw new JsonException("The decoded release-plan state was empty.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new McpException("The client returned invalid release-plan requestState.", ex);
        }
    }

    private enum ReleasePlanStage
    {
        Details,
        Approval,
    }

    private sealed record ReleasePlanState(
        ReleasePlanStage Stage,
        string Application,
        string Version,
        string? TargetEnvironment = null,
        string? Strategy = null,
        int? InitialRolloutPercent = null,
        double? MaxErrorRatePercent = null);
}
