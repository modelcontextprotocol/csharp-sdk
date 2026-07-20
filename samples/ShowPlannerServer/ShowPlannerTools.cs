#pragma warning disable MCPEXP001 // MRTR (SEP-2322) is experimental.
#pragma warning disable MCP9005 // Sampling is intentionally demonstrated as a deprecated Visual Studio feature.

using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace ShowPlannerServer;

[McpServerToolType]
public sealed class ShowPlannerTools(IOptions<McpServerOptions> serverOptions)
{
    private const string DetailsResponseKey = "show_details";
    private const string ApprovalResponseKey = "show_approval";
    private const string CreativeDirectionResponseKey = "creative_direction";

    [McpServerTool(Name = "plan_show")]
    [Description("Build a show run sheet through two interactive rounds without retaining server-side conversation state.")]
    public static string PlanShow(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        [Description("Event or milestone the show should celebrate")] string occasion)
    {
        string? encodedState = context.Params!.RequestState;
        if (encodedState is null)
        {
            if (!server.IsMrtrSupported)
            {
                return "This client cannot complete the interactive plan. Use MCP 2026-07-28 or a stateful client with elicitation support.";
            }

            if (server.ClientCapabilities?.Elicitation?.Form is null)
            {
                return "This client cannot complete the interactive plan because it does not support form elicitation.";
            }

            throw RequestShowDetails(occasion);
        }

        PlannerState state = DecodeState(encodedState);
        return state.Stage switch
        {
            PlannerStage.Details => ProcessDetails(context.Params.InputResponses, state),
            PlannerStage.Approval => ProcessApproval(context.Params.InputResponses, state, encodedState.Length),
            _ => throw new McpException($"Unexpected planner stage '{state.Stage}'."),
        };
    }

    [McpServerTool(Name = "surprise_me")]
    [Description("Ask the client's LLM for one bold creative direction using MRTR sampling.")]
    public static string SurpriseMe(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        [Description("Event or audience that needs a creative show concept")] string occasion)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue(CreativeDirectionResponseKey, out InputResponse? response))
        {
            CreateMessageResult? result = response.Deserialize(InputResponse.CreateMessageResultJsonTypeInfo);
            string idea = result?.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? throw new McpException("The sampling response did not contain text.");
            string originalOccasion = DecodeState(context.Params.RequestState!).Occasion;
            return $"Creative direction for {originalOccasion}:{Environment.NewLine}{idea}";
        }

        if (!server.IsMrtrSupported)
        {
            return "This client cannot complete the sampling request.";
        }

        if (server.ClientCapabilities?.Sampling is null)
        {
            return "This client cannot complete the creative-direction request because it does not support sampling.";
        }

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                [CreativeDirectionResponseKey] = InputRequest.ForSampling(new CreateMessageRequestParams
                {
                    SystemPrompt =
                        "You are a live-event creative director. Return one vivid, stageable concept in two sentences. " +
                        "Include a memorable visual beat and no preamble.",
                    Messages =
                    [
                        new SamplingMessage
                        {
                            Role = Role.User,
                            Content = [new TextContentBlock { Text = $"Invent a surprising show concept for: {occasion}" }],
                        },
                    ],
                    MaxTokens = 100,
                    Temperature = 0.9f,
                }),
            },
            requestState: EncodeState(new PlannerState(PlannerStage.Sampling, occasion)));
    }

    [McpServerTool(Name = "unlock_grand_finale")]
    [Description("Add a brand-new finale tool while the server is running and notify the client that the tool list changed.")]
    public string UnlockGrandFinale()
    {
        McpServerPrimitiveCollection<McpServerTool> tools =
            serverOptions.Value.ToolCollection ?? throw new McpException("The server tool collection is unavailable.");

        McpServerTool finale = McpServerTool.Create(
            ([Description("Person, team, or idea celebrated in the finale")] string honoree) =>
                $"""
                GRAND FINALE CUE
                House lights: zero
                Countdown: 3 - 2 - 1
                Sky text: {honoree.ToUpperInvariant()}
                Follow with the largest Celebration fireworks show.
                """,
            new McpServerToolCreateOptions
            {
                Name = "launch_grand_finale",
                Title = "Launch Grand Finale",
                Description = "Reveal the final stage cue for a named honoree.",
                ReadOnly = true,
                Idempotent = true,
                OpenWorld = false,
            });

        return tools.TryAdd(finale)
            ? "Grand finale unlocked. The client was sent notifications/tools/list_changed; refresh the tool picker to see launch_grand_finale."
            : "Grand finale was already unlocked.";
    }

    private static InputRequiredException RequestShowDetails(string occasion)
    {
        return new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                [DetailsResponseKey] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = $"Production details for \"{occasion}\"",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["venue"] = new ElicitRequestParams.StringSchema
                            {
                                Title = "Venue",
                                Description = "Where the show will run",
                                MinLength = 2,
                                MaxLength = 80,
                            },
                            ["audienceSize"] = new ElicitRequestParams.NumberSchema
                            {
                                Type = "integer",
                                Title = "Audience size",
                                Minimum = 1,
                                Maximum = 100_000,
                                Default = 500,
                            },
                            ["style"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                            {
                                Title = "Show style",
                                Enum = ["cinematic", "playful", "elegant", "maximum-energy"],
                                Default = "cinematic",
                            },
                            ["indoor"] = new ElicitRequestParams.BooleanSchema
                            {
                                Title = "Indoor venue",
                                Description = "Use effects that are safe for an enclosed venue",
                                Default = false,
                            },
                        },
                        Required = ["venue", "audienceSize", "style", "indoor"],
                    },
                }),
            },
            requestState: EncodeState(new PlannerState(PlannerStage.Details, occasion)));
    }

    private static string ProcessDetails(
        IDictionary<string, InputResponse>? responses,
        PlannerState state)
    {
        ElicitResult result = GetElicitationResult(responses, DetailsResponseKey);
        if (!result.IsAccepted)
        {
            return $"Show planning {result.Action}.";
        }

        IDictionary<string, JsonElement> content =
            result.Content ?? throw new McpException("The accepted show-details form was empty.");

        var nextState = state with
        {
            Stage = PlannerStage.Approval,
            Venue = GetRequiredString(content, "venue"),
            AudienceSize = GetRequiredInt32(content, "audienceSize"),
            Style = GetRequiredString(content, "style"),
            Indoor = GetRequiredBoolean(content, "indoor"),
        };

        string environment = nextState.Indoor is true ? "indoor" : "outdoor";
        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                [ApprovalResponseKey] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message =
                        $"Approve a {nextState.Style} {environment} show for {nextState.AudienceSize:N0} people " +
                        $"at {nextState.Venue}?",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["approved"] = new ElicitRequestParams.BooleanSchema
                            {
                                Title = "Approve run sheet",
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
        PlannerState state,
        int requestStateLength)
    {
        ElicitResult result = GetElicitationResult(responses, ApprovalResponseKey);
        if (!result.IsAccepted)
        {
            return $"Run-sheet approval {result.Action}.";
        }

        IDictionary<string, JsonElement> content =
            result.Content ?? throw new McpException("The accepted approval form was empty.");
        if (!GetRequiredBoolean(content, "approved"))
        {
            return "Run sheet was not approved.";
        }

        string effects = state.Indoor is true
            ? "Cold sparks, lasers, projection, and synchronized lighting"
            : "Fireworks, drones, lasers, and synchronized lighting";

        return $"""
            SHOW RUN SHEET: {state.Occasion.ToUpperInvariant()}
            Venue: {state.Venue}
            Audience: {state.AudienceSize:N0}
            Style: {state.Style}
            Effects: {effects}

            00:00  House to black
            00:08  Signature visual reveal
            00:25  Audience participation beat
            00:45  Finale and hero message

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

    private static string GetRequiredString(IDictionary<string, JsonElement> content, string name)
    {
        if (!content.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new McpException($"The elicitation response field '{name}' must be a string.");
        }

        return value.GetString()!;
    }

    private static int GetRequiredInt32(IDictionary<string, JsonElement> content, string name)
    {
        if (!content.TryGetValue(name, out JsonElement value) || !value.TryGetInt32(out int result))
        {
            throw new McpException($"The elicitation response field '{name}' must be an integer.");
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

    private static string EncodeState(PlannerState state) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(state, JsonSerializerOptions.Web));

    private static PlannerState DecodeState(string encodedState)
    {
        try
        {
            return JsonSerializer.Deserialize<PlannerState>(
                Convert.FromBase64String(encodedState),
                JsonSerializerOptions.Web) ?? throw new JsonException("The decoded planner state was empty.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new McpException("The client returned invalid planner requestState.", ex);
        }
    }

    private enum PlannerStage
    {
        Details,
        Approval,
        Sampling,
    }

    private sealed record PlannerState(
        PlannerStage Stage,
        string Occasion,
        string? Venue = null,
        int? AudienceSize = null,
        string? Style = null,
        bool? Indoor = null);
}
