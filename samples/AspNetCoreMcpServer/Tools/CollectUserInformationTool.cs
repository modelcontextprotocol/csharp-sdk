using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace AspNetCoreMcpServer.Tools;

[McpServerToolType]
public sealed class CollectUserInformationTool
{
    public enum InfoType
    {
        contact,
        preferences,
        feedback
    }

    [McpServerTool(Name = "collect-user-info"), Description("A tool that collects user information through elicitation")]
    public static async Task<CallToolResult> ElicitationEcho(McpServer thisServer, [Description("Type of information to collect")] InfoType infoType)
    {
        ElicitRequestParams elicitRequestParams;
        switch (infoType)
        {
            case InfoType.contact:
                elicitRequestParams = new ElicitRequestParams()
                { 
                    Message = "Please provide your contact information",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>()
                        {
                            ["name"] = new ElicitRequestParams.StringSchema
                            {
                                Title = "Full name",
                                Description = "Your full name",
                            },
                            ["email"] = new ElicitRequestParams.StringSchema
                            {
                                Title = "Email address",
                                Description = "Your email address",
                                Format = "email",
                            },
                            ["phone"] = new ElicitRequestParams.StringSchema
                            {
                                Title = "Phone number",
                                Description = "Your phone number (optional)",
                            }
                        },
                        Required = new List<string> { "name", "email" }
                    }
                };
                break;

            case InfoType.preferences:
                elicitRequestParams = new ElicitRequestParams()
                {
                    Message = "Please set your preferences",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>()
                        {
                            ["theme"] = new ElicitRequestParams.EnumSchema
                            {
                                Title = "Theme",
                                Description = "Choose your preferred theme",
                                Enum = new List<string> { "light", "dark", "auto" },
                                EnumNames = new List<string> { "Light", "Dark", "Auto" }
                            },
                            ["notifications"] = new ElicitRequestParams.BooleanSchema
                            {
                                Title = "Enable notifications",
                                Description = "Would you like to receive notifications?",
                                Default = true,
                            },
                            ["frequency"] = new ElicitRequestParams.EnumSchema
                            {
                                Title = "Notification frequency",
                                Description = "How often would you like notifications?",
                                Enum = new List<string> { "daily", "weekly", "monthly" },
                                EnumNames = new List<string> { "Daily", "Weekly", "Monthly" }
                            }
                        },
                        Required = new List<string> { "theme" }
                    }
                };

                break;

            case InfoType.feedback:
                elicitRequestParams = new ElicitRequestParams()
                {
                    Message = "Please provide your feedback",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>()
                        {
                            ["rating"] = new ElicitRequestParams.NumberSchema
                            {
                                Title = "Rating",
                                Description = "Rate your experience (1-5)",
                                Minimum = 1,
                                Maximum = 5,
                            },
                            ["comments"] = new ElicitRequestParams.StringSchema
                            {
                                Title = "Comments",
                                Description = "Additional comments (optional)",
                                MaxLength = 500,
                            },
                            ["recommend"] = new ElicitRequestParams.BooleanSchema
                            {
                                Title = "Would you recommend this?",
                                Description = "Would you recommend this to others?",
                            }
                        },
                        Required = new List<string> { "rating", "recommend" }
                    }
                };

                break;

            default:
                throw new Exception($"Unknown info type: ${infoType}");

        }


        var result = await thisServer.ElicitAsync(elicitRequestParams);
        var textResult = result.Action switch
        {
            "accept" => $"Thank you! Collected ${infoType} information: {JsonSerializer.Serialize(result.Content, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, JsonElement>)))}",
            "decline" => "No information was collected. User declined ${infoType} information request.",
            "cancel" => "Information collection was cancelled by the user.",
            _ => "Error collecting ${infoType} information: ${error}"
        };

        return new CallToolResult()
        { 
            Content = [ new TextContentBlock { Text = textResult } ],
        };
    }
}
