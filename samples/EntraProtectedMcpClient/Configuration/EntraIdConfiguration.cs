namespace EntraProtectedMcpClient.Configuration;

public sealed class EntraIdConfiguration
{
    public const string SectionName = "EntraId";
    
    public required string TenantId { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string ServerClientId { get; set; }
    public required string RedirectUri { get; set; }
    public required string Scope { get; set; }
    public string ResponseMode { get; set; } = "query";
}