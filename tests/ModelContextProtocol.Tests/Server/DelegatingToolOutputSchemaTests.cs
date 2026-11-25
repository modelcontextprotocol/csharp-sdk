using Json.Schema;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Server;

public partial class DelegatingToolOutputSchemaTests
{
    [Fact]
    public async Task CallTool_OrdinaryFilterNameChangeDoesNotInvokeStaleTool()
    {
        int calls = 0;
        McpServerTool tool = McpServerTool.Create(() =>
        {
            calls++;
            return new CallToolResult { Content = [] };
        }, new() { Name = "ordinary_filter_known_tool" });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            await Assert.ThrowsAsync<McpProtocolException>(async () =>
                await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(0, calls);
        }, configure: options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => (request, cancellationToken) =>
            {
                request.Params.Name = "ordinary_filter_unknown_tool";
                return next(request, cancellationToken);
            });
        });
    }

    [Fact]
    public async Task CallTool_AlternateFilterPrimitiveOverrideIsPreserved()
    {
        McpServerTool registeredTool = McpServerTool.Create(() => "registered", new() { Name = "registered_override_tool" });
        McpServerTool overrideTool = McpServerTool.Create(() => "override", new() { Name = "explicit_override_tool" });

        await WithClientAsync(registeredTool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                registeredTool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("override", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // Verify a filter-supplied primitive override is not replaced by name matching.
            options.Filters.Request.CallToolWithAlternateFilters.Add((request, next, cancellationToken) =>
            {
                request.MatchedPrimitive = overrideTool;
                return next(request, cancellationToken);
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ReplacedContextAndSuppressedFlowRetainValidationState()
    {
        McpServerTool tool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"unexpected":1}"""),
        }, new()
        {
            Name = "replacement_context_validation",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"]}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                "initially_unknown_validation_tool", cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // Exercise request replacement without flowing AsyncLocal state.
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                var replacement = new RequestContext<CallToolRequestParams>(
                    request.Server,
                    new JsonRpcRequest { Id = request.JsonRpcRequest.Id, Method = request.JsonRpcRequest.Method },
                    new CallToolRequestParams { Name = tool.ProtocolTool.Name });
                Task<ResultOrAlternate<CallToolResult>> pending;
                using (ExecutionContext.SuppressFlow())
                {
                    pending = Task.Run(async () => await next(replacement, cancellationToken), cancellationToken);
                }

                return await pending.ConfigureAwait(false);
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ReplacedContextWithNewRequestIdRetainsValidationState()
    {
        McpServerTool tool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"unexpected":1}"""),
        }, new()
        {
            Name = "replacement_context_new_id",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"]}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                "initially_unknown_validation_tool", cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // Verify schema correlation does not depend on a filter-preserved request ID.
            options.Filters.Request.CallToolWithAlternateFilters.Add((request, next, cancellationToken) =>
            {
                var replacement = new RequestContext<CallToolRequestParams>(
                    request.Server,
                    new JsonRpcRequest { Id = new RequestId("replacement"), Method = request.JsonRpcRequest.Method },
                    new CallToolRequestParams { Name = tool.ProtocolTool.Name });
                return next(replacement, cancellationToken);
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ServerFacadePreservesFilterRepairOrdering()
    {
        McpServerTool tool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"expected":1}"""),
        }, new()
        {
            Name = "facade_filter_repair_order",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"]}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(1, result.StructuredContent!.Value.GetProperty("expected").GetInt32());
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // A general outgoing-request facade must not be mistaken for task execution.
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                request.Server = request.Server.WithOutgoingRequestInterceptor(
                    (_, _, _) => new ValueTask<JsonNode?>((JsonNode?)null));
                ResultOrAlternate<CallToolResult> result = await next(request, cancellationToken);
                result.Result!.StructuredContent = ParseJson("""{"expected":1}""");
                return result;
            });
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                ResultOrAlternate<CallToolResult> result = await next(request, cancellationToken);
                result.Result!.StructuredContent = ParseJson("""{"unexpected":1}""");
                return result;
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ReplacedResultUsesSchemaForItsStructuredContent()
    {
        McpServerTool first = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"a":1}"""),
        }, new()
        {
            Name = "first_dispatched_tool",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["a"]}"""),
        });
        McpServerTool second = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"a":1}"""),
        }, new()
        {
            Name = "second_dispatched_tool",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["b"]}"""),
        });

        await WithClientAsync(first, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                first.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
            options.ToolCollection!.Add(second);
#pragma warning disable MCPEXP002 // Dispatch two tools and return a copied second result.
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                _ = await next(request, cancellationToken);
                request.Params.Name = second.ProtocolTool.Name;
                CallToolResult secondResult = (await next(request, cancellationToken)).Result!;
                request.Params.Name = first.ProtocolTool.Name;
                return new CallToolResult
                {
                    Content = secondResult.Content,
                    StructuredContent = secondResult.StructuredContent,
                    IsError = secondResult.IsError,
                    Meta = secondResult.Meta,
                };
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_CopiedResultPrefersOriginalStructuredContentIdentity()
    {
        McpServerTool first = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"a":1}"""),
        }, new()
        {
            Name = "identity_first_dispatched_tool",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["a"]}"""),
        });
        McpServerTool second = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"a":1}"""),
        }, new()
        {
            Name = "identity_second_dispatched_tool",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["b"]}"""),
        });

        await WithClientAsync(first, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                first.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(1, result.StructuredContent!.Value.GetProperty("a").GetInt32());
        }, configure: options =>
        {
            options.ToolCollection!.Add(second);
#pragma warning disable MCPEXP002 // Dispatch two tools and copy the first result after discarding the second.
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                CallToolResult firstResult = (await next(request, cancellationToken)).Result!;
                request.Params.Name = second.ProtocolTool.Name;
                _ = await next(request, cancellationToken);
                request.Params.Name = first.ProtocolTool.Name;
                return new CallToolResult
                {
                    Content = firstResult.Content,
                    StructuredContent = firstResult.StructuredContent,
                    IsError = firstResult.IsError,
                    Meta = firstResult.Meta,
                };
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ReplacedRetryResultUsesLatestSchemaSnapshot()
    {
        int calls = 0;
        McpServerTool tool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ++calls == 1 ? ParseJson("""{"a":1}""") : ParseJson("""{"b":1}"""),
        }, new()
        {
            Name = "retry_schema_snapshot",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["a"]}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(1, result.StructuredContent!.Value.GetProperty("b").GetInt32());
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // Retry with a new schema and return a copy of that invocation's result.
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                _ = await next(request, cancellationToken);
                tool.ProtocolTool.OutputSchema = ParseJson("""{"type":"object","required":["b"]}""");
                CallToolResult result = (await next(request, cancellationToken)).Result!;
                return new CallToolResult
                {
                    Content = result.Content,
                    StructuredContent = result.StructuredContent,
                    IsError = result.IsError,
                    Meta = result.Meta,
                };
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ReSerializedRetryResultUsesLatestSchemaSnapshot()
    {
        int calls = 0;
        McpServerTool tool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ++calls == 1 ? ParseJson("""{"a":1}""") : ParseJson("""{"b":1}"""),
        }, new()
        {
            Name = "reserialized_retry_schema_snapshot",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["a"]}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(1, result.StructuredContent!.Value.GetProperty("b").GetInt32());
        }, configure: options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                _ = await next(request, cancellationToken);
                tool.ProtocolTool.OutputSchema = ParseJson("""{"type":"object","required":["b"]}""");
                CallToolResult result = await next(request, cancellationToken);
                return new CallToolResult
                {
                    Content = result.Content,
                    StructuredContent = ParseJson(result.StructuredContent!.Value.GetRawText()),
                };
            });
        });
    }

    [Fact]
    public async Task CallTool_LargeResultRemainsValidWhenFiltersReturnTheSameInstance()
    {
        string payload = new('x', 70 * 1024);
        McpServerTool tool = McpServerTool.Create(() => new { Payload = payload }, new()
        {
            Name = "large_result_fingerprint_limit",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["payload"],"properties":{"payload":{"type":"string"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(payload, result.StructuredContent!.Value.GetProperty("payload").GetString());
        }, configure: options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
                await next(request, cancellationToken));
        });
    }

    [Fact]
    public async Task CallTool_OuterAlternateFilterCanRepairInnerFilterOutput()
    {
        McpServerTool tool = McpServerTool.Create(() => new { Expected = 1 }, new()
        {
            Name = "alternate_filter_repair",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(1, result.StructuredContent!.Value.GetProperty("expected").GetInt32());
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // Exercise alternate filters in nested registration order.
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                ResultOrAlternate<CallToolResult> result = await next(request, cancellationToken);
                result.Result!.StructuredContent = ParseJson("""{"expected":1}""");
                return result;
            });
            options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
            {
                ResultOrAlternate<CallToolResult> result = await next(request, cancellationToken);
                result.Result!.StructuredContent = ParseJson("""{"unexpected":1}""");
                return result;
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ValidatesToolSelectedByAlternateFilterAfterRouting()
    {
        McpServerTool? tool = null;
        tool = McpServerTool.Create(() =>
        {
            tool!.ProtocolTool.OutputSchema = null;
            return new { Unexpected = 1 };
        }, new()
        {
            Name = "alternate_filter_routed_tool",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                "initially_unknown_tool", cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // Exercise alternate routing to a registered tool.
            options.Filters.Request.CallToolWithAlternateFilters.Add((request, next, cancellationToken) =>
            {
                request.Params.Name = tool.ProtocolTool.Name;
                return ReplaceResultAsync(next(request, cancellationToken));
            });
#pragma warning restore MCPEXP002

#pragma warning disable MCPEXP002 // Preserve alternate-result metadata while replacing the result.
            static async ValueTask<ResultOrAlternate<CallToolResult>> ReplaceResultAsync(
                ValueTask<ResultOrAlternate<CallToolResult>> pending)
            {
                ResultOrAlternate<CallToolResult> response = await pending;
                CallToolResult original = response.Result!;
                return new CallToolResult
                {
                    Content = original.Content,
                    StructuredContent = original.StructuredContent,
                    IsError = original.IsError,
                    Meta = original.Meta,
                };
            }
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_AlternateFilterChangingKnownNameToUnknownDoesNotInvokeKnownTool()
    {
        int calls = 0;
        McpServerTool tool = McpServerTool.Create(() =>
        {
            calls++;
            return new CallToolResult { Content = [] };
        }, new() { Name = "alternate_filter_known_tool" });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
                await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("Unknown tool", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, calls);
        }, configure: options =>
        {
#pragma warning disable MCPEXP002 // Exercise alternate routing away from a registered tool.
            options.Filters.Request.CallToolWithAlternateFilters.Add((request, next, cancellationToken) =>
            {
                request.Params.Name = "unknown_after_filter";
                return next(request, cancellationToken);
            });
#pragma warning restore MCPEXP002
        });
    }

    [Fact]
    public async Task CallTool_ReusedResultRetainsEachDispatchSchemaSnapshot()
    {
        CallToolResult shared = new() { Content = [], StructuredContent = ParseJson("""{"b":1}""") };
        McpServerTool tool = McpServerTool.Create(() => shared, new()
        {
            Name = "reused_output_result",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["a"]}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(
                tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                CallToolResult first = await next(request, cancellationToken);
                tool.ProtocolTool.OutputSchema = ParseJson("""{"type":"object","required":["b"]}""");
                await next(request, cancellationToken);
                return first;
            });
        });
    }

    [Fact]
    public async Task CallTool_LiteralReferenceInSchemaExampleDoesNotSkipValidation()
    {
        McpServerTool tool = McpServerTool.Create(() => ParseJson("""{"ref":"expected"}"""), new()
        {
            Name = "literal_ref_example",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","properties":{"ref":{"const":"expected"}},"examples":[{"$ref":"https://example.com/data"}]}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                CallToolResult result = await next(request, cancellationToken);
                result.StructuredContent = ParseJson("""{"ref":"unexpected"}""");
                return result;
            });
        });
    }

    [Fact]
    public async Task CallTool_AbsoluteReferenceToLocalSchemaResourceIsValidated()
    {
        McpServerTool tool = McpServerTool.Create(() => ParseJson("""{"expected":1}"""), new()
        {
            Name = "local_absolute_ref",
            UseStructuredContent = true,
            OutputSchema = ParseJson(
                """{"$id":"https://example.test/output","$ref":"https://example.test/output#/$defs/result","$defs":{"result":{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}}}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
            {
                CallToolResult result = await next(request, cancellationToken);
                result.StructuredContent = ParseJson("""{"unexpected":1}""");
                return result;
            });
        });
    }

    [Fact]
    public async Task CallTool_ExternalSchemaReferenceDoesNotFetchRemoteSchema()
    {
        McpServerTool tool = McpServerTool.Create(() => ParseJson("""{"anything":1}"""), new()
        {
            Name = "external_ref",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"$ref":"https://example.invalid/output"}"""),
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(1, result.StructuredContent!.Value.GetProperty("anything").GetInt32());
        });
    }

    [Theory]
    [InlineData("converter")]
    [InlineData("enum")]
    [InlineData("struct")]
    public async Task CallTool_ReturnsErrorWhenSafeRecoveryCannotSatisfyOutputSchema(string valueKind)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        object output;
        JsonElement outputSchema;
        if (valueKind == "converter")
        {
            output = new UnsafeDefaultConverterOutput();
            outputSchema = ParseJson("""{"type":"object","required":["value"],"properties":{"value":{"const":"not-for-output"}}}""");
        }
        else if (valueKind == "enum")
        {
            options.Converters.Add(new JsonStringEnumConverter(new DisclosingEnumNamingPolicy()));
            output = new UnsafeDefaultEnumOutput();
            outputSchema = ParseJson("""{"type":"object","required":["value"],"properties":{"value":{"const":"not-for-output"}}}""");
        }
        else
        {
            output = new UnsafeDefaultStructOutput();
            outputSchema = ParseJson("""{"type":"object","required":["child"],"properties":{"child":{"type":"object","required":["secret"]}}}""");
        }

        McpServerTool tool = McpServerTool.Create(() => output, new()
        {
            Name = "unsafe_default",
            UseStructuredContent = true,
            OutputSchema = outputSchema,
            SerializerOptions = options,
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsError);
            Assert.Null(result.StructuredContent);
            string message = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Equal("The tool could not produce structured content that conforms to its advertised output schema.", message);
            Assert.DoesNotContain("not-for-output", message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CallTool_ValidatesAgainstOutputSchemaCapturedBeforeToolInvocation()
    {
        McpServerTool? tool = null;
        tool = McpServerTool.Create(() =>
        {
            tool!.ProtocolTool.OutputSchema = null;
            return new { Unexpected = 1 };
        }, new()
        {
            Name = "changed_output_schema",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
            Assert.Null(tool.ProtocolTool.OutputSchema);
        });
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task CallTool_ValidatesExplicitResultAgainstCapturedSchema(string protocolVersion)
    {
        McpServerTool? tool = null;
        tool = McpServerTool.Create(() =>
        {
            tool!.ProtocolTool.OutputSchema = null;
            return new CallToolResult
            {
                Content = [],
                StructuredContent = ParseJson("""{"unexpected":1}"""),
            };
        }, new()
        {
            Name = "changed_explicit_result_schema",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
        });

        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            CallToolResult result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
            Assert.Null(tool.ProtocolTool.OutputSchema);
        });
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task CallTool_ValidatesExplicitResultAgainstUnchangedSchema(string protocolVersion)
    {
        McpServerTool tool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [],
            StructuredContent = ParseJson("""{"unexpected":1}"""),
        }, new()
        {
            Name = "explicit_result_unchanged_schema",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
        });

        await WithClientAsync(tool, protocolVersion, async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallTool_ResponseFiltersCanRepairIncompleteExplicitResult(bool alternateFilter)
    {
        McpServerTool tool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [new TextContentBlock { Text = "original content" }],
            StructuredContent = ParseJson("""{"unexpected":1}"""),
        }, new()
        {
            Name = "filter_repairs_explicit_result",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
        });

        bool? filterSawError = null;
        string? filterSawContent = null;
        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, filterSawError);
            Assert.Equal("original content", filterSawContent);
            Assert.NotEqual(true, result.IsError);
            Assert.Equal("original content", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.Equal(1, result.StructuredContent!.Value.GetProperty("expected").GetInt32());
        }, configure: options =>
        {
            void Repair(CallToolResult result)
            {
                filterSawError = result.IsError;
                filterSawContent = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
                result.StructuredContent = ParseJson("""{"expected":1}""");
            }

            if (alternateFilter)
            {
#pragma warning disable MCPEXP002 // Exercise response repair by alternate-result filters.
                options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
                {
                    ResultOrAlternate<CallToolResult> result = await next(request, cancellationToken);
                    Repair(result.Result!);
                    return result;
                });
#pragma warning restore MCPEXP002
            }
            else
            {
                options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
                {
                    CallToolResult result = await next(request, cancellationToken);
                    Repair(result);
                    return result;
                });
            }
        });
    }

    [Theory]
    [InlineData("2025-11-25", "element", false)]
    [InlineData("2025-11-25", "element", true)]
    [InlineData("2025-11-25", "node", false)]
    [InlineData("2025-11-25", "node", true)]
    [InlineData("2026-07-28", "element", false)]
    [InlineData("2026-07-28", "element", true)]
    [InlineData("2026-07-28", "node", false)]
    [InlineData("2026-07-28", "node", true)]
    public async Task CallTool_ValidatesSerializedJsonAgainstCapturedSchema(
        string protocolVersion, string jsonKind, bool clearSchema)
    {
        McpServerTool? tool = null;
        object structuredContent = jsonKind == "element"
            ? ParseJson("""{"unexpected":1}""")
            : JsonNode.Parse("""{"unexpected":1}""")!;
        tool = McpServerTool.Create(() =>
        {
            if (clearSchema)
            {
                tool!.ProtocolTool.OutputSchema = null;
            }

            return structuredContent;
        }, new()
        {
            Name = "changed_serialized_json_schema",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
        });

        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            CallToolResult result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
            Assert.Equal(clearSchema, tool.ProtocolTool.OutputSchema is null);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallTool_ValidatesStructuredContentAfterResponseFilters(bool alternateFilter)
    {
        McpServerTool tool = McpServerTool.Create(() => new { Expected = 1 }, new()
        {
            Name = "filtered_structured_content",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["expected"],"properties":{"expected":{"type":"integer"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        }, configure: options =>
        {
            if (alternateFilter)
            {
#pragma warning disable MCPEXP002 // Exercise response mutation by alternate-result filters.
                options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
                {
                    ResultOrAlternate<CallToolResult> result = await next(request, cancellationToken);
                    if (!result.IsAlternate)
                    {
                        result.Result!.StructuredContent = ParseJson("""{"unexpected":1}""");
                    }

                    return result;
                });
#pragma warning restore MCPEXP002
            }
            else
            {
                options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
                {
                    CallToolResult result = await next(request, cancellationToken);
                    result.StructuredContent = ParseJson("""{"unexpected":1}""");
                    return result;
                });
            }
        });
    }

    [Fact]
    public async Task CallTool_UsesCapturedIntermediateSchemaForRecovery()
    {
        JsonElement requiredSchema = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        JsonElement replacementSchema = ParseJson("""{"type":"object"}""");
        McpServerTool inner = McpServerTool.Create(() => new MutableNote(), new()
        {
            Name = "intermediate_schema_recovery",
            UseStructuredContent = true,
            OutputSchema = replacementSchema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        var intermediate = new SchemaOverrideTool(inner, requiredSchema);
        McpServerTool tool = new MutatingInvocationTool(
            intermediate, () => intermediate.ProtocolTool.OutputSchema = replacementSchema);

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            CallToolResult result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(JsonValueKind.Null, result.StructuredContent!.Value.GetProperty("note").ValueKind);
            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent.Value).IsValid);
        });
    }

    [Fact]
    public async Task CallTool_DoesNotRecoverAfterDispatchContextExpires()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        McpServerTool inner = McpServerTool.Create(async () =>
        {
            entered.TrySetResult(true);
            await release.Task;
            return new MutableNote();
        }, new()
        {
            Name = "late_result",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        var fireAndForget = new FireAndForgetInvocationTool(inner, entered);
        McpServerTool tool = new SchemaOverrideTool(fireAndForget, ParseJson("""{"type":"object"}"""));

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            try
            {
                CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);
                Assert.NotEqual(true, result.IsError);

                release.TrySetResult(true);
                CallToolResult lateResult = await fireAndForget.Pending!.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
                Assert.Null(lateResult.StructuredContent);
            }
            finally
            {
                release.TrySetResult(true);
            }
        });
    }

    [Theory]
    [InlineData("2025-11-25", false, false, false)]
    [InlineData("2025-11-25", false, true, false)]
    [InlineData("2025-11-25", true, false, false)]
    [InlineData("2025-11-25", true, true, false)]
    [InlineData("2025-11-25", false, false, true)]
    [InlineData("2025-11-25", false, true, true)]
    [InlineData("2025-11-25", true, false, true)]
    [InlineData("2025-11-25", true, true, true)]
    [InlineData("2026-07-28", false, false, false)]
    [InlineData("2026-07-28", true, true, true)]
    public async Task CallTool_UsesCapturedSchemaForWireShapeWhenSchemaChanges(
        string protocolVersion, bool replaceSchema, bool delegated, bool invalid)
    {
        JsonElement schema = ParseJson(invalid
            ? """{"type":"array","items":{"const":2}}"""
            : """{"type":"array","items":{"type":"integer"}}""");
        McpServerTool? inner = null;
        inner = McpServerTool.Create(() =>
        {
            inner!.ProtocolTool.OutputSchema = replaceSchema ? ParseJson("""{"type":"object"}""") : null;
            return new[] { 1 };
        }, new()
        {
            Name = "changed_array_schema",
            UseStructuredContent = true,
            OutputSchema = schema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = delegated
            ? new SchemaOverrideTool(new ForwardingInvocationTool(inner, 1), schema)
            : inner;

        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            CallToolResult result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            if (invalid)
            {
                AssertSchemaOutputError(result);
            }
            else
            {
                Assert.NotEqual(true, result.IsError);
                JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
                Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
                JsonElement array = protocolVersion == "2026-07-28" ? content : content.GetProperty("result");
                Assert.Equal(1, Assert.Single(array.EnumerateArray()).GetInt32());
            }
        });
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task CallTool_CapturesInnerSchemaBeforeDelegatorRuns(string protocolVersion)
    {
        JsonElement schema = ParseJson("""{"type":"array","items":{"type":"integer"}}""");
        McpServerTool inner = McpServerTool.Create(() => new[] { 1 }, new()
        {
            Name = "delegator_changes_inner_schema",
            UseStructuredContent = true,
            OutputSchema = schema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(
            new MutatingInvocationTool(inner, () => inner.ProtocolTool.OutputSchema = null), schema);

        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            CallToolResult result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
            JsonElement array = protocolVersion == "2026-07-28" ? content : content.GetProperty("result");
            Assert.Equal(1, Assert.Single(array.EnumerateArray()).GetInt32());
        });
    }

    [Fact]
    public async Task CallTool_RecoversSafeDefaultScalarAndValidatesFinalOutput()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        McpServerTool tool = McpServerTool.Create(() => new SafeDefaultScalarOutput(), new()
        {
            Name = "safe_default",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["count"],"properties":{"count":{"const":0}}}"""),
            SerializerOptions = options,
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(0, result.StructuredContent!.Value.GetProperty("count").GetInt32());
        });
    }

    private static void AssertSchemaOutputError(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        string message = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("The tool could not produce structured content that conforms to its advertised output schema.", message);
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task CallTool_RepeatedInnerInvocations_VisitOnlyTheRetainedResultPlan(string protocolVersion)
    {
        McpServerTool inner = McpServerTool.Create(() => new MutableNote(), new()
        {
            Name = "indexed_note",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        var repeated = new ObservedRecoveryTool(inner);
        McpServerTool tool = new SchemaOverrideTool(repeated, ParseJson("""{"required":["note"]}"""));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(JsonValueKind.Null, result.StructuredContent!.Value.GetProperty("note").ValueKind);
            Assert.Equal(1, repeated.EarlierPlanVisits);
        });
    }

    [Theory]
    [InlineData(1024 * 1024, 16, true)]
    [InlineData(1024 * 1024, 16, false)]
    [InlineData(0, 160, true)]
    [InlineData(0, 160, false)]
    public async Task CallTool_RepeatedInnerInvocations_BoundRetainedRecoveryPlans(int payloadSize, int invocationCount, bool returnFirst)
    {
        string payload = new('x', payloadSize);
        McpServerTool inner = McpServerTool.Create(() => new LargeNote(payload), new()
        {
            Name = "large_note",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        var repeated = new BudgetObservingTool(inner, invocationCount, returnFirst);
        McpServerTool tool = new SchemaOverrideTool(
            repeated, ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}"""));

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(repeated.PeakPlanCount < invocationCount);
            Assert.InRange(repeated.PeakRetainedBytes, 0, 4 * 1024 * 1024);
            if (!returnFirst)
            {
                AssertSchemaOutputError(result);
            }
            else
            {
                Assert.NotEqual(true, result.IsError);
                Assert.Equal(JsonValueKind.Null, result.StructuredContent!.Value.GetProperty("note").ValueKind);
            }
        });
    }

    [Fact]
    public async Task CallTool_RepeatedWhitespaceHeavyResults_BoundRetainedRecoveryPlans()
    {
        const int invocationCount = 12;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new WhitespaceNoteConverter(1024 * 1024));
        McpServerTool inner = McpServerTool.Create(() => new LargeNote("value"), new()
        {
            Name = "whitespace_note",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
            SerializerOptions = options,
        });
        var repeated = new BudgetObservingTool(inner, invocationCount, returnFirst: false);
        McpServerTool tool = new SchemaOverrideTool(repeated, ParseJson("""{"required":["note"]}"""));

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(repeated.PeakPlanCount < invocationCount);
            Assert.InRange(repeated.PeakRetainedBytes, 0, 4 * 1024 * 1024);
            AssertSchemaOutputError(result);
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task CallTool_OverlappingFilterDispatches_KeepIndependentRecovery(
        string protocolVersion, bool suspendAfterInner)
    {
        foreach (bool alternateHandler in new[] { false, true })
        {
            foreach (bool secondThrows in new[] { false, true })
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                McpServerTool inner = McpServerTool.Create(() => new MutableNote(), new()
                {
                    Name = "overlapping_note",
                    UseStructuredContent = true,
                    OutputSchema = ParseJson("""{"type":"object"}"""),
                    SerializerOptions = new(JsonSerializerDefaults.Web)
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                    },
                });
                McpServerTool tool = new SchemaOverrideTool(
                    new GatedInvocationTool(inner, entered, release, suspendAfterInner, secondThrows),
                    ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}"""));
                await WithClientAsync(tool, protocolVersion, async client =>
                {
                    var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

                    Assert.NotEqual(true, result.IsError);
                    Assert.Equal(JsonValueKind.Null, result.StructuredContent!.Value.GetProperty("note").ValueKind);
                }, alternateHandler, options =>
                {
                    if (alternateHandler)
                    {
#pragma warning disable MCPEXP002 // Exercise overlapping dispatch through the alternate pipeline.
                        options.Filters.Request.CallToolWithAlternateFilters.Add(async (request, next, cancellationToken) =>
                        {
                            var first = next(request, cancellationToken).AsTask();
                            await entered.Task.WaitAsync(cancellationToken);
                            try { await next(request, cancellationToken); }
                            catch (InvalidOperationException exception) when (exception.Message == "Second dispatch failed.") { }
                            finally { release.TrySetResult(true); }
                            return await first;
                        });
#pragma warning restore MCPEXP002
                    }
                    else
                    {
                        options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) =>
                        {
                            var first = next(request, cancellationToken).AsTask();
                            await entered.Task.WaitAsync(cancellationToken);
                            try { await next(request, cancellationToken); }
                            catch (InvalidOperationException exception) when (exception.Message == "Second dispatch failed.") { }
                            finally { release.TrySetResult(true); }
                            return await first;
                        });
                    }
                });
            }
        }
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DeferredRecovery_PreservesWrapperOwnedResults(string protocolVersion)
    {
        foreach (int mode in new[] { 0, 1, 2 })
        {
            foreach (bool alternateHandler in new[] { false, true })
            {
                McpServerTool inner = McpServerTool.Create(() => new MutableCount(), new()
                {
                    Name = "owned_count",
                    UseStructuredContent = true,
                    OutputSchema = ParseJson("""{"type":"object"}"""),
                    SerializerOptions = new(JsonSerializerDefaults.Web)
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                    },
                });
                McpServerTool tool = new SchemaOverrideTool(
                    new OwnedResultTool(inner, mode), ParseJson("""{"required":["count"]}"""));
                await WithClientAsync(tool, protocolVersion, async client =>
                {
                    var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

                    Assert.Equal(mode != 1, result.IsError == true);
                    if (mode == 1)
                    {
                        Assert.Equal("""{"count":99}""", result.StructuredContent!.Value.GetRawText());
                    }
                    else if (mode == 2)
                    {
                        AssertSchemaOutputError(result);
                    }
                    else
                    {
                        Assert.Equal("{}", result.StructuredContent!.Value.GetRawText());
                    }
                }, alternateHandler);
            }
        }
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DeferredRecovery_DoesNotReadSourceOrRunConvertersAfterWrapperCompletion(string protocolVersion)
    {
        var source = new ExpiringOutput();
        var converter = new ExpiringIntConverter();
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(converter);
        McpServerTool inner = McpServerTool.Create(() => source, new()
        {
            Name = "expired_count",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
            SerializerOptions = options,
        });
        McpServerTool tool = new SchemaOverrideTool(new RequestStateTool(inner, null, () =>
        {
            source.Expired = true;
            converter.Expired = true;
        }), ParseJson("""{"required":["count"],"properties":{"count":{"const":"zero"}}}"""));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(source.Expired);
            Assert.True(converter.Expired);
            Assert.Equal(0, converter.WriteCalls);
            AssertSchemaOutputError(result);
        });
    }

    [Fact]
    public async Task CallTool_DoesNotExposeEnumConverterNamingPolicyForOmittedDefault()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(new DisclosingEnumNamingPolicy()));
        var source = new DisclosingEnumOutput();
        Assert.False(JsonSerializer.SerializeToElement(source, options).TryGetProperty("value", out _));
        McpServerTool tool = McpServerTool.Create(() => source, new()
        {
            Name = "enum_value",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"required":["value"],"properties":{"value":{"const":"not-for-output"}}}"""),
            SerializerOptions = options,
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsError);
            Assert.Null(result.StructuredContent);
            string message = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Equal("The tool could not produce structured content that conforms to its advertised output schema.", message);
            Assert.DoesNotContain("not-for-output", message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CallTool_DoesNotExposeOmittedDefaultStruct()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var source = new DisclosingStructOutput();
        Assert.False(JsonSerializer.SerializeToElement(source, options).TryGetProperty("child", out _));
        McpServerTool tool = McpServerTool.Create(() => source, new()
        {
            Name = "struct_value",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"required":["child"]}"""),
            SerializerOptions = options,
        });

        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            CallToolResult result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsError);
            Assert.Null(result.StructuredContent);
            string message = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Equal("The tool could not produce structured content that conforms to its advertised output schema.", message);
            Assert.DoesNotContain("not-for-output", message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_ForwardingSegment_RecoversBeforeTransformingWrapper(
        string protocolVersion, bool requiresNull)
    {
        JsonElement required = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        JsonElement optional = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}""");
        foreach (int forwardingMode in new[] { 1, 2, 5 })
        {
            foreach (bool alternateHandler in new[] { false, true })
            {
                McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
                {
                    Name = "stacked_note",
                    UseStructuredContent = true,
                    OutputSchema = requiresNull ? optional : required,
                    SerializerOptions = new(JsonSerializerDefaults.Web)
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                    },
                });
                McpServerTool tool = new PayloadTool(new SchemaOverrideTool(
                    new ForwardingInvocationTool(inner, forwardingMode), requiresNull ? required : optional));
                tool = new PayloadTool(new ForwardingInvocationTool(tool, 2));
                await WithClientAsync(tool, protocolVersion, async client =>
                {
                    var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
                    var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

                    Assert.NotEqual(true, result.IsError);
                    Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent!.Value).IsValid);
                    JsonElement content = result.StructuredContent.Value.GetProperty("payload").GetProperty("payload");
                    Assert.Equal(requiresNull, content.TryGetProperty("note", out _));
                }, alternateHandler);
            }
        }
    }

    [Theory]
    [InlineData("2025-06-18", "object")]
    [InlineData("2025-06-18", "array")]
    [InlineData("2025-06-18", "nested")]
    [InlineData("2025-11-25", "object")]
    [InlineData("2025-11-25", "array")]
    [InlineData("2025-11-25", "nested")]
    [InlineData("2026-07-28", "object")]
    [InlineData("2026-07-28", "array")]
    [InlineData("2026-07-28", "nested")]
    public async Task ListTools_ForwardingInvocation_CapturesOmittedValuesBeforeSourceMutation(
        string protocolVersion, string shape)
    {
        const string requiredCount = """{"type":"object","required":["count"],"properties":{"count":{"type":"integer"}}}""";
        string schema = shape switch
        {
            "array" => $$"""{"type":"array","items":{{requiredCount}}}""",
            "nested" => $$$$"""{"type":"object","required":["child"],"properties":{"child":{{{{requiredCount}}}}}}""",
            _ => requiredCount,
        };
        foreach (int forwardingMode in new[] { 1, 2, 5 })
        {
            foreach (bool alternateHandler in new[] { false, true })
            {
                var source = new MutableCount();
                object value = shape switch
                {
                    "array" => new[] { source },
                    "nested" => new { Child = source },
                    _ => source,
                };
                McpServerTool inner = McpServerTool.Create(() => value, new()
                {
                    Name = "captured_count",
                    UseStructuredContent = true,
                    OutputSchema = ParseJson(shape == "array" ? """{"type":"array"}""" : """{"type":"object"}"""),
                    SerializerOptions = new(JsonSerializerDefaults.Web)
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                    },
                });
                McpServerTool tool = new SchemaOverrideTool(new RequestStateTool(
                    new ForwardingInvocationTool(inner, forwardingMode), null, () => source.Count = 42), ParseJson(schema));
                await WithClientAsync(tool, protocolVersion, async client =>
                {
                    var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
                    var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
                    JsonElement content = result.StructuredContent!.Value;

                    Assert.Equal(42, source.Count);
                    Assert.NotEqual(true, result.IsError);
                    Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
                    if (shape == "array")
                    {
                        if (protocolVersion != "2026-07-28")
                        {
                            content = content.GetProperty("result");
                        }
                        content = content[0];
                    }
                    else if (shape == "nested")
                    {
                        content = content.GetProperty("child");
                    }
                    Assert.Equal(0, content.GetProperty("count").GetInt32());
                }, alternateHandler);
            }
        }
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_ConcurrentForwardingInvocation_CapturesEachResultBeforeMutation(
        string protocolVersion, bool returnSecond)
    {
        var source = new MutableCount();
        int started = 0;
        var bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        McpServerTool inner = McpServerTool.Create(async () =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.TrySetResult(true);
            }
            await bothStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            return source;
        }, new()
        {
            Name = "concurrent_captured_count",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(
            new ConcurrentInvocationTool(inner, returnSecond, () => source.Count = 42),
            ParseJson("""{"type":"object","required":["count"],"properties":{"count":{"type":"integer"}}}"""));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, started);
            Assert.Equal(42, source.Count);
            Assert.NotEqual(true, result.IsError);
            Assert.Equal(0, result.StructuredContent!.Value.GetProperty("count").GetInt32());
        });
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_ForwardingInvocation_RecoversRegisteredSchemaWhenSegmentLimitIsExceeded(string protocolVersion)
    {
        McpServerTool tool = McpServerTool.Create(() => new MutableCount(), new()
        {
            Name = "bounded_forwarding",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        for (int i = 0; i < 129; i++)
        {
            tool = new ForwardingInvocationTool(tool, 1);
        }
        tool = new SchemaOverrideTool(tool, ParseJson("""{"required":["count"]}"""));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(0, result.StructuredContent!.Value.GetProperty("count").GetInt32());
        });
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_ForwardingInvocation_CapturesSchemaBeforeUserCodeRuns(string protocolVersion)
    {
        JsonElement required = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        JsonElement optional = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}""");
        McpServerTool? tool = null;
        McpServerTool inner = McpServerTool.Create(() =>
        {
            tool!.ProtocolTool.OutputSchema = optional;
            return new { Note = (string?)null };
        }, new()
        {
            Name = "changed_schema",
            UseStructuredContent = true,
            OutputSchema = optional,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        tool = new SchemaOverrideTool(new ForwardingInvocationTool(inner, 1), required);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(optional, tool.ProtocolTool.OutputSchema);
            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent!.Value).IsValid);
            Assert.Equal(JsonValueKind.Null, result.StructuredContent.Value.GetProperty("note").ValueKind);
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_TransformingDelegator_RecoversAgainstInnerSchema(string protocolVersion, bool overrideInnerSchema)
    {
        JsonElement required = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "transformed_note",
            UseStructuredContent = true,
            OutputSchema = overrideInnerSchema ? ParseJson("""{"type":"object"}""") : required,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        if (overrideInnerSchema)
        {
            inner = new SchemaOverrideTool(inner, required);
        }
        McpServerTool tool = new DelegatingTool(new PayloadTool(inner));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent!.Value).IsValid);
            Assert.Equal(JsonValueKind.Null, result.StructuredContent.Value.GetProperty("payload").GetProperty("note").ValueKind);
        });
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_StackedTransformingDelegator_RecoversBeforeOuterTransformation(string protocolVersion)
    {
        JsonElement required = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        JsonElement optional = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}""");
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "stacked_transformed_note",
            UseStructuredContent = true,
            OutputSchema = optional,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });

        McpServerTool tool = new PayloadTool(
            new SchemaOverrideTool(new ForwardingInvocationTool(inner, 1), required));

        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            JsonElement payload = result.StructuredContent!.Value.GetProperty("payload");
            Assert.Equal(JsonValueKind.Null, payload.GetProperty("note").ValueKind);
            Assert.True(ParseSchema(tool.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent.Value).IsValid);
        });
    }

    [Theory]
    [InlineData("2025-06-18", false, 0)]
    [InlineData("2025-06-18", true, 0)]
    [InlineData("2025-11-25", false, 0)]
    [InlineData("2025-11-25", true, 0)]
    [InlineData("2026-07-28", false, 0)]
    [InlineData("2026-07-28", true, 0)]
    [InlineData("2025-06-18", false, 1)]
    [InlineData("2025-06-18", true, 1)]
    [InlineData("2025-11-25", false, 1)]
    [InlineData("2025-11-25", true, 1)]
    [InlineData("2026-07-28", false, 1)]
    [InlineData("2026-07-28", true, 1)]
    [InlineData("2025-06-18", false, 2)]
    [InlineData("2025-06-18", true, 2)]
    [InlineData("2025-11-25", false, 2)]
    [InlineData("2025-11-25", true, 2)]
    [InlineData("2026-07-28", false, 2)]
    [InlineData("2026-07-28", true, 2)]
    [InlineData("2025-06-18", false, 3)]
    [InlineData("2025-06-18", true, 3)]
    [InlineData("2025-11-25", false, 3)]
    [InlineData("2025-11-25", true, 3)]
    [InlineData("2026-07-28", false, 3)]
    [InlineData("2026-07-28", true, 3)]
    [InlineData("2025-06-18", false, 4)]
    [InlineData("2025-06-18", true, 4)]
    [InlineData("2025-11-25", false, 4)]
    [InlineData("2025-11-25", true, 4)]
    [InlineData("2026-07-28", false, 4)]
    [InlineData("2026-07-28", true, 4)]
    [InlineData("2025-06-18", false, 5)]
    [InlineData("2025-06-18", true, 5)]
    [InlineData("2025-11-25", false, 5)]
    [InlineData("2025-11-25", true, 5)]
    [InlineData("2026-07-28", false, 5)]
    [InlineData("2026-07-28", true, 5)]
    public async Task ListTools_DelegatingTool_RecoversAgainstEffectiveSchema(
        string protocolVersion, bool outerRequiresNull, int forwardingMode)
    {
        JsonElement requiredSchema = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        JsonElement optionalSchema = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}""");
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "effective_note",
            UseStructuredContent = true,
            OutputSchema = outerRequiresNull ? optionalSchema : requiredSchema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool forwarded = new DelegatingTool(inner);
        if (forwardingMode != 0)
        {
            forwarded = new ForwardingInvocationTool(forwarded, forwardingMode);
        }
        McpServerTool tool = new SchemaOverrideTool(forwarded, outerRequiresNull ? requiredSchema : optionalSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement content = result.StructuredContent!.Value;

            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
            Assert.Equal(outerRequiresNull, content.TryGetProperty("note", out _));
        }, alternateHandler: forwardingMode == 4);
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_ForwardingArrayInvocation_PreservesWireShape(string protocolVersion, bool outerRequiresNull)
    {
        JsonElement requiredSchema = ParseJson("""{"type":"array","items":{"required":["note"],"properties":{"note":{"type":"null"}}}}""");
        JsonElement optionalSchema = ParseJson("""{"type":"array","items":{"properties":{"note":{"type":"string"}}}}""");
        McpServerTool inner = McpServerTool.Create(() => new[] { new { Note = (string?)null } }, new()
        {
            Name = "forwarded_array",
            UseStructuredContent = true,
            OutputSchema = outerRequiresNull ? optionalSchema : requiredSchema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(
            new ForwardingInvocationTool(inner, 2), outerRequiresNull ? requiredSchema : optionalSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement content = result.StructuredContent!.Value;

            Assert.NotEqual(true, result.IsError);
            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
            if (protocolVersion != "2026-07-28")
            {
                content = content.GetProperty("result");
            }
            Assert.Equal(outerRequiresNull, content[0].TryGetProperty("note", out _));
        });
    }

    [Fact]
    public async Task ListTools_ForwardingInvocation_RecoversAfterAsynchronousCompletion()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        JsonElement requiredSchema = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        McpServerTool inner = McpServerTool.Create(async () =>
        {
            started.TrySetResult(true);
            await release.Task;
            return new { Note = (string?)null };
        }, new()
        {
            Name = "async_forwarded_note",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(new ForwardingInvocationTool(inner, 2), requiredSchema);
        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            Task<CallToolResult> pending = client.CallToolAsync(tool.ProtocolTool.Name,
                cancellationToken: TestContext.Current.CancellationToken).AsTask();
            try
            {
                Assert.Same(started.Task, await Task.WhenAny(started.Task, pending));
            }
            finally
            {
                release.TrySetResult(true);
            }

            CallToolResult result = await pending;
            Assert.NotEqual(true, result.IsError);
            Assert.True(ParseSchema(requiredSchema).Evaluate(result.StructuredContent!.Value).IsValid);
            Assert.Equal(JsonValueKind.Null, result.StructuredContent.Value.GetProperty("note").ValueKind);
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_ForwardingInvocation_PreservesRegisteredSchemaWhenMatchChanges(
        string protocolVersion, bool outerRequiresNull)
    {
        JsonElement required = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        JsonElement optional = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}""");
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "rebound_note",
            UseStructuredContent = true,
            OutputSchema = outerRequiresNull ? optional : required,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(
            new RequestStateTool(inner, inner, null), outerRequiresNull ? required : optional);
        foreach (bool alternateHandler in new[] { false, true })
        {
            await WithClientAsync(tool, protocolVersion, async client =>
            {
                var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
                var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

                Assert.NotEqual(true, result.IsError);
                Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent!.Value).IsValid);
                Assert.Equal(outerRequiresNull, result.StructuredContent.Value.TryGetProperty("note", out _));
            }, alternateHandler);
        }
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_ForwardingInvocation_PreservesValidJsonAfterSourceMutation(
        string protocolVersion, bool arrayResult)
    {
        const string itemSchema = """{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""";
        string schema = arrayResult ? $$"""{"type":"array","items":{{itemSchema}}}""" : itemSchema;
        foreach (bool alternateHandler in new[] { false, true })
        {
            var source = new MutableNote();
            McpServerTool inner = McpServerTool.Create(() => arrayResult ? (object)new[] { source } : source, new()
            {
                Name = "mutated_note",
                UseStructuredContent = true,
                OutputSchema = ParseJson(schema),
                SerializerOptions = new(JsonSerializerDefaults.Web)
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                },
            });
            McpServerTool tool = new SchemaOverrideTool(
                new RequestStateTool(inner, null, () => source.Note = "changed after serialization"), ParseJson(schema));
            await WithClientAsync(tool, protocolVersion, async client =>
            {
                var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
                var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
                JsonElement content = result.StructuredContent!.Value;

                Assert.Equal("changed after serialization", source.Note);
                Assert.NotEqual(true, result.IsError);
                Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
                if (arrayResult)
                {
                    if (protocolVersion != "2026-07-28")
                    {
                        content = content.GetProperty("result");
                    }
                    content = content[0];
                }
                Assert.Equal(JsonValueKind.Null, content.GetProperty("note").ValueKind);
            }, alternateHandler);
        }
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_ForwardingInvocation_PreservesInnerProjectionWhenOuterRecoveryFails(
        string protocolVersion, bool invalidSchema)
    {
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "unrecoverable_outer_note",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["note"]}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(new ForwardingInvocationTool(inner, 1),
            ParseJson(invalidSchema ? """{"type":42}""" : """{"required":["note","missing"]}"""));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsError);
            Assert.Null(result.StructuredContent);
            string message = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Equal("The tool could not produce structured content that conforms to its advertised output schema.", message);
        });
    }

    [Fact]
    public async Task ListTools_ForwardingInvocation_DoesNotRestartRecoveryAfterRegexTimeout()
    {
        string real = new string('a', 26) + "!";
        McpServerTool inner = McpServerTool.Create(() => new { Real = real, Note = (string?)null }, new()
        {
            Name = "timed_out_forwarded_note",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","properties":{"real":{"pattern":"^(a+)+$"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(new ForwardingInvocationTool(inner, 1),
            ParseJson("""{"type":"object","required":["note"]}"""));
        await WithClientAsync(tool, "2026-07-28", async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);

            AssertSchemaOutputError(result);
        });
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_TransformingInvocation_ReceivesInnerRecoveryAndRetainsItsOutput(string protocolVersion)
    {
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "transformed_note",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}"""),
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new NoteTransformingTool(inner);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent!.Value).IsValid);
            Assert.Equal("transformed", result.StructuredContent.Value.GetProperty("note").GetString());
        });
    }

    [Theory]
    [InlineData("2025-06-18", 1, false)]
    [InlineData("2025-06-18", 2, false)]
    [InlineData("2025-06-18", 1, true)]
    [InlineData("2025-06-18", 2, true)]
    [InlineData("2025-11-25", 1, false)]
    [InlineData("2025-11-25", 2, false)]
    [InlineData("2025-11-25", 1, true)]
    [InlineData("2025-11-25", 2, true)]
    [InlineData("2026-07-28", 1, false)]
    [InlineData("2026-07-28", 2, false)]
    [InlineData("2026-07-28", 1, true)]
    [InlineData("2026-07-28", 2, true)]
    public async Task ListTools_DelegatingTool_MatchesStructuredContent(string protocolVersion, int depth, bool overrideProtocolTool)
    {
        McpServerTool inner = McpServerTool.Create(() => 72, new() { Name = "count", UseStructuredContent = true });
        McpServerTool tool = inner;
        for (int i = 0; i < depth; i++)
        {
            tool = new DelegatingTool(tool);
        }

        if (overrideProtocolTool)
        {
            tool = new DescribedTool(tool);
        }

        string originalSchema = tool.ProtocolTool.OutputSchema!.Value.GetRawText();
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;
            bool legacy = protocolVersion != "2026-07-28";

            Assert.Equal(legacy ? "object" : "integer", schema.GetProperty("type").GetString());
            if (legacy)
            {
                schema = schema.GetProperty("properties").GetProperty("result");
                Assert.Equal("integer", schema.GetProperty("type").GetString());
                Assert.Equal(72, content.GetProperty("result").GetInt32());
            }
            else
            {
                Assert.Equal(72, content.GetInt32());
            }

            if (overrideProtocolTool)
            {
                Assert.Equal("Wrapped count", listed.Description);
                Assert.Equal(0, schema.GetProperty("minimum").GetInt32());
            }
        });

        Assert.Equal(originalSchema, tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("integer", inner.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DelegatingCustomTool_PreservesItsOutputSchema(string protocolVersion)
    {
        McpServerTool tool = new DelegatingTool(new DelegatingTool(new CustomTool()));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("integer", listed.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(72, result.StructuredContent!.Value.GetInt32());
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_DelegatingTool_RecoversRequiredNullableMembers(string protocolVersion, bool innerUsesComposition)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"note":{"type":"null"}},"required":["note"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        JsonElement innerSchema = innerUsesComposition ? composedSchema : objectSchema;
        JsonElement outerSchema = innerUsesComposition ? objectSchema : composedSchema;
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "required_note",
            UseStructuredContent = true,
            OutputSchema = innerSchema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), outerSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement content = result.StructuredContent!.Value;

            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
            if (innerUsesComposition && protocolVersion != "2026-07-28")
            {
                content = content.GetProperty("result");
            }
            Assert.Equal(JsonValueKind.Null, content.GetProperty("note").ValueKind);
        });

        Assert.Equal(innerSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(outerSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_DelegatingTool_HandlesOptionalOutputSchema(string protocolVersion, bool useStructuredContent)
    {
        JsonElement nullableSchema = ParseJson("""{"type":["object","null"]}""");
        McpServerTool inner = McpServerTool.Create(() => ParseJson("""{"name":"test"}"""), new()
        {
            Name = "optional",
            UseStructuredContent = useStructuredContent,
            OutputSchema = useStructuredContent ? nullableSchema : null,
        });
        await WithClientAsync(new DelegatingTool(inner), protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            if (!useStructuredContent)
            {
                Assert.Null(listed.ProtocolTool.OutputSchema);
                Assert.Null(result.StructuredContent);
            }
            else
            {
                JsonElement type = listed.ProtocolTool.OutputSchema!.Value.GetProperty("type");
                if (protocolVersion == "2026-07-28")
                {
                    Assert.Equal(JsonValueKind.Array, type.ValueKind);
                }
                else
                {
                    Assert.Equal("object", type.GetString());
                }

                Assert.Equal("test", result.StructuredContent!.Value.GetProperty("name").GetString());
                Assert.Equal(JsonValueKind.Array, inner.ProtocolTool.OutputSchema!.Value.GetProperty("type").ValueKind);
            }
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_DelegatingTool_ComposedSchemaMatchesStructuredContent(string protocolVersion, bool innerUsesComposition)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        JsonElement innerSchema = innerUsesComposition ? composedSchema : objectSchema;
        JsonElement outerSchema = innerUsesComposition ? objectSchema : composedSchema;
        McpServerTool inner = McpServerTool.Create(() => ParseJson("""{"name":"test"}"""), new()
        {
            Name = "composed",
            UseStructuredContent = true,
            OutputSchema = innerSchema,
        });
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), outerSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;
            bool legacy = protocolVersion != "2026-07-28";
            bool wrapped = legacy && innerUsesComposition;

            if (legacy)
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
            }
            else
            {
                Assert.Equal(outerSchema.GetRawText(), schema.GetRawText());
            }

            Assert.Equal("test", (wrapped ? content.GetProperty("result") : content).GetProperty("name").GetString());
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText());
            Assert.True(advertisedSchema.Evaluate(content).IsValid);
            JsonElement invalidContent = ParseJson(wrapped ? """{"result":{"name":""}}""" : """{"name":""}""");
            Assert.False(advertisedSchema.Evaluate(invalidContent).IsValid);
        });

        Assert.Equal(outerSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(innerSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DelegatingTool_BooleanSchemaMatchesStructuredContent(string protocolVersion)
    {
        McpServerTool inner = McpServerTool.Create(() => ParseJson("{}"), new()
        {
            Name = "boolean_schema",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
        });
        McpServerTool tool = new SchemaOverrideTool(inner, ParseJson("true"));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;

            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(JsonValueKind.True, schema.ValueKind);
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
                Assert.False(schema.TryGetProperty("properties", out _));
            }

            Assert.Equal(JsonValueKind.Object, content.ValueKind);
            Assert.True(JsonSchema.FromText(schema.GetRawText()).Evaluate(content).IsValid);
        });

        Assert.Equal(JsonValueKind.True, tool.ProtocolTool.OutputSchema!.Value.ValueKind);
    }

    [Theory]
    [InlineData("2025-06-18", "sync", false)]
    [InlineData("2025-06-18", "sync", true)]
    [InlineData("2025-06-18", "task", false)]
    [InlineData("2025-06-18", "task", true)]
    [InlineData("2025-06-18", "valueTask", false)]
    [InlineData("2025-06-18", "valueTask", true)]
    [InlineData("2025-11-25", "sync", false)]
    [InlineData("2025-11-25", "sync", true)]
    [InlineData("2025-11-25", "task", false)]
    [InlineData("2025-11-25", "task", true)]
    [InlineData("2025-11-25", "valueTask", false)]
    [InlineData("2025-11-25", "valueTask", true)]
    [InlineData("2026-07-28", "sync", false)]
    [InlineData("2026-07-28", "sync", true)]
    [InlineData("2026-07-28", "task", false)]
    [InlineData("2026-07-28", "task", true)]
    [InlineData("2026-07-28", "valueTask", false)]
    [InlineData("2026-07-28", "valueTask", true)]
    public async Task ListTools_DelegatingTool_ExplicitResultMatchesStructuredContent(string protocolVersion, string returnKind, bool overrideSchema)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        CallToolResult response = new()
        {
            Content = [new TextContentBlock { Text = "explicit response" }],
            StructuredContent = ParseJson("""{"name":"test"}"""),
            IsError = false,
        };
        Delegate callback = returnKind switch
        {
            "sync" => new Func<CallToolResult>(() => response),
            "task" => new Func<Task<CallToolResult>>(() => Task.FromResult(response)),
            "valueTask" => new Func<ValueTask<CallToolResult>>(() => new(response)),
            _ => throw new ArgumentOutOfRangeException(nameof(returnKind)),
        };
        McpServerTool inner = McpServerTool.Create(callback, new()
        {
            Name = "explicit_result",
            UseStructuredContent = true,
            OutputSchema = composedSchema,
        });
        McpServerTool tool = new DelegatingTool(inner);
        if (overrideSchema)
        {
            tool = new SchemaOverrideTool(tool, objectSchema);
        }

        JsonElement originalSchema = tool.ProtocolTool.OutputSchema!.Value;
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;

            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(originalSchema.GetRawText(), schema.GetRawText());
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
                Assert.False(schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty("result", out _));
            }

            Assert.Equal(response.StructuredContent!.Value.GetRawText(), content.GetRawText());
            Assert.Equal("explicit response", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.False(result.IsError);
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText());
            Assert.True(advertisedSchema.Evaluate(content).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
        });

        Assert.Equal(originalSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(composedSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("""{"name":"test"}""", response.StructuredContent!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DelegatingTool_CustomResultMarshallingMatchesStructuredContent(string protocolVersion)
    {
        JsonElement objectSchema = ParseJson("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");
        AIFunction function = AIFunctionFactory.Create(() => new CallToolResult(), new()
        {
            SerializerOptions = McpJsonUtilities.DefaultOptions,
            MarshalResult = static (_, _, _) => new ValueTask<object?>(ParseJson("""{"name":"test"}""")),
        });
        McpServerTool inner = McpServerTool.Create(function, new()
        {
            Name = "marshalled_result",
            UseStructuredContent = true,
            OutputSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}"""),
        });
        await WithClientAsync(new SchemaOverrideTool(inner, objectSchema), protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement content = result.StructuredContent!.Value;
            if (protocolVersion != "2026-07-28")
            {
                content = content.GetProperty("result");
            }

            Assert.Equal("test", content.GetProperty("name").GetString());
            Assert.True(JsonSchema.FromText(listed.ProtocolTool.OutputSchema!.Value.GetRawText()).Evaluate(result.StructuredContent.Value).IsValid);
        });
    }

    [Theory]
    [InlineData("2025-06-18", "supplied")]
    [InlineData("2025-06-18", "methodless")]
    [InlineData("2025-06-18", "sync")]
    [InlineData("2025-06-18", "task")]
    [InlineData("2025-06-18", "valueTask")]
    [InlineData("2025-06-18", "baseResult")]
    [InlineData("2025-11-25", "supplied")]
    [InlineData("2025-11-25", "methodless")]
    [InlineData("2025-11-25", "sync")]
    [InlineData("2025-11-25", "task")]
    [InlineData("2025-11-25", "valueTask")]
    [InlineData("2025-11-25", "baseResult")]
    [InlineData("2026-07-28", "supplied")]
    [InlineData("2026-07-28", "methodless")]
    [InlineData("2026-07-28", "sync")]
    [InlineData("2026-07-28", "task")]
    [InlineData("2026-07-28", "valueTask")]
    [InlineData("2026-07-28", "baseResult")]
    public async Task ListTools_UnknownMarshalling_ValidatesBothRuntimePaths(string protocolVersion, string creationKind)
    {
        JsonElement objectSchema = ParseJson(creationKind == "baseResult"
            ? """{"type":"object","properties":{"name":{"type":"string","minLength":1}}}"""
            : """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        CallToolResult response = new() { StructuredContent = ParseJson("""{"name":"test"}""") };
        object GetValue(bool returnExplicit) => returnExplicit ? response : ParseJson("""{"name":"test"}""");
        Delegate callback = creationKind switch
        {
            "supplied" or "methodless" or "sync" => new Func<bool, object>(returnExplicit => GetValue(returnExplicit)),
            "task" => new Func<bool, Task<object>>(returnExplicit => Task.FromResult(GetValue(returnExplicit))),
            "valueTask" => new Func<bool, ValueTask<object>>(returnExplicit => new(GetValue(returnExplicit))),
            "baseResult" => new Func<bool, Result>(returnExplicit => returnExplicit ? response : new ListToolsResult()),
            _ => throw new ArgumentOutOfRangeException(nameof(creationKind)),
        };
        McpServerToolCreateOptions options = new()
        {
            Name = "unknown_result",
            UseStructuredContent = true,
            OutputSchema = composedSchema,
            SerializerOptions = new(McpJsonUtilities.DefaultOptions)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(McpJsonUtilities.DefaultOptions.TypeInfoResolver, ResultJsonContext.Default),
            },
        };
        McpServerTool inner;
        if (creationKind is "supplied" or "methodless")
        {
            AIFunction function = CreateIdentityFunction(callback);
            if (creationKind == "methodless")
            {
                function = new MethodlessFunction(function);
            }

            inner = McpServerTool.Create(function, options);
        }
        else
        {
            inner = McpServerTool.Create(callback, options);
        }

        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), objectSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText(), new BuildOptions { Dialect = Dialect.Draft202012 });
            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(objectSchema.GetRawText(), schema.GetRawText());
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
            }

            foreach (bool returnExplicit in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnExplicit"] = returnExplicit,
                }, cancellationToken: TestContext.Current.CancellationToken);
                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (!returnExplicit && protocolVersion != "2026-07-28")
                {
                    content = content.GetProperty("result");
                }

                if (!returnExplicit && creationKind == "baseResult")
                {
                    Assert.Empty(content.GetProperty("tools").EnumerateArray());
                }
                else
                {
                    Assert.Equal("test", content.GetProperty("name").GetString());
                }
            }

            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
        });

        Assert.Equal(objectSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(composedSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("""{"name":"test"}""", response.StructuredContent!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18", "pointer")]
    [InlineData("2025-06-18", "self")]
    [InlineData("2025-06-18", "emptySelf")]
    [InlineData("2025-06-18", "encodedPointer")]
    [InlineData("2025-06-18", "resource")]
    [InlineData("2025-06-18", "nestedResource")]
    [InlineData("2025-06-18", "anchor")]
    [InlineData("2025-06-18", "dynamic")]
    [InlineData("2025-06-18", "dynamicPointer")]
    [InlineData("2025-06-18", "literal")]
    [InlineData("2025-06-18", "boolean")]
    [InlineData("2025-11-25", "pointer")]
    [InlineData("2025-11-25", "self")]
    [InlineData("2025-11-25", "emptySelf")]
    [InlineData("2025-11-25", "encodedPointer")]
    [InlineData("2025-11-25", "resource")]
    [InlineData("2025-11-25", "nestedResource")]
    [InlineData("2025-11-25", "anchor")]
    [InlineData("2025-11-25", "dynamic")]
    [InlineData("2025-11-25", "dynamicPointer")]
    [InlineData("2025-11-25", "literal")]
    [InlineData("2025-11-25", "boolean")]
    [InlineData("2026-07-28", "pointer")]
    [InlineData("2026-07-28", "self")]
    [InlineData("2026-07-28", "emptySelf")]
    [InlineData("2026-07-28", "encodedPointer")]
    [InlineData("2026-07-28", "resource")]
    [InlineData("2026-07-28", "nestedResource")]
    [InlineData("2026-07-28", "anchor")]
    [InlineData("2026-07-28", "dynamic")]
    [InlineData("2026-07-28", "dynamicPointer")]
    [InlineData("2026-07-28", "literal")]
    [InlineData("2026-07-28", "boolean")]
    public async Task ListTools_UnknownMarshalling_PreservesSchemaMeaning(string protocolVersion, string schemaKind)
    {
        (string schemaJson, string contentJson) = schemaKind switch
        {
            "pointer" => (
                """{"allOf":[{"$ref":"#/$defs/node"}],"$defs":{"node":{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}}}""",
                """{"name":"test"}"""),
            "self" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$ref":"#"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "emptySelf" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$ref":""}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "encodedPointer" => (
                """{"allOf":[{"$ref":"#%2F$defs%2Fnode"}],"$defs":{"node":{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}}}""",
                """{"name":"test"}"""),
            "resource" => (
                """{"$id":"urn:test:output","allOf":[{"$ref":"#/$defs/node"}],"$defs":{"node":{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}}}""",
                """{"name":"test"}"""),
            "nestedResource" => (
                """{"allOf":[{"$ref":"#/$defs/node"}],"$defs":{"node":{"$id":"urn:test:node","type":"object","properties":{"name":{"type":"string","minLength":1},"child":{"$ref":"#"}},"required":["name"]}}}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "anchor" => (
                """{"$anchor":"node","allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$ref":"#node"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "dynamic" => (
                """{"$dynamicAnchor":"node","allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$dynamicRef":"#node"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "dynamicPointer" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$dynamicRef":"#"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "literal" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"literal":{"const":{"$ref":"#"}},"$ref":{"const":"literal"}},"required":["name","literal","$ref"]}""",
                """{"name":"test","literal":{"$ref":"#"},"$ref":"literal"}"""),
            "boolean" => ("true", """{"name":"test"}"""),
            _ => throw new ArgumentOutOfRangeException(nameof(schemaKind)),
        };
        schemaJson = schemaJson.Replace("urn:test:", $"urn:test:{protocolVersion}:");
        JsonElement naturalSchema = ParseJson(schemaJson);
        JsonElement expectedContent = ParseJson(contentJson);
        CallToolResult response = new() { StructuredContent = expectedContent };
        AIFunction function = CreateIdentityFunction(
            (bool returnExplicit) => returnExplicit ? (object)response : expectedContent);
        McpServerTool tool = new DelegatingTool(McpServerTool.Create(function, new()
        {
            Name = "schema_meaning",
            UseStructuredContent = true,
            OutputSchema = naturalSchema,
        }));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText(), new BuildOptions { Dialect = Dialect.Draft202012 });
            foreach (bool returnExplicit in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnExplicit"] = returnExplicit,
                }, cancellationToken: TestContext.Current.CancellationToken);
                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (!returnExplicit && protocolVersion != "2026-07-28")
                {
                    content = content.GetProperty("result");
                }

                Assert.Equal(expectedContent.GetRawText(), content.GetRawText());
            }

            if (schemaKind != "boolean")
            {
                string invalidContent = contentJson.Replace("\"test\"", "\"\"");
                Assert.False(advertisedSchema.Evaluate(ParseJson(invalidContent)).IsValid);
                if (protocolVersion != "2026-07-28")
                {
                    Assert.False(advertisedSchema.Evaluate(ParseJson($$"""{"result":{{invalidContent}}}""")).IsValid);
                }
            }
        });

        Assert.Equal(naturalSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Invoke_ExplicitResult_DoesNotRequireFunctionSerializationMetadata(string protocolVersion)
    {
        CallToolResult response = new()
        {
            Content = [new TextContentBlock { Text = "explicit response" }],
            StructuredContent = ParseJson("""{"name":"test"}"""),
            IsError = true,
        };
        AIFunction function = AIFunctionFactory.Create(() => (object)response, new()
        {
            SerializerOptions = new JsonSerializerOptions { TypeInfoResolver = ObjectJsonContext.Default },
            MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
        });
        McpServerTool tool = new DelegatingTool(McpServerTool.Create(function, new()
        {
            Name = "explicit_without_metadata",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"allOf":[{"type":"object"}]}"""),
        }));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(response.StructuredContent!.Value.GetRawText(), result.StructuredContent!.Value.GetRawText());
            Assert.Equal("explicit response", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.True(result.IsError);
        });
    }

    [Theory]
    [MemberData(nameof(NullableExplicitResultCases))]
    public async Task ListTools_NullableExplicitResult_MatchesBothRuntimePaths(string protocolVersion, string returnKind)
    {
        JsonElement naturalSchema = ParseJson(
            """{"anyOf":[{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]},{"type":"null"}]}""");
        CallToolResult response = new() { StructuredContent = ParseJson("""{"name":"test"}""") };
        Delegate callback = returnKind switch
        {
            "sync" or "method" or "factory" or "supplied" =>
                new Func<bool, CallToolResult?>(returnNull => returnNull ? null : response),
            "task" => new Func<bool, Task<CallToolResult?>>(returnNull => Task.FromResult(returnNull ? null : response)),
            "valueTask" => new Func<bool, ValueTask<CallToolResult?>>(returnNull => new(returnNull ? null : response)),
            "broad" => new Func<bool, object?>(returnNull => returnNull ? null : response),
            "ordinary" => new Func<bool, JsonElement?>(returnNull => returnNull ? null : response.StructuredContent),
            _ => throw new ArgumentOutOfRangeException(nameof(returnKind)),
        };
        McpServerToolCreateOptions options = new()
        {
            Name = "nullable_result",
            UseStructuredContent = true,
            OutputSchema = naturalSchema,
        };
        McpServerTool inner = returnKind switch
        {
            "method" => McpServerTool.Create(callback.Method, callback.Target, options),
            "factory" => McpServerTool.Create(callback.Method, _ => callback.Target!, options),
            "supplied" => McpServerTool.Create(CreateIdentityFunction(callback), options),
            _ => McpServerTool.Create(callback, options),
        };
        await WithClientAsync(new DelegatingTool(inner), protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = ParseSchema(schema);
            bool legacy = protocolVersion != "2026-07-28";
            if (!legacy)
            {
                Assert.Equal(naturalSchema.GetRawText(), schema.GetRawText());
            }

            foreach (bool returnNull in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnNull"] = returnNull,
                }, cancellationToken: TestContext.Current.CancellationToken);
                if (returnNull && !legacy)
                {
                    Assert.Null(result.StructuredContent);
                    Assert.Empty(result.Content);
                    continue;
                }

                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (returnNull || (legacy && returnKind == "ordinary"))
                {
                    content = content.GetProperty("result");
                }

                if (returnNull)
                {
                    Assert.Equal(JsonValueKind.Null, content.ValueKind);
                    Assert.Empty(result.Content);
                }
                else
                {
                    Assert.Equal("test", content.GetProperty("name").GetString());
                }
            }

            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
            if (returnKind is not ("ordinary" or "supplied" or "broad"))
            {
                Assert.False(advertisedSchema.Evaluate(ParseJson("""{"result":{"name":"test"}}""")).IsValid);
            }
        });

        Assert.Equal(naturalSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("""{"name":"test"}""", response.StructuredContent!.Value.GetRawText());
    }

    public static IEnumerable<object[]> NullableExplicitResultCases()
    {
        foreach (string protocolVersion in new[] { "2025-06-18", "2025-11-25", "2026-07-28" })
        {
            foreach (string returnKind in new[] { "sync", "task", "valueTask", "method", "factory", "supplied", "broad", "ordinary" })
            {
                yield return [protocolVersion, returnKind];
            }
        }
    }

    [Theory]
    [MemberData(nameof(NullableRecursiveSchemaCases))]
    public async Task ListTools_NullableRecursiveSchema_PreservesDescendantConstraints(
        string protocolVersion, string returnKind, string schemaKind)
    {
        string refKeyword = schemaKind == "dynamic" ? "$dynamicRef" : "$ref";
        string reference = schemaKind is "anchor" or "dynamic" ? "#node" : "#";
        string identity = schemaKind switch
        {
            "resource" => $"\"$id\":\"urn:recursive:{protocolVersion}:{returnKind}\",",
            "anchor" => "\"$anchor\":\"node\",",
            "dynamic" => "\"$dynamicAnchor\":\"node\",",
            _ => "",
        };
        string objectMembers = $$$$"""
            "properties":{"name":{"type":"string","minLength":1},"child":{"{{{{refKeyword}}}}":"{{{{reference}}}}"},"children":{"type":"array","items":{"{{{{refKeyword}}}}":"{{{{reference}}}}"}}},"required":["name"],"additionalProperties":false
            """;
        string schemaJson = schemaKind == "nullableArray"
            ? $$"""{"type":["object","null"],{{objectMembers}}}"""
            : $$"""{"anyOf":[{"type":"null"},{"type":"object",{{objectMembers}}}]}""";
        JsonElement naturalSchema = ParseJson(schemaJson.Insert(1, identity));
        JsonElement expectedContent = ParseJson(
            """{"name":"root","child":{"name":"child","child":null},"children":[null,{"name":"leaf","child":null}]}""");
        Assert.True(ParseSchema(naturalSchema).Evaluate(expectedContent).IsValid);
        CallToolResult response = new() { StructuredContent = expectedContent };
        McpServerToolCreateOptions options = new()
        {
            Name = "recursive_result",
            UseStructuredContent = true,
            OutputSchema = schemaKind == "wrapper" ? ParseJson("""{"type":"object"}""") : naturalSchema,
        };
        McpServerTool inner = returnKind switch
        {
            "explicit" => McpServerTool.Create(() => response, options),
            "supplied" => McpServerTool.Create(CreateIdentityFunction(() => response), options),
            "ordinary" => McpServerTool.Create(() => expectedContent, options),
            _ => throw new ArgumentOutOfRangeException(nameof(returnKind)),
        };
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), naturalSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;
            JsonSchema advertisedSchema = ParseSchema(schema);
            Assert.True(advertisedSchema.Evaluate(content).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson(content.GetRawText().Replace("\"child\":null", "\"child\":42"))).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson(content.GetRawText().Replace("\"root\"", "\"\""))).IsValid);
            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(naturalSchema.GetRawText(), schema.GetRawText());
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
                Assert.False(advertisedSchema.Evaluate(ParseJson("null")).IsValid);
                if (returnKind == "ordinary" && schemaKind is not ("nullableArray" or "wrapper"))
                {
                    content = content.GetProperty("result");
                }
            }

            Assert.Equal(expectedContent.GetRawText(), content.GetRawText());
        });

        Assert.Equal(naturalSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(options.OutputSchema!.Value.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(expectedContent.GetRawText(), response.StructuredContent!.Value.GetRawText());
    }

    public static IEnumerable<object[]> NullableRecursiveSchemaCases()
    {
        foreach (string protocolVersion in new[] { "2025-06-18", "2025-11-25", "2026-07-28" })
        {
            foreach (string returnKind in new[] { "explicit", "supplied", "ordinary" })
            {
                foreach (string schemaKind in new[] { "composed", "nullableArray", "wrapper", "resource", "anchor", "dynamic" })
                {
                    yield return [protocolVersion, returnKind, schemaKind];
                }
            }
        }
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_CustomDelegatingInvocation_MatchesBothRuntimePaths(string protocolVersion)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        CallToolResult response = new() { StructuredContent = ParseJson("""{"name":"test"}""") };
        McpServerTool inner = McpServerTool.Create((bool returnExplicit) => ParseJson("""{"name":"test"}"""), new()
        {
            Name = "custom_invocation",
            UseStructuredContent = true,
            OutputSchema = composedSchema,
        });
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(new AlternateInvocationTool(inner, response)), objectSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = ParseSchema(schema);
            foreach (bool returnExplicit in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnExplicit"] = returnExplicit,
                }, cancellationToken: TestContext.Current.CancellationToken);
                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (!returnExplicit && protocolVersion != "2026-07-28")
                {
                    content = content.GetProperty("result");
                }

                Assert.Equal("test", content.GetProperty("name").GetString());
            }

            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"result":{"name":""}}""")).IsValid);
            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(objectSchema.GetRawText(), schema.GetRawText());
            }
        });

        Assert.Equal(objectSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(composedSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
    }

    [JsonSerializable(typeof(Result))]
    [JsonSerializable(typeof(object))]
    private sealed partial class ResultJsonContext : JsonSerializerContext;

    [JsonSerializable(typeof(object))]
    private sealed partial class ObjectJsonContext : JsonSerializerContext;

    private static AIFunction CreateIdentityFunction(Delegate callback) =>
        AIFunctionFactory.Create(callback, new()
        {
            SerializerOptions = McpJsonUtilities.DefaultOptions,
            MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
        });

    private static JsonSchema ParseSchema(JsonElement schema) =>
        JsonSchema.FromText(schema.GetRawText(), new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new(),
        });

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task WithClientAsync(
        McpServerTool tool, string protocolVersion, Func<McpClient, Task> action, bool alternateHandler = false,
        Action<McpServerOptions>? configure = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TestConstants.DefaultTimeout);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        McpServerOptions options = new() { ToolCollection = new() { tool } };
        if (alternateHandler)
        {
#pragma warning disable MCPEXP002 // Exercise registered tools through the explicit alternate handler pipeline.
            options.Handlers.CallToolWithAlternateHandler =
                static (_, _) => throw new InvalidOperationException("Registered tool was not matched.");
#pragma warning restore MCPEXP002
        }
        configure?.Invoke(options);
        await using var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            options);
        Task serverTask = server.RunAsync(cts.Token);
        try
        {
            var clientOptions = TestMcpClient.CreateOptions();
            clientOptions.ProtocolVersion = protocolVersion;
            await using var client = await TestMcpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
                clientOptions, cancellationToken: cts.Token);
            await action(client);
        }
        finally
        {
            await cts.CancelAsync();
            await serverTask;
        }
    }

    private sealed class DelegatingTool(McpServerTool inner) : DelegatingMcpServerTool(inner);

    private sealed class MutableNote
    {
        public string? Note { get; set; }
    }

    private sealed class UnsafeDefaultConverterOutput
    {
        [JsonConverter(typeof(DisclosingIntConverter))]
        public int Value { get; set; }
    }

    private sealed class DisclosingIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteStringValue("not-for-output");
    }

    private sealed class UnsafeDefaultEnumOutput
    {
        public DisclosingEnum Value { get; set; }
    }

    private enum DisclosingEnum
    {
        None,
    }

    private sealed class DisclosingEnumNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => "not-for-output";
    }

    private sealed class UnsafeDefaultStructOutput
    {
        public UnsafeDefaultStruct Child { get; set; }
    }

    private readonly struct UnsafeDefaultStruct
    {
        public string Secret => "not-for-output";
    }

    private sealed class SafeDefaultScalarOutput
    {
        public int Count { get; set; }
    }

    private sealed class LargeNote(string payload)
    {
        public string Payload { get; } = payload;
        public string? Note { get; set; }
    }

    private sealed class WhitespaceNoteConverter(int whitespaceLength) : JsonConverter<LargeNote>
    {
        private readonly string _json = "{\"payload\":" + new string(' ', whitespaceLength) + "\"value\"}";

        public override LargeNote Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, LargeNote value, JsonSerializerOptions options) =>
            writer.WriteRawValue(_json, skipInputValidation: true);
    }

    private sealed class MutableCount
    {
        public int Count { get; set; }
    }

    private sealed class DisclosingEnumOutput
    {
        public DisclosingEnumValue Value { get; set; }
    }

    private enum DisclosingEnumValue
    {
        None,
    }

    private sealed class DisclosingStructOutput
    {
        public DisclosingStructValue Child { get; set; }
    }

    private readonly struct DisclosingStructValue
    {
        public string Secret => "not-for-output";
    }

    private sealed class ExpiringOutput
    {
        [JsonIgnore]
        public bool Expired { get; set; }

        public int Count => Expired ? throw new InvalidOperationException("Source is no longer owned by the inner invocation.") : 0;
    }

    private sealed class ExpiringIntConverter : JsonConverter<int>
    {
        public bool Expired { get; set; }
        public int WriteCalls { get; private set; }

        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            WriteCalls++;
            if (Expired)
            {
                throw new InvalidOperationException("Converter is no longer owned by the inner invocation.");
            }
            writer.WriteStringValue(value == 0 ? "zero" : "changed");
        }
    }

    private sealed class OwnedResultTool(McpServerTool inner, int mode) : DelegatingMcpServerTool(inner)
    {
        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult result = await base.InvokeAsync(request, cancellationToken);
            if (mode == 0)
            {
                result.IsError = true;
            }
            else
            {
                result.StructuredContent = ParseJson(mode == 1 ? """{"count":99}""" : result.StructuredContent!.Value.GetRawText());
            }
            return result;
        }
    }

    private sealed class RequestStateTool(McpServerTool inner, McpServerTool? temporaryMatch, Action? afterInvocation)
        : DelegatingMcpServerTool(inner)
    {
        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            IMcpServerPrimitive? original = request.MatchedPrimitive;
            if (temporaryMatch is not null)
            {
                request.MatchedPrimitive = temporaryMatch;
            }
            try
            {
                CallToolResult result = await base.InvokeAsync(request, cancellationToken);
                afterInvocation?.Invoke();
                return result;
            }
            finally
            {
                request.MatchedPrimitive = original;
            }
        }
    }

    private sealed class ForwardingInvocationTool(McpServerTool inner, int mode) : DelegatingMcpServerTool(inner)
    {
        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
            mode == 1 ? base.InvokeAsync(request, cancellationToken) : ForwardAsync(request, cancellationToken);

        private async ValueTask<CallToolResult> ForwardAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            CallToolResult result = await base.InvokeAsync(request, cancellationToken);
            if (mode == 5)
            {
                await base.InvokeAsync(request, cancellationToken);
                return result;
            }
            return mode == 2 ? result : new()
            {
                Content = result.Content,
                StructuredContent = result.StructuredContent,
                IsError = result.IsError,
                Meta = result.Meta,
            };
        }
    }

    private sealed class MutatingInvocationTool(McpServerTool inner, Action mutate) : DelegatingMcpServerTool(inner)
    {
        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            mutate();
            return base.InvokeAsync(request, cancellationToken);
        }
    }

    private sealed class FireAndForgetInvocationTool(
        McpServerTool inner, TaskCompletionSource<bool> entered) : DelegatingMcpServerTool(inner)
    {
        public Task<CallToolResult>? Pending { get; private set; }

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            Pending = base.InvokeAsync(request, cancellationToken).AsTask();
            await entered.Task.WaitAsync(cancellationToken);
            return new CallToolResult { Content = [] };
        }
    }

    private sealed class ConcurrentInvocationTool(McpServerTool inner, bool returnSecond, Action afterInvocation)
        : DelegatingMcpServerTool(inner)
    {
        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            Task<CallToolResult> first = base.InvokeAsync(request, cancellationToken).AsTask();
            Task<CallToolResult> second = base.InvokeAsync(request, cancellationToken).AsTask();
            CallToolResult[] results = await Task.WhenAll(first, second);
            afterInvocation();
            return results[returnSecond ? 1 : 0];
        }
    }

    private sealed class GatedInvocationTool(
        McpServerTool inner, TaskCompletionSource<bool> entered, TaskCompletionSource<bool> release,
        bool suspendAfterInner, bool secondThrows) : DelegatingMcpServerTool(inner)
    {
        private int _calls;

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            bool first = Interlocked.Increment(ref _calls) == 1;
            if (!first && secondThrows)
            {
                throw new InvalidOperationException("Second dispatch failed.");
            }
            if (first && !suspendAfterInner)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }
            CallToolResult result = await base.InvokeAsync(request, cancellationToken);
            if (first && suspendAfterInner)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObservedRecoveryTool(McpServerTool inner) : DelegatingMcpServerTool(inner)
    {
        public int EarlierPlanVisits { get; private set; }

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult first = await base.InvokeAsync(request, cancellationToken);
            object target = typeof(RequestContext<CallToolRequestParams>)
                .GetProperty("StructuredContentRecoveryTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(request)!;
            var plans = (Dictionary<JsonElement, Action<McpServerTool, CallToolResult>>)target.GetType()
                .GetField("_plans", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
            var original = plans[first.StructuredContent!.Value];
            JsonElement[] identities = plans.Where(entry => ReferenceEquals(entry.Value, original)).Select(entry => entry.Key).ToArray();
            Action<McpServerTool, CallToolResult> observed = (tool, result) =>
            {
                EarlierPlanVisits++;
                original(tool, result);
            };
            target.GetType().GetMethod("Register", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(target, [identities, observed, null, 0]);
            for (int i = 1; i < 32; i++)
            {
                await base.InvokeAsync(request, cancellationToken);
            }
            return first;
        }
    }

    private sealed class BudgetObservingTool(McpServerTool inner, int invocationCount, bool returnFirst) : DelegatingMcpServerTool(inner)
    {
        public int PeakPlanCount { get; private set; }
        public int PeakRetainedBytes { get; private set; }

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult? selected = null;
            for (int i = 0; i < invocationCount; i++)
            {
                CallToolResult result = await base.InvokeAsync(request, cancellationToken);
                if (!returnFirst || selected is null)
                {
                    selected = result;
                }

                object target = typeof(RequestContext<CallToolRequestParams>)
                    .GetProperty("StructuredContentRecoveryTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(request)!;
                var plans = (Dictionary<JsonElement, Action<McpServerTool, CallToolResult>>)target.GetType()
                    .GetField("_plans", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
                PeakPlanCount = Math.Max(PeakPlanCount, plans.Values.Distinct().Count());
                int retainedBytes = (int)target.GetType()
                    .GetField("_retainedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
                PeakRetainedBytes = Math.Max(PeakRetainedBytes, retainedBytes);
            }

            return selected!;
        }
    }

    private sealed class NoteTransformingTool(McpServerTool inner) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}"""),
        };

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult result = await base.InvokeAsync(request, cancellationToken);
            Assert.Equal(JsonValueKind.Null, result.StructuredContent!.Value.GetProperty("note").ValueKind);
            result.StructuredContent = ParseJson("""{"note":"transformed"}""");
            return result;
        }
    }

    private sealed class PayloadTool(McpServerTool inner) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = ParseJson($$$$"""{"type":"object","required":["payload"],"properties":{"payload":{{{{inner.ProtocolTool.OutputSchema!.Value.GetRawText()}}}}}}"""),
        };

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult result = await base.InvokeAsync(request, cancellationToken);
            result.StructuredContent = ParseJson($$"""{"payload":{{result.StructuredContent!.Value.GetRawText()}}}""");
            return result;
        }
    }

    private sealed class AlternateInvocationTool(McpServerTool inner, CallToolResult response) : DelegatingMcpServerTool(inner)
    {
        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            if (request.Params?.Arguments?.TryGetValue("returnExplicit", out JsonElement returnExplicit) is true && returnExplicit.GetBoolean())
            {
                return new(response);
            }

            return base.InvokeAsync(request, cancellationToken);
        }
    }

    private sealed class MethodlessFunction(AIFunction inner) : DelegatingAIFunction(inner)
    {
        public override MethodInfo? UnderlyingMethod => null;
    }

    private sealed class SchemaOverrideTool(McpServerTool inner, JsonElement schema) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = schema,
        };
    }

    private sealed class DescribedTool(McpServerTool inner) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            Description = "Wrapped count",
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = ParseJson("""{"type":"integer","minimum":0}"""),
        };
    }

    private sealed class CustomTool : McpServerTool
    {
        public override IReadOnlyList<object> Metadata => [];

        public override Tool ProtocolTool { get; } = new()
        {
            Name = "custom",
            InputSchema = ParseJson("""{"type":"object"}"""),
            OutputSchema = ParseJson("""{"type":"integer"}"""),
        };

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
            new(new CallToolResult { StructuredContent = ParseJson("72") });
    }
}
