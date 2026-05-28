#pragma warning disable MCPEXP001 // MRTR (SEP-2322) is experimental.

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConformanceServer.Tools;

/// <summary>
/// Tools implementing the SEP-2322 (MRTR / IncompleteResult) conformance scenarios from
/// <c>incomplete-result.ts</c> in the conformance test suite. All tools use the
/// <see cref="InputRequiredException"/> API so they work both in stateful sessions with
/// MRTR-aware clients and in legacy-resolve mode (the SDK will translate exceptions to the
/// proper wire shape based on negotiated protocol version).
/// </summary>
[McpServerToolType]
public sealed class IncompleteResultTools
{
    // ──── A1: Basic Elicitation ─────────────────────────────────────────────
    [McpServerTool(Name = "test_tool_with_elicitation")]
    [Description("SEP-2322 A1: returns IncompleteResult with elicitation/create keyed 'user_name'.")]
    public static CallToolResult ToolWithElicitation(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("user_name", out var response))
        {
            var elicit = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            var name = TryReadString(elicit?.Content, "name") ?? "world";
            return TextResult($"Hello, {name}!");
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                ["user_name"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What is your name?",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["name"] = new ElicitRequestParams.StringSchema(),
                        },
                        Required = ["name"],
                    },
                }),
            });
    }

    // ──── A2: Basic Sampling ────────────────────────────────────────────────
    [McpServerTool(Name = "test_incomplete_result_sampling")]
    [Description("SEP-2322 A2: returns IncompleteResult with sampling/createMessage keyed 'capital_question'.")]
    public static CallToolResult ToolWithSampling(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("capital_question", out var response))
        {
            var text = response.Deserialize(InputResponse.CreateMessageResultJsonTypeInfo)?.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "(no text)";
            return TextResult($"Sampling said: {text}");
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                ["capital_question"] = InputRequest.ForSampling(new CreateMessageRequestParams
                {
                    Messages =
                    [
                        new SamplingMessage
                        {
                            Role = Role.User,
                            Content = [new TextContentBlock { Text = "What is the capital of France?" }],
                        },
                    ],
                    MaxTokens = 100,
                }),
            });
    }

    // ──── A3: Basic ListRoots ───────────────────────────────────────────────
    [McpServerTool(Name = "test_incomplete_result_list_roots")]
    [Description("SEP-2322 A3: returns IncompleteResult with roots/list keyed 'client_roots'.")]
    public static CallToolResult ToolWithListRoots(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("client_roots", out var response))
        {
            var count = response.Deserialize(InputResponse.ListRootsResultJsonTypeInfo)?.Roots?.Count ?? 0;
            return TextResult($"Got {count} root(s) from the client.");
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                ["client_roots"] = InputRequest.ForRootsList(new ListRootsRequestParams()),
            });
    }

    // ──── B1: requestState round-trip ───────────────────────────────────────
    private const string RequestStateToken = "mrtr-conformance-state-v1";

    [McpServerTool(Name = "test_incomplete_result_request_state")]
    [Description("SEP-2322 B1: round-trips a requestState string; R2 echoes 'state-ok' on success.")]
    public static CallToolResult ToolWithRequestState(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.RequestState is { } state)
        {
            if (state != RequestStateToken)
            {
                return TextResult("state-mismatch: client echoed an unexpected requestState");
            }
            return TextResult("state-ok: server received and validated the echoed requestState");
        }

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "Please confirm",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["ok"] = new ElicitRequestParams.BooleanSchema(),
                        },
                        Required = ["ok"],
                    },
                }),
            },
            requestState: RequestStateToken);
    }

    // ──── B2: Multiple input requests in one round ──────────────────────────
    [McpServerTool(Name = "test_incomplete_result_multiple_inputs")]
    [Description("SEP-2322 B2: returns 3 simultaneous inputRequests (elicit + sampling + roots) plus requestState.")]
    public static CallToolResult ToolWithMultipleInputs(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses && responses.Count >= 3)
        {
            return TextResult("multiple-inputs-ok: received elicit + sampling + roots responses");
        }

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["user_name"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What is your name?",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["name"] = new ElicitRequestParams.StringSchema(),
                        },
                        Required = ["name"],
                    },
                }),
                ["greeting"] = InputRequest.ForSampling(new CreateMessageRequestParams
                {
                    Messages =
                    [
                        new SamplingMessage
                        {
                            Role = Role.User,
                            Content = [new TextContentBlock { Text = "Generate a greeting" }],
                        },
                    ],
                    MaxTokens = 50,
                }),
                ["client_roots"] = InputRequest.ForRootsList(new ListRootsRequestParams()),
            },
            requestState: "multi-input-state");
    }

    // ──── B3: Multi-round (R1 -> incomplete, R2 -> incomplete (new state), R3 -> complete) ─────
    [McpServerTool(Name = "test_incomplete_result_multi_round")]
    [Description("SEP-2322 B3: three-round flow whose requestState changes between rounds.")]
    public static CallToolResult ToolWithMultiRound(RequestContext<CallToolRequestParams> context)
    {
        var state = context.Params!.RequestState;
        if (state is null)
        {
            // Round 1: elicit name.
            throw new InputRequiredException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    ["step1"] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Message = "Step 1: What is your name?",
                        RequestedSchema = new ElicitRequestParams.RequestSchema
                        {
                            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                            {
                                ["name"] = new ElicitRequestParams.StringSchema(),
                            },
                            Required = ["name"],
                        },
                    }),
                },
                requestState: "round-1");
        }

        if (state == "round-1")
        {
            // Round 2: elicit color (new state).
            throw new InputRequiredException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    ["step2"] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Message = "Step 2: What is your favorite color?",
                        RequestedSchema = new ElicitRequestParams.RequestSchema
                        {
                            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                            {
                                ["color"] = new ElicitRequestParams.StringSchema(),
                            },
                            Required = ["color"],
                        },
                    }),
                },
                requestState: "round-2");
        }

        // Round 3: complete.
        return TextResult("multi-round-ok");
    }

    // ──── C1: Missing/wrong inputResponses key — re-request rather than error ────
    [McpServerTool(Name = "test_incomplete_result_elicitation")]
    [Description("SEP-2322 C1: re-requests missing inputResponses key instead of erroring.")]
    public static CallToolResult ToolForMissingResponse(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("user_name", out var response))
        {
            var elicit = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            var name = TryReadString(elicit?.Content, "name") ?? "world";
            return TextResult($"Hello, {name}!");
        }

        // Either no inputResponses or wrong key — re-request via a fresh InputRequiredResult
        // (per SEP-2322 recommendation in scenario C1).
        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                ["user_name"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What is your name?",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["name"] = new ElicitRequestParams.StringSchema(),
                        },
                        Required = ["name"],
                    },
                }),
            });
    }

    private static CallToolResult TextResult(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }],
    };

    private static string? TryReadString(IDictionary<string, JsonElement>? content, string key)
    {
        if (content is null || !content.TryGetValue(key, out var element))
        {
            return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }
}
