using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Client;

internal static class DiscoverResultCloner
{
    public static DiscoverResult Clone(DiscoverResult discoverResult)
    {
        return new DiscoverResult
        {
            SupportedVersions = [.. discoverResult.SupportedVersions],
            Capabilities = CloneServerCapabilities(discoverResult.Capabilities),
            ServerInfo = CloneImplementation(discoverResult.ServerInfo),
            Instructions = discoverResult.Instructions,
            TimeToLive = discoverResult.TimeToLive,
            CacheScope = discoverResult.CacheScope,
            Meta = discoverResult.Meta is null ? null : (JsonObject)discoverResult.Meta.DeepClone(),
            ResultType = discoverResult.ResultType,
        };
    }

#pragma warning disable MCP9005 // LoggingCapability is deprecated but still part of the protocol DTO.
    private static ServerCapabilities CloneServerCapabilities(ServerCapabilities capabilities) =>
        new()
        {
            Experimental = CloneObjectDictionary(capabilities.Experimental),
            Logging = capabilities.Logging is null ? null : new LoggingCapability(),
            Prompts = capabilities.Prompts is null ? null : new PromptsCapability { ListChanged = capabilities.Prompts.ListChanged },
            Resources = capabilities.Resources is null ? null : new ResourcesCapability
            {
                Subscribe = capabilities.Resources.Subscribe,
                ListChanged = capabilities.Resources.ListChanged,
            },
            Tools = capabilities.Tools is null ? null : new ToolsCapability { ListChanged = capabilities.Tools.ListChanged },
            Completions = capabilities.Completions is null ? null : new CompletionsCapability(),
            Extensions = CloneObjectDictionary(capabilities.Extensions),
        };
#pragma warning restore MCP9005

    private static Implementation CloneImplementation(Implementation implementation) =>
        new()
        {
            Name = implementation.Name,
            Title = implementation.Title,
            Version = implementation.Version,
            Description = implementation.Description,
            Icons = implementation.Icons is null ? null : [.. implementation.Icons.Select(CloneIcon)],
            WebsiteUrl = implementation.WebsiteUrl,
        };

    private static Icon CloneIcon(Icon icon) =>
        new()
        {
            Source = icon.Source,
            MimeType = icon.MimeType,
            Sizes = icon.Sizes is null ? null : [.. icon.Sizes],
            Theme = icon.Theme,
        };

    private static IDictionary<string, object>? CloneObjectDictionary(IDictionary<string, object>? values) =>
        values is null ? null : new Dictionary<string, object>(values, StringComparer.Ordinal);
}
