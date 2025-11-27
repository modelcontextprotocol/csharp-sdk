namespace EntraProtectedMcpServer.Configuration;

public sealed class ServerConfiguration
{
    public const string SectionName = "Server";
    
    public required string Url { get; set; }
    public string? ResourceDocumentationUrl { get; set; }
}