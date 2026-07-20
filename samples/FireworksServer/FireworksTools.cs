using Microsoft.AspNetCore.SignalR;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FireworksServer;

[McpServerToolType]
public sealed class FireworksTools
{
    [McpServerTool(Name = "launch_fireworks")]
    [McpAppUi(ResourceUri = FireworksResources.StageUri)]
    [Description("Launch a synchronized animated fireworks show in the MCP App and every connected audience browser.")]
    public static async Task<CallToolResult> LaunchFireworks(
        IHubContext<FireworksHub> hub,
        ShowState showState,
        FireworksSettings settings,
        [Description("Short title displayed above the fireworks")] string title,
        [Description("Color palette and visual personality")] FireworksTheme theme = FireworksTheme.Aurora,
        [Description("Show intensity from 1 (subtle) to 5 (spectacular)")] int intensity = 4,
        [Description("Large message revealed during the finale")] string finaleMessage = "BUILD SOMETHING BRILLIANT",
        CancellationToken cancellationToken = default)
    {
        FireworksShow show = FireworksShowFactory.Create(title, theme, intensity, finaleMessage);
        showState.SetLatest(show);
        await hub.Clients.All.SendAsync("ShowLaunched", show, cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = $"Launched \"{show.Title}\" with {show.Cues.Count} synchronized cues. " +
                        $"Audience display: {settings.DashboardUrl}"
                },
            ],
            StructuredContent = JsonSerializer.SerializeToElement(show, JsonSerializerOptions.Web),
        };
    }
}
