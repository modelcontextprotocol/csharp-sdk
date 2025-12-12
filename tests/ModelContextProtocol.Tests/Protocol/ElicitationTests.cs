using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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
                    Mode = "form",
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
                            ["prop4"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                            {
                                OneOf =
                                [
                                    new ElicitRequestParams.EnumSchemaOption { Const = "option1", Title = "Name1" },
                                    new ElicitRequestParams.EnumSchemaOption { Const = "option2", Title = "Name2" },
                                    new ElicitRequestParams.EnumSchemaOption { Const = "option3", Title = "Name3" },
                                ]
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
            Handlers = new McpClientHandlers()
            {
                ElicitationHandler = async (request, cancellationtoken) =>
                {
                    Assert.NotNull(request);
                    Assert.Equal("form", request.Mode);
                    Assert.Equal("Please provide more information.", request.Message);
                    Assert.NotNull(request.RequestedSchema);
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
                                var primitiveEnum = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(entry.Value);
                                Assert.Equal(["option1", "option2", "option3"], primitiveEnum.OneOf.Select(e => e.Const));
                                Assert.Equal(["Name1", "Name2", "Name3"], primitiveEnum.OneOf.Select(e => e.Title));
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
                            ["prop1"] = JsonElement.Parse("""
                                "string result"
                                """),
                            ["prop2"] = JsonElement.Parse("""
                                42
                                """),
                            ["prop3"] = JsonElement.Parse("""
                                true
                                """),
                            ["prop4"] = JsonElement.Parse("""
                                "option2"
                                """),
                        },
                    };
                }
            }
        });

        var result = await client.CallToolAsync("TestElicitation", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("success", (result.Content[0] as TextContentBlock)?.Text);
    }
}