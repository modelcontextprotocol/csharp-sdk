using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerResourceType]
public sealed class PortfolioResources
{
    private static readonly string UiDirectory = Path.Combine(AppContext.BaseDirectory, "ui");

    [McpServerResource(
        UriTemplate = "ui://portfolio/assign-manager",
        Name = "portfolio-assign-manager",
        MimeType = McpApps.HtmlMimeType)]
    [McpMeta("ui", """{"prefersBorder":true}""")]
    [Description("Custom portfolio review and account manager assignment elicitation UI.")]
    public static string GetAssignManagerUi() =>
        File.ReadAllText(Path.Combine(UiDirectory, "assign-manager.html"));
}
