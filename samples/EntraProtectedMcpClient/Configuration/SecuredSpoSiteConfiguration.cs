namespace EntraProtectedMcpClient.Configuration;

public sealed class SecuredSpoSiteConfiguration
{
    public const string SectionName = "SecuredSpoSite";
    
    public string Url { get; set; } = "https://docs.microsoft.com/";
}