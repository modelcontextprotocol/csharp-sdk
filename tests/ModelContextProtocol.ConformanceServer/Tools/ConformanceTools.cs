using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ConformanceServer.Tools;

[McpServerToolType]
public class ConformanceTools
{
    // Sample base64 encoded 1x1 red PNG pixel for testing
    private const string TestImageBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";

    // Sample base64 encoded minimal WAV file for testing
    private const string TestAudioBase64 =
        "UklGRiYAAABXQVZFZm10IBAAAAABAAEAQB8AAAB9AAACABAAZGF0YQIAAAA=";

    /// <summary>
    /// Simple text tool - returns simple text content for testing
    /// </summary>
    [McpServerTool(Name = "test_simple_text")]
    [Description("Tests simple text content response")]
    public static string SimpleText()
    {
        return "This is a simple text response for testing.";
    }

    /// <summary>
    /// Image content tool - returns base64-encoded image content
    /// </summary>
    [McpServerTool(Name = "test_image_content")]
    [Description("Tests image content response")]
    public static ImageContentBlock ImageContent()
    {
        return new ImageContentBlock
        {
            Data = System.Text.Encoding.UTF8.GetBytes(TestImageBase64),
            MimeType = "image/png"
        };
    }

    /// <summary>
    /// Audio content tool - returns base64-encoded audio content
    /// </summary>
    [McpServerTool(Name = "test_audio_content")]
    [Description("Tests audio content response")]
    public static AudioContentBlock AudioContent()
    {
        return new AudioContentBlock
        {
            Data = System.Text.Encoding.UTF8.GetBytes(TestAudioBase64),
            MimeType = "audio/wav"
        };
    }

    /// <summary>
    /// Embedded resource tool - returns embedded resource content
    /// </summary>
    [McpServerTool(Name = "test_embedded_resource")]
    [Description("Tests embedded resource content response")]
    public static EmbeddedResourceBlock EmbeddedResource()
    {
        return new EmbeddedResourceBlock
        {
            Resource = new TextResourceContents
            {
                Uri = "test://embedded-resource",
                MimeType = "text/plain",
                Text = "This is an embedded resource content."
            }
        };
    }

    /// <summary>
    /// Multiple content types tool - returns mixed content types (text, image, resource)
    /// </summary>
    [McpServerTool(Name = "test_multiple_content_types")]
    [Description("Tests response with multiple content types (text, image, resource)")]
    public static ContentBlock[] MultipleContentTypes()
    {
        return
        [
            new TextContentBlock { Text = "Multiple content types test:" },
            new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes(TestImageBase64), MimeType = "image/png" },
            new EmbeddedResourceBlock
            {
                Resource = new TextResourceContents
                {
                    Uri = "test://mixed-content-resource",
                    MimeType = "application/json",
                    Text = "{ \"test\" = \"data\", \"value\" = 123 }"
                }
            }
        ];
    }

    /// <summary>
    /// Tool with logging - emits log messages during execution
    /// </summary>
    [McpServerTool(Name = "test_tool_with_logging")]
    [Description("Tests tool that emits log messages during execution")]
    public static async Task<string> ToolWithLogging(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var server = context.Server;

        // Use ILogger for logging (will be forwarded to client if supported)
        ILoggerProvider loggerProvider = server.AsClientLoggerProvider();
        ILogger logger = loggerProvider.CreateLogger("ConformanceTools");

        logger.LogInformation("Tool execution started");
        await Task.Delay(50, cancellationToken);

        logger.LogInformation("Tool processing data");
        await Task.Delay(50, cancellationToken);

        logger.LogInformation("Tool execution completed");

        return "Tool with logging executed successfully";
    }

    /// <summary>
    /// Tool with progress - reports progress notifications
    /// </summary>
    [McpServerTool(Name = "test_tool_with_progress")]
    [Description("Tests tool that reports progress notifications")]
    public static async Task<string> ToolWithProgress(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        var progressToken = context.Params?.ProgressToken;

        if (progressToken is not null)
        {
            await server.NotifyProgressAsync(progressToken.Value, new ProgressNotificationValue
            {
                Progress = 0,
                Total = 100,
            }, cancellationToken: cancellationToken);

            await Task.Delay(50, cancellationToken);

            await server.NotifyProgressAsync(progressToken.Value, new ProgressNotificationValue
            {
                Progress = 50,
                Total = 100,
            }, cancellationToken: cancellationToken);

            await Task.Delay(50, cancellationToken);

            await server.NotifyProgressAsync(progressToken.Value, new ProgressNotificationValue
            {
                Progress = 100,
                Total = 100,
            }, cancellationToken: cancellationToken);
        }

        return progressToken?.ToString() ?? "No progress token provided";
    }

    /// <summary>
    /// Error handling tool - intentionally throws an error for testing
    /// </summary>
    [McpServerTool(Name = "test_error_handling")]
    [Description("Tests error response handling")]
    public static string ErrorHandling()
    {
        throw new Exception("This tool intentionally returns an error for testing");
    }

    /// <summary>
    /// Sampling tool - requests LLM completion from client
    /// </summary>
    [McpServerTool(Name = "test_sampling")]
    [Description("Tests server-initiated sampling (LLM completion request)")]
    public static async Task<string> Sampling(
        McpServer server,
        [Description("The prompt to send to the LLM")] string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            var samplingParams = new CreateMessageRequestParams
            {
                Messages = [new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = prompt }],
                }],
                MaxTokens = 100,
                Temperature = 0.7f
            };

            var result = await server.SampleAsync(samplingParams, cancellationToken);
            return $"Sampling result: {(result.Content.FirstOrDefault() as TextContentBlock)?.Text ?? "No text content"}";
        }
        catch (Exception ex)
        {
            return $"Sampling not supported or error: {ex.Message}";
        }
    }

    /// <summary>
    /// Elicitation tool - requests user input from client
    /// </summary>
    [McpServerTool(Name = "test_elicitation")]
    [Description("Tests elicitation (user input request from client)")]
    public static async Task<string> Elicitation(
        McpServer server,
        [Description("Message to show to the user")] string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var schema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["response"] = new ElicitRequestParams.StringSchema()
                    {
                        Description = "User's response to the message"
                    }
                }
            };

            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = message,
                RequestedSchema = schema
            }, cancellationToken);

            if (result.Action == "accept" && result.Content != null)
            {
                return $"User responded: {result.Content["response"].GetString()}";
            }
            else
            {
                return $"Elicitation {result.Action}";
            }
        }
        catch (Exception ex)
        {
            return $"Elicitation not supported or error: {ex.Message}";
        }
    }

    /// <summary>
    /// SEP-1034: Elicitation with default values for all primitive types
    /// </summary>
    [McpServerTool(Name = "test_elicitation_sep1034_defaults")]
    [Description("Tests elicitation with default values per SEP-1034")]
    public static async Task<string> ElicitationSep1034Defaults(
        McpServer server,
        CancellationToken cancellationToken)
    {
        try
        {
            var schema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["name"] = new ElicitRequestParams.StringSchema()
                    {
                        Description = "Name",
                        Default = "John Doe"
                    },
                    ["age"] = new ElicitRequestParams.NumberSchema()
                    {
                        Type = "integer",
                        Description = "Age",
                        Default = 30
                    },
                    ["score"] = new ElicitRequestParams.NumberSchema()
                    {
                        Description = "Score",
                        Default = 95.5
                    },
                    ["status"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema()
                    {
                        Description = "Status",
                        Enum = ["active", "inactive", "pending"],
                        Default = "active"
                    },
                    ["verified"] = new ElicitRequestParams.BooleanSchema()
                    {
                        Description = "Verified",
                        Default = true
                    }
                }
            };

            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = "Test elicitation with default values for primitive types",
                RequestedSchema = schema
            }, cancellationToken);

            if (result.Action == "accept" && result.Content != null)
            {
                return $"Accepted with values: string={result.Content["stringField"].GetString()}, " +
                       $"number={result.Content["numberField"].GetInt32()}, " +
                       $"boolean={result.Content["booleanField"].GetBoolean()}";
            }
            else
            {
                return $"Elicitation {result.Action}";
            }
        }
        catch (Exception ex)
        {
            return $"Elicitation not supported or error: {ex.Message}";
        }
    }

    /// <summary>
    /// SEP-1330: Elicitation with enum schema improvements
    /// </summary>
    [McpServerTool(Name = "test_elicitation_sep1330_enums")]
    [Description("Tests elicitation with enum schema improvements per SEP-1330")]
    public static async Task<string> ElicitationSep1330Enums(
        McpServer server,
        CancellationToken cancellationToken)
    {
        try
        {
            var schema = new ElicitRequestParams.RequestSchema
            {
                Properties =
                {
                    ["untitledSingle"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema()
                    {
                        Description = "Choose an option",
                        Enum = ["option1", "option2", "option3"]
                    },
                    ["titledSingle"] = new ElicitRequestParams.TitledSingleSelectEnumSchema()
                    {
                        Description = "Choose a titled option",
                        OneOf =
                        [
                            new() { Const = "value1", Title = "First Option" },
                            new() { Const = "value2", Title = "Second Option" },
                            new() { Const = "value3", Title = "Third Option" }
                        ]
                    },
#pragma warning disable MCP9001
                    ["legacyEnum"] = new ElicitRequestParams.LegacyTitledEnumSchema()
                    {
                        Description = "Choose a legacy option",
                        Enum = ["opt1", "opt2", "opt3"],
                        EnumNames = ["Option One", "Option Two", "Option Three"]
                    },
#pragma warning restore MCP9001
                    ["untitledMulti"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema()
                    {
                        Description = "Choose multiple options",
                        Items = new ElicitRequestParams.UntitledEnumItemsSchema
                        {
                            Enum = ["option1", "option2", "option3"]
                        }
                    },
                    ["titledMulti"] = new ElicitRequestParams.TitledMultiSelectEnumSchema()
                    {
                        Description = "Choose multiple titled options",
                        Items = new ElicitRequestParams.TitledEnumItemsSchema
                        {
                            AnyOf =
                            [
                                new() { Const = "value1", Title = "First Choice" },
                                new() { Const = "value2", Title = "Second Choice" },
                                new() { Const = "value3", Title = "Third Choice" }
                            ]
                        }
                    }
                }
            };

            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = "Test elicitation with enum schema",
                RequestedSchema = schema
            }, cancellationToken);

            if (result.Action == "accept" && result.Content != null)
            {
                return $"Elicitation completed: action={result.Action}, content={result.Content}";
            }
            else
            {
                return $"Elicitation {result.Action}";
            }
        }
        catch (Exception ex)
        {
            return $"Elicitation not supported or error: {ex.Message}";
        }
    }

    /// <summary>Create the json_schema_2020_12_tool with a raw JSON Schema 2020-12 inputSchema.</summary>
    public static McpServerTool CreateJsonSchema202012Tool()
    {
        var tool = McpServerTool.Create(
            () => "JSON Schema 2020-12 tool executed successfully",
            new()
            {
                Name = "json_schema_2020_12_tool",
                Description = "Tool with JSON Schema 2020-12 features"
            });

        tool.ProtocolTool.InputSchema = JsonElement.Parse("""
            {
                "$schema": "https://json-schema.org/draft/2020-12/schema",
                "type": "object",
                "$defs": {
                    "address": {
                        "type": "object",
                        "properties": {
                            "street": { "type": "string" },
                            "city": { "type": "string" }
                        }
                    }
                },
                "properties": {
                    "name": { "type": "string" },
                    "address": { "$ref": "#/$defs/address" }
                },
                "additionalProperties": false
            }
            """);

        return tool;
    }

    [McpServerTool(Name = "test_reconnection")]
    [Description("Tests SSE stream reconnection by closing the stream mid-call")]
    public static string TestReconnection()
    {
        // This tool doesn't need to do anything - the call filter will close the stream after this tool runs,
        // and the client must reconnect to get the result.
        return "Reconnection test completed successfully";
    }
}