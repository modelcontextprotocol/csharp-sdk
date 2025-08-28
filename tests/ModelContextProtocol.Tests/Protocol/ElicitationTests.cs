using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Configuration;

public partial class ElicitationTests : ClientServerTestBase
{
    public ElicitationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithCallToolHandler(async (request, cancellationToken) =>
        {
            Assert.Equal("TestElicitation", request.Params?.Name);

            var result = await request.Server.ElicitAsync(
                new()
                {
                    Message = "Please provide more information.",
                    RequestedSchema = new()
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>()
                        {
                            ["prop1"] = new ElicitRequestParams.StringSchema
                            {
                                Title = "title1",
                                MinLength = 1,
                                MaxLength = 100,
                            },
                            ["prop2"] = new ElicitRequestParams.NumberSchema
                            {
                                Description = "description2",
                                Minimum = 0,
                                Maximum = 1000,
                            },
                            ["prop3"] = new ElicitRequestParams.BooleanSchema
                            {
                                Title = "title3",
                                Description = "description4",
                                Default = true,
                            },
                            ["prop4"] = new ElicitRequestParams.EnumSchema
                            {
                                Enum = ["option1", "option2", "option3"],
                                EnumNames = ["Name1", "Name2", "Name3"],
                            },
                        },
                    },
                },
                CancellationToken.None);

            Assert.Equal("accept", result.Action);

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = "success" }],
            };
        });
    }

    [Fact]
    public async Task Can_Elicit_Information()
    {
        await using McpClient client = await CreateMcpClientForServer(new McpClientOptions
        {
            Capabilities = new()
            {
                Elicitation = new()
                {
                    ElicitationHandler = async (request, cancellationtoken) =>
                    {
                        Assert.NotNull(request);
                        Assert.Equal("Please provide more information.", request.Message);
                        Assert.Equal(4, request.RequestedSchema.Properties.Count);

                        foreach (var entry in request.RequestedSchema.Properties)
                        {
                            switch (entry.Key)
                            {
                                case "prop1":
                                    var primitiveString = Assert.IsType<ElicitRequestParams.StringSchema>(entry.Value);
                                    Assert.Equal("title1", primitiveString.Title);
                                    Assert.Equal(1, primitiveString.MinLength);
                                    Assert.Equal(100, primitiveString.MaxLength);
                                    break;

                                case "prop2":
                                    var primitiveNumber = Assert.IsType<ElicitRequestParams.NumberSchema>(entry.Value);
                                    Assert.Equal("description2", primitiveNumber.Description);
                                    Assert.Equal(0, primitiveNumber.Minimum);
                                    Assert.Equal(1000, primitiveNumber.Maximum);
                                    break;

                                case "prop3":
                                    var primitiveBool = Assert.IsType<ElicitRequestParams.BooleanSchema>(entry.Value);
                                    Assert.Equal("title3", primitiveBool.Title);
                                    Assert.Equal("description4", primitiveBool.Description);
                                    Assert.True(primitiveBool.Default);
                                    break;

                                case "prop4":
                                    var primitiveEnum = Assert.IsType<ElicitRequestParams.EnumSchema>(entry.Value);
                                    Assert.Equal(["option1", "option2", "option3"], primitiveEnum.Enum);
                                    Assert.Equal(["Name1", "Name2", "Name3"], primitiveEnum.EnumNames);
                                    break;

                                default:
                                    Assert.Fail($"Unknown property: {entry.Key}");
                                    break;
                            }
                        }

                        return new ElicitResult
                        {
                            Action = "accept",
                            Content = new Dictionary<string, JsonElement>
                            {
                                ["prop1"] = (JsonElement)JsonSerializer.Deserialize("""
                                    "string result"
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                ["prop2"] = (JsonElement)JsonSerializer.Deserialize("""
                                    42
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                ["prop3"] = (JsonElement)JsonSerializer.Deserialize("""
                                    true
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                                ["prop4"] = (JsonElement)JsonSerializer.Deserialize("""
                                    "option2"
                                    """, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonElement)))!,
                            },
                        };
                    },
                },
            },
        });

        var result = await client.CallToolAsync("TestElicitation", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("success", (result.Content[0] as TextContentBlock)?.Text);
    }
}