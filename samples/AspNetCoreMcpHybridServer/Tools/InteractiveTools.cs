using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpHybridServer.Tools;

[McpServerToolType]
public sealed class InteractiveTools
{
    [McpServerTool, Description("Asks for confirmation before returning a greeting.")]
    public static string GreetWithConfirmation(
        GreetingService greetingService,
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        [Description("The name to greet.")] string name)
    {
        if (context.Params?.RequestState == "awaiting-confirmation" &&
            context.Params.InputResponses?.TryGetValue("confirmation", out var response) is true)
        {
            var elicitation = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
            return elicitation?.IsAccepted is true
                ? greetingService.CreateGreeting(name)
                : "Greeting cancelled.";
        }

        if (!server.IsMrtrSupported)
        {
            return "This client cannot provide the required confirmation.";
        }

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                ["confirmation"] = InputRequest.ForElicitation(new ElicitRequestParams
                {
                    Message = $"May I greet {name}?",
                    RequestedSchema = new(),
                }),
            },
            requestState: "awaiting-confirmation");
    }
}
