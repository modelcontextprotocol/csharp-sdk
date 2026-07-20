using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FireworksServer;

[McpServerResourceType]
public sealed class FireworksResources
{
    public const string StageUri = "ui://fireworks/stage";

    [McpServerResource(UriTemplate = StageUri, Name = "fireworks-stage", MimeType = McpApps.HtmlMimeType)]
    [McpMeta("ui", JsonValue = """{"prefersBorder":false}""")]
    [Description("Animated MCP App used to design and render synchronized fireworks shows")]
    public static string GetStage() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));

    [McpServerResource(
        UriTemplate = "show://fireworks/palettes/{theme}",
        Name = "fireworks-theme-palette",
        MimeType = "application/json")]
    [Description("Color palette for a fireworks theme: Aurora, Cyberpunk, Ocean, Sunset, or Celebration")]
    public static string GetPalette([Description("Fireworks theme name")] string theme)
    {
        if (!Enum.TryParse(theme, ignoreCase: true, out FireworksTheme parsedTheme))
        {
            throw new ArgumentException($"Unknown fireworks theme '{theme}'.", nameof(theme));
        }

        return JsonSerializer.Serialize(
            new { Theme = parsedTheme, Colors = FireworksShowFactory.GetPalette(parsedTheme) },
            JsonSerializerOptions.Web);
    }
}
