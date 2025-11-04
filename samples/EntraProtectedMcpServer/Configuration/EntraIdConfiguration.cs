namespace EntraProtectedMcpServer.Configuration;

public sealed class EntraIdConfiguration
{
    public const string SectionName = "EntraId";
    
    public required string TenantId { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public string? AuthorityUrl { get; set; }
    public string? TokenEndpoint { get; set; }
    public List<string> ValidAudiences { get; set; } = [];
    public List<string> ValidIssuers { get; set; } = [];
    public List<string> ScopesSupported { get; set; } = [];
}