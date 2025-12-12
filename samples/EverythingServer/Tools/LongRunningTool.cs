using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public class LongRunningTool
{
    [McpServerTool(Name = "longRunningOperation"), Description("Demonstrates a long running operation with progress updates")]
    public static async Task<string> LongRunningOperation(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        int duration = 10,
        int steps = 5)
    {
        var progressToken = context.Params?.ProgressToken;
        var stepDuration = duration / steps;

        for (int i = 1; i <= steps + 1; i++)
        {
            await Task.Delay(stepDuration * 1000);
            
            if (progressToken is not null)
            {
                await server.SendNotificationAsync("notifications/progress", new
                    {
                        Progress = i,
                        Total = steps,
                        progressToken
                    });
            }
        }

        return $"Long running operation completed. Duration: {duration} seconds. Steps: {steps}.";
    }
}
