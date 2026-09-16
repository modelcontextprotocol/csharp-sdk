using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConformanceServer.Resources;

[McpServerResourceType]
public class ConformanceResources
{
    // Sample base64 encoded 1x1 red PNG pixel for testing
    private const string TestImageBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";

    /// <summary>
    /// Static text resource for testing
    /// </summary>
    [McpServerResource(UriTemplate = "test://static-text", Name = "Static Text Resource", MimeType = "text/plain")]
    [Description("A static text resource for testing")]
    public static string StaticText()
    {
        return "This is the content of the static text resource.";
    }

    /// <summary>
    /// Static binary resource (image) for testing
    /// </summary>
    [McpServerResource(UriTemplate = "test://static-binary", Name = "Static Binary Resource", MimeType = "image/png")]
    [Description("A static binary resource (image) for testing")]
    public static BlobResourceContents StaticBinary()
    {
        return new BlobResourceContents
        {
            Uri = "test://static-binary",
            MimeType = "image/png",
            Blob = System.Text.Encoding.UTF8.GetBytes(TestImageBase64)
        };
    }

    /// <summary>
    /// Resource template with parameter substitution
    /// </summary>
    [McpServerResource(UriTemplate = "test://template/{id}/data", Name = "Resource Template", MimeType = "application/json")]
    [Description("A resource template with parameter substitution")]
    public static TextResourceContents TemplateResource(string id)
    {
        var data = new ResourceData(id, true, $"Data for ID: {id}");

        return new TextResourceContents
        {
            Uri = $"test://template/{id}/data",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(data, JsonContext.Default.ResourceData)
        };
    }

    /// <summary>
    /// Subscribable resource that can send updates
    /// </summary>
    [McpServerResource(UriTemplate = "test://watched-resource", Name = "Watched Resource", MimeType = "text/plain")]
    [Description("A resource that auto-updates every 3 seconds")]
    public static string WatchedResource()
    {
        return "Watched resource content";
    }
}

record ResourceData(string Id, bool TemplateTest, string Data);

[JsonSerializable(typeof(ResourceData))]
internal partial class JsonContext : JsonSerializerContext;
