using ModelContextProtocol.Extensions.Apps.Elicitation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

[McpServerToolType]
public sealed class PortfolioTools
{
    [McpServerTool(Name = "assign_account_manager")]
    [Description("Review a customer portfolio and ask the user to confirm an account manager assignment.")]
    public static CallToolResult AssignAccountManager(
        McpServer server,
        RequestContext<CallToolRequestParams> context)
    {
        var elicitation = McpAppElicitation.SetAppUiIfSupported(
            new ElicitRequestParams
            {
                Message = "Review the Contoso portfolio and confirm its account manager.",
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["confirmed"] = new ElicitRequestParams.BooleanSchema
                        {
                            Title = "Confirm assignment",
                            Default = true,
                        },
                        ["selectedManagerId"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                        {
                            Title = "Account manager",
                            Enum = ["mgr-alex", "mgr-priya", "mgr-sam"],
                            Default = "mgr-priya",
                        },
                    },
                    Required = ["confirmed", "selectedManagerId"],
                },
            },
            context,
            "ui://portfolio/assign-manager");

        var response = McpAppElicitation.ResolveOrRequest(
            server,
            context.Params,
            inputKey: "manager-assignment",
            elicitation,
            DemoJsonContext.Default.ManagerAssignment,
            requestState: "assign-account-manager:v1");

        if (!response.IsAccepted || response.Content is null)
        {
            var disposition = response.Action switch
            {
                "decline" => "declined",
                "cancel" => "canceled",
                _ => response.Action,
            };
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"The user {disposition} the manager assignment." }],
            };
        }

        var assignment = response.Content;
        var summary = assignment.Confirmed
            ? $"Assigned Contoso to {assignment.SelectedManagerId}."
            : $"The user selected {assignment.SelectedManagerId} but did not confirm the assignment.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = summary }],
            StructuredContent = JsonSerializer.SerializeToElement(assignment, DemoJsonContext.Default.ManagerAssignment),
        };
    }
}

public sealed class ManagerAssignment
{
    public bool Confirmed { get; set; }

    public string SelectedManagerId { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ManagerAssignment))]
internal sealed partial class DemoJsonContext : JsonSerializerContext
{
}
