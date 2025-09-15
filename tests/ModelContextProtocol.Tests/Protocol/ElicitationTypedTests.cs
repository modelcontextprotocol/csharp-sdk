using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Configuration;

public partial class ElicitationTypedTests : ClientServerTestBase
{
    public ElicitationTypedTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithCallToolHandler(async (request, cancellationToken) =>
        {
            Assert.NotNull(request.Params);

            if (request.Params!.Name == "TestElicitationTyped")
            {
                var result = await request.Server.ElicitAsync<SampleForm>(
                    message: "Please provide more information.",
                    serializerOptions: ElicitationTypedDefaultJsonContext.Default.Options,
                    cancellationToken: CancellationToken.None);

                Assert.Equal("accept", result.Action);
                Assert.NotNull(result.Content);
                Assert.Equal("Alice", result.Content!.Name);
                Assert.Equal(30, result.Content!.Age);
                Assert.True(result.Content!.Active);
                Assert.Equal(SampleRole.Admin, result.Content!.Role);
                Assert.Equal(99.5, result.Content!.Score);
            }
            else if (request.Params!.Name == "TestElicitationCamelForm")
            {
                var result = await request.Server.ElicitAsync<CamelForm>(
                    message: "Please provide more information.",
                    serializerOptions: ElicitationTypedCamelJsonContext.Default.Options,
                    cancellationToken: CancellationToken.None);

                Assert.Equal("accept", result.Action);
                Assert.NotNull(result.Content);
                Assert.Equal("Bob", result.Content!.FirstName);
                Assert.Equal(90210, result.Content!.ZipCode);
                Assert.False(result.Content!.IsAdmin);
            }
            else if (request.Params!.Name == "TestElicitationNullablePropertyForm")
            {
                var result = await request.Server.ElicitAsync<NullablePropertyForm>(
                    message: "Please provide more information.",
                    serializerOptions: ElicitationNullablePropertyJsonContext.Default.Options,
                    cancellationToken: CancellationToken.None);

                // Should be unreachable
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "unexpected" }],
                };
            }
            else if (request.Params!.Name == "TestElicitationUnsupportedType")
            {
                await request.Server.ElicitAsync<UnsupportedForm>(
                    message: "Please provide more information.",
                    serializerOptions: ElicitationUnsupportedJsonContext.Default.Options,
                    cancellationToken: CancellationToken.None);

                // Should be unreachable
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "unexpected" }],
                };
            }
            else if (request.Params!.Name == "TestElicitationNonObjectGenericType")
            {
                // This should throw because T is not an object type with properties (string primitive)
                await request.Server.ElicitAsync<string>(
                    message: "Any message",
                    serializerOptions: McpJsonUtilities.DefaultOptions,
                    cancellationToken: CancellationToken.None);

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "unexpected" }],
                };
            }
            else
            {
                Assert.Fail($"Unexpected tool name: {request.Params!.Name}");
            }

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = "success" }],
            };
        });
    }

    [Fact]
    public async Task Can_Elicit_Typed_Information()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new()
            {
                Elicitation = new()
                {
                    ElicitationHandler = async (request, cancellationToken) =>
                    {
                        Assert.NotNull(request);
                        Assert.Equal("Please provide more information.", request.Message);

                        Assert.Equal(6, request.RequestedSchema.Properties.Count);

                        foreach (var entry in request.RequestedSchema.Properties)
                        {
                            var key = entry.Key;
                            var value = entry.Value;
                            switch (key)
                            {
                                case nameof(SampleForm.Name):
                                    var stringSchema = Assert.IsType<ElicitRequestParams.StringSchema>(value);
                                    Assert.Equal("string", stringSchema.Type);
                                    break;

                                case nameof(SampleForm.Age):
                                    var intSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(value);
                                    Assert.Equal("integer", intSchema.Type);
                                    break;

                                case nameof(SampleForm.Active):
                                    var boolSchema = Assert.IsType<ElicitRequestParams.BooleanSchema>(value);
                                    Assert.Equal("boolean", boolSchema.Type);
                                    break;

                                case nameof(SampleForm.Role):
                                    var enumSchema = Assert.IsType<ElicitRequestParams.EnumSchema>(value);
                                    Assert.Equal("string", enumSchema.Type);
                                    Assert.Equal([nameof(SampleRole.User), nameof(SampleRole.Admin)], enumSchema.Enum);
                                    break;

                                case nameof(SampleForm.Score):
                                    var numSchema = Assert.IsType<ElicitRequestParams.NumberSchema>(value);
                                    Assert.Equal("number", numSchema.Type);
                                    break;

                                case nameof(SampleForm.Created):
                                    var dateTimeSchema = Assert.IsType<ElicitRequestParams.StringSchema>(value);
                                    Assert.Equal("string", dateTimeSchema.Type);
                                    Assert.Equal("date-time", dateTimeSchema.Format);

                                    break;

                                default:
                                    Assert.Fail($"Unexpected property in schema: {key}");
                                    break;
                            }
                        }

                        return new ElicitResult
                        {
                            Action = "accept",
                            Content = new Dictionary<string, JsonElement>
                            {
                                [nameof(SampleForm.Name)] = (JsonElement)JsonSerializer.Deserialize("""
                                    "Alice"
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                [nameof(SampleForm.Age)] = (JsonElement)JsonSerializer.Deserialize("""
                                    30
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                [nameof(SampleForm.Active)] = (JsonElement)JsonSerializer.Deserialize("""
                                    true
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                [nameof(SampleForm.Role)] = (JsonElement)JsonSerializer.Deserialize("""
                                    "Admin"
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                [nameof(SampleForm.Score)] = (JsonElement)JsonSerializer.Deserialize("""
                                    99.5
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                [nameof(SampleForm.Created)] = (JsonElement)JsonSerializer.Deserialize("""
                                    "2023-08-27T03:05:00"
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                            },
                        };
                    },
                },
            },
        });

        var result = await client.CallToolAsync("TestElicitationTyped", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("success", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task Elicit_Typed_Respects_NamingPolicy()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new()
            {
                Elicitation = new()
                {
                    ElicitationHandler = async (request, cancellationToken) =>
                    {
                        Assert.NotNull(request);
                        Assert.Equal("Please provide more information.", request.Message);

                        // Expect camelCase names based on serializer options
                        Assert.Contains("firstName", request.RequestedSchema.Properties.Keys);
                        Assert.Contains("zipCode", request.RequestedSchema.Properties.Keys);
                        Assert.Contains("isAdmin", request.RequestedSchema.Properties.Keys);

                        return new ElicitResult
                        {
                            Action = "accept",
                            Content = new Dictionary<string, JsonElement>
                            {
                                ["firstName"] = (JsonElement)JsonSerializer.Deserialize("""
                                    "Bob"
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                ["zipCode"] = (JsonElement)JsonSerializer.Deserialize("""
                                    90210
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                ["isAdmin"] = (JsonElement)JsonSerializer.Deserialize("""
                                    false
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                            },
                        };
                    },
                },
            },
        });

        var result = await client.CallToolAsync("TestElicitationCamelForm", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("success", (result.Content[0] as TextContentBlock)?.Text);
    }

    [Fact]
    public async Task Elicit_Typed_With_Unsupported_Property_Type_Throws()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new()
            {
                Elicitation = new()
                {
                    // Handler should never be invoked because the exception occurs before the request is sent.
                    ElicitationHandler = async (req, ct) =>
                    {
                        Assert.Fail("Elicitation handler should not be called for unsupported schema test.");
                        return new ElicitResult { Action = "cancel" };
                    },
                },
            },
        });

        var ex = await Assert.ThrowsAsync<McpException>(async() =>
            await client.CallToolAsync("TestElicitationUnsupportedType", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(typeof(UnsupportedForm.Nested).FullName!, ex.Message);
    }

    [Fact]
    public async Task Elicit_Typed_With_Nullable_Property_Type_Throws()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new()
            {
                Elicitation = new()
                {
                    // Handler should never be invoked because the exception occurs before the request is sent.
                    ElicitationHandler = async (req, ct) =>
                    {
                        Assert.Fail("Elicitation handler should not be called for unsupported schema test.");
                        return new ElicitResult { Action = "cancel" };
                    },
                },
            },
        });

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync("TestElicitationNullablePropertyForm", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Elicit_Typed_With_NonObject_Generic_Type_Throws()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new()
            {
                Elicitation = new()
                {
                    // Should not be invoked
                    ElicitationHandler = async (req, ct) =>
                    {
                        Assert.Fail("Elicitation handler should not be called for non-object generic type test.");
                        return new ElicitResult { Action = "cancel" };
                    },
                },
            },
        });

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync("TestElicitationNonObjectGenericType", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(typeof(string).FullName!, ex.Message);
    }

    [JsonConverter(typeof(CustomizableJsonStringEnumConverter<SampleRole>))]

    public enum SampleRole
    {
        User,
        Admin,
    }

    public sealed class SampleForm
    {
        public required string Name { get; set; }
        public int Age { get; set; }
        public bool Active { get; set; }
        public SampleRole Role { get; set; }
        public double Score { get; set; }

        
        public DateTime Created { get; set; }
    }

    public sealed class CamelForm
    {
        public required string FirstName { get; set; }
        public int ZipCode { get; set; }
        public bool IsAdmin { get; set; }
    }

    public sealed class NullablePropertyForm
    {
        public string? FirstName { get; set; }
        public int ZipCode { get; set; }
        public bool IsAdmin { get; set; }
    }

    [JsonSerializable(typeof(SampleForm))]
    [JsonSerializable(typeof(SampleRole))]
    [JsonSerializable(typeof(JsonElement))]
    internal partial class ElicitationTypedDefaultJsonContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(CamelForm))]
    [JsonSerializable(typeof(JsonElement))]
    internal partial class ElicitationTypedCamelJsonContext : JsonSerializerContext;


    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(NullablePropertyForm))]
    [JsonSerializable(typeof(JsonElement))]
    internal partial class ElicitationNullablePropertyJsonContext : JsonSerializerContext;

    public sealed class UnsupportedForm
    {
        public string? Name { get; set; }
        public Nested? NestedProperty { get; set; } // Triggers unsupported (complex object)
        public sealed class Nested
        {
            public string? Value { get; set; }
        }
    }

    [JsonSerializable(typeof(UnsupportedForm))]
    [JsonSerializable(typeof(UnsupportedForm.Nested))]
    [JsonSerializable(typeof(JsonElement))]
    internal partial class ElicitationUnsupportedJsonContext : JsonSerializerContext;
}
