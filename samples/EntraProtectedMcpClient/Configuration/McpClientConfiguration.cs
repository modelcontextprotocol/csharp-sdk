namespace EntraProtectedMcpClient.Configuration;

public sealed class McpClientConfiguration
{
    public const string SectionName = "McpServer";
    
    public required string Url { get; set; }
}