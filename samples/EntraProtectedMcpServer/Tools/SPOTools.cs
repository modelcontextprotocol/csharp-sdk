using ModelContextProtocol;
using ModelContextProtocol.Server;
using EntraProtectedMcpServer.Configuration;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EntraProtectedMcpServer.Tools;

[McpServerToolType]
public sealed class SPOTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly EntraIdConfiguration _entraIdConfig;

    /// <summary>
    /// Initializes a new instance of the SPOTools class.
    /// </summary>
    public SPOTools(
        IHttpClientFactory httpClientFactory, 
        IHttpContextAccessor httpContextAccessor, 
        IConfiguration configuration,
        IOptions<EntraIdConfiguration> entraIdOptions)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _entraIdConfig = entraIdOptions.Value;
    }

    /// <summary>
    /// Retrieves SharePoint site information including title, description, URL, creation date, and language.
    /// </summary>
    /// <param name="siteUrl">The SharePoint site URL.</param>
    /// <returns>Formatted site information as a string.</returns>
    [McpServerTool, Description("Get SharePoint site information using SharePoint REST API.")]
    public async Task<string> GetSiteInfo(
        [Description("The SharePoint site URL (e.g., https://contoso.sharepoint.com/sites/sitename)")] string siteUrl)
    {
        var spoAccessToken = await GetSharePointTokenAsync(siteUrl);
        var client = _httpClientFactory.CreateClient();
        
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", spoAccessToken);
        client.DefaultRequestHeaders.Add("Accept", "application/json;odata=verbose");
        
        var apiUrl = $"{siteUrl.TrimEnd('/')}/_api/web";
        var response = await client.GetAsync(apiUrl);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new McpException($"SharePoint REST API call failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        }

        var jsonContent = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(jsonContent);
        var webInfo = document.RootElement.GetProperty("d");

        var title = webInfo.TryGetProperty("Title", out var titleElement) ? titleElement.GetString() : "Unknown";
        var description = webInfo.TryGetProperty("Description", out var descElement) ? descElement.GetString() : "";
        var url = webInfo.TryGetProperty("Url", out var urlElement) ? urlElement.GetString() : "";
        var created = webInfo.TryGetProperty("Created", out var createdElement) ? createdElement.GetString() : "";
        var language = webInfo.TryGetProperty("Language", out var langElement) ? langElement.GetInt32() : 0;

        return $"""
            Site Title: {title}
            Description: {description}
            URL: {url}
            Created: {created}
            Language: {language}
            """;
    }

    /// <summary>
    /// Exchanges an MCP access token for a SharePoint access token using On-Behalf-Of flow.
    /// </summary>
    /// <param name="siteUrl">The SharePoint site URL to determine the resource scope.</param>
    /// <returns>A SharePoint access token.</returns>
    private async Task<string> GetSharePointTokenAsync(string siteUrl)
    {
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new McpException("HTTP context not available");
        
        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            throw new McpException("No valid bearer token found in request");
        }
        
        var mcpAccessToken = authHeader["Bearer ".Length..];
        
        // Validate required configuration
        if (string.IsNullOrEmpty(_entraIdConfig.TenantId))
        {
            throw new McpException("EntraId:TenantId configuration is required for On-Behalf-Of flow");
        }

        if (string.IsNullOrEmpty(_entraIdConfig.ClientId))
        {
            throw new McpException("EntraId:ClientId configuration is required for On-Behalf-Of flow");
        }

        // Get client secret from configuration (should be in user secrets or secure storage)
        var clientSecret = _entraIdConfig.ClientSecret ?? _configuration["AzureAd:ClientSecret"];
        if (string.IsNullOrEmpty(clientSecret))
        {
            throw new McpException("Client secret not configured for On-Behalf-Of flow. Check EntraId:ClientSecret or AzureAd:ClientSecret configuration.");
        }
        
        // Extract SharePoint resource from URL
        var uri = new Uri(siteUrl);
        var spoResource = $"https://{uri.Host}";
        
        var client = _httpClientFactory.CreateClient();

        var tokenEndpoint = _entraIdConfig.TokenEndpoint;

        var requestParams = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["client_id"] = _entraIdConfig.ClientId,
            ["client_secret"] = clientSecret,
            ["assertion"] = mcpAccessToken,
            ["scope"] = $"{spoResource}/.default",
            ["requested_token_use"] = "on_behalf_of"
        };
        
        var requestContent = new FormUrlEncodedContent(requestParams);
        var response = await client.PostAsync(tokenEndpoint, requestContent);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new McpException($"SharePoint On-Behalf-Of token exchange failed: {response.StatusCode} - {errorContent}");
        }
        
        var responseContent = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseContent);
        
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            throw new McpException("No access token in SharePoint On-Behalf-Of response");
        }
        
        return tokenElement.GetString() ?? throw new McpException("SharePoint access token is null");
    }
}