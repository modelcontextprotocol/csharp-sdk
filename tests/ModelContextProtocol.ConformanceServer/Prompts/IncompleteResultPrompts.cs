#pragma warning disable MCPEXP001 // MRTR (SEP-2322) is experimental.

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ConformanceServer.Prompts;

/// <summary>
/// Prompt implementing the SEP-2322 D1 conformance scenario (<c>incomplete-result-non-tool-request</c>),
/// proving that <c>prompts/get</c> can return an <see cref="InputRequiredResult"/> just like
/// <c>tools/call</c>.
/// </summary>
[McpServerPromptType]
public sealed class IncompleteResultPrompts
{
    [McpServerPrompt(Name = "test_input_required_result_prompt")]
    [Description("SEP-2322 D1: prompts/get returns InputRequiredResult until user_context is supplied.")]
    public static GetPromptResult IncompleteResultPrompt(RequestContext<GetPromptRequestParams> context)
    {
        if (context.Params!.InputResponses is { } responses &&
            responses.TryGetValue("user_context", out var response))
        {
            var elicit = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            var contextValue = TryReadString(elicit?.Content, "context") ?? "(unknown)";
            return new GetPromptResult
            {
                Description = "Prompt customized with elicited user context.",
                Messages =
                [
                    new PromptMessage
                    {
                        Role = Role.User,
                        Content = new TextContentBlock { Text = $"Please continue using context: {contextValue}" },
                    },
                ],
            };
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>
            {
                ["user_context"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = "What context should the prompt use?",
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            ["context"] = new ElicitRequestParams.StringSchema(),
                        },
                        Required = ["context"],
                    },
                }),
            });
    }

    private static string? TryReadString(IDictionary<string, JsonElement>? content, string key)
    {
        if (content is null || !content.TryGetValue(key, out var element))
        {
            return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }
}
