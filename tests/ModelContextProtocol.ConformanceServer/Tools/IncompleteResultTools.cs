#pragma warning disable MCPEXP001 // MRTR (SEP-2322) is experimental.

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
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
    [McpServerTool(Name = "test_input_required_result_elicitation")]
    [Description("SEP-2322 A1: returns InputRequiredResult with elicitation/create keyed 'user_name'.")]
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
    [McpServerTool(Name = "test_input_required_result_sampling")]
    [Description("SEP-2322 A2: returns InputRequiredResult with sampling/createMessage keyed 'capital_question'.")]
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
    [McpServerTool(Name = "test_input_required_result_list_roots")]
    [Description("SEP-2322 A3: returns InputRequiredResult with roots/list keyed 'client_roots'.")]
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

    [McpServerTool(Name = "test_input_required_result_request_state")]
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
    [McpServerTool(Name = "test_input_required_result_multiple_inputs")]
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
    [McpServerTool(Name = "test_input_required_result_multi_round")]
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

    // ──── C1: Missing/wrong inputResponses key - re-request rather than error ────
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

        // Either no inputResponses or wrong key - re-request via a fresh InputRequiredResult
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

    // ──── A12: Tampered requestState rejection (integrity protection) ───────
    // SEP-2322 recommends integrity-protecting requestState (e.g. an HMAC signature)
    // so a client cannot forge or mutate it. R1 returns a signed requestState; R2 with
    // a tampered requestState fails verification and surfaces a JSON-RPC error (not an
    // isError CallToolResult and not a re-prompt).
    private static readonly byte[] s_requestStateKey = Encoding.UTF8.GetBytes("mrtr-conformance-hmac-key-v1");

    [McpServerTool(Name = "test_input_required_result_tampered_state")]
    [Description("SEP-2322 A12: R1 returns an HMAC-signed requestState; R2 rejects a tampered requestState with a JSON-RPC error.")]
    public static CallToolResult ToolWithTamperedState(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.RequestState is { } state)
        {
            if (!VerifyRequestState(state))
            {
                throw new McpProtocolException(
                    "requestState failed integrity verification (tampered or invalid signature).",
                    McpErrorCode.InvalidParams);
            }

            return TextResult("tampered-state-ok: requestState integrity verified");
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
            requestState: SignRequestState());
    }

    // ──── A13: Respect client capabilities ──────────────────────────────────
    // Per SEP-2575 the client declares its capabilities in the per-request
    // _meta['io.modelcontextprotocol/clientCapabilities'] envelope (surfaced on
    // JsonRpcMessageContext.ClientCapabilities). The server MUST only emit inputRequests
    // for capabilities the client advertised on this request.
    [McpServerTool(Name = "test_input_required_result_capabilities")]
    [Description("SEP-2322 A13: returns inputRequests only for the capabilities the client declared in per-request _meta.")]
    public static CallToolResult ToolWithCapabilityCheck(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params!.InputResponses is { Count: > 0 })
        {
            return TextResult("capability-check-ok: received input responses");
        }

        var capabilities = context.JsonRpcRequest.Context?.ClientCapabilities;
        var inputRequests = new Dictionary<string, InputRequest>();

        if (capabilities?.Sampling is not null)
        {
            inputRequests["capital_question"] = InputRequest.ForSampling(new CreateMessageRequestParams
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
            });
        }

        if (capabilities?.Elicitation is not null)
        {
            inputRequests["user_name"] = InputRequest.ForElicitation(new ElicitRequestParams
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
            });
        }

        if (capabilities?.Roots is not null)
        {
            inputRequests["client_roots"] = InputRequest.ForRootsList(new ListRootsRequestParams());
        }

        if (inputRequests.Count == 0)
        {
            return TextResult("capability-check-ok: client declared no MRTR-capable features");
        }

        throw new InputRequiredException(inputRequests);
    }

    private static string SignRequestState()
    {
        var nonce = Guid.NewGuid().ToString("N");
        return $"{nonce}.{ComputeSignature(nonce)}";
    }

    private static bool VerifyRequestState(string state)
    {
        var separator = state.LastIndexOf('.');
        if (separator <= 0 || separator == state.Length - 1)
        {
            return false;
        }

        var nonce = state[..separator];
        var signature = state[(separator + 1)..];
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(ComputeSignature(nonce)));
    }

    private static string ComputeSignature(string nonce)
    {
        using var hmac = new HMACSHA256(s_requestStateKey);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(nonce)));
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
