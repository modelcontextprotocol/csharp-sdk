using ModelContextProtocol;
using ModelContextProtocol.Server;
using EntraProtectedMcpServer.Configuration;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EntraProtectedMcpServer.Tools;

[McpServerToolType]
public sealed class GraphTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly EntraIdConfiguration _entraIdConfig;

    /// <summary>
    /// Initializes a new instance of the GraphTools class.
    /// </summary>
    public GraphTools(
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
    /// Gets a personalized greeting using Microsoft Graph to retrieve the user's display name.
    /// </summary>
    /// <returns>A personalized hello message with the user's name.</returns>
    [McpServerTool, Description("Get a personalized hello message using Microsoft Graph.")]
    public async Task<string> Hello()
    {
        // Get the current user's access token from the HTTP context
        var httpContext = _httpContextAccessor.HttpContext 
            ?? throw new McpException("HTTP context not available");
        
        var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            throw new McpException("No valid bearer token found in request");
        }
        
        var mcpAccessToken = authHeader["Bearer ".Length..];
        
        try
        {
            // Exchange the MCP token for a Microsoft Graph token using On-Behalf-Of flow
            var graphAccessToken = await GetGraphTokenAsync(mcpAccessToken);
            
            // Create HTTP client for Microsoft Graph
            var client = _httpClientFactory.CreateClient("GraphApi");
            
            // Add the Graph access token to the request
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            
            // Call Microsoft Graph /me endpoint
            var response = await client.GetAsync("/v1.0/me");
            
            if (!response.IsSuccessStatusCode)
            {
                throw new McpException($"Microsoft Graph API call failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            }
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;
            
            // Extract the user's display name from the response
            var displayName = root.TryGetProperty("displayName", out var nameElement) 
                ? nameElement.GetString() 
                : null;
            
            // Fallback to other name properties if displayName is not available
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = root.TryGetProperty("givenName", out var givenNameElement)
                    ? givenNameElement.GetString()
                    : root.TryGetProperty("userPrincipalName", out var upnElement)
                        ? upnElement.GetString()?.Split('@')[0] // Take the part before @ for UPN
                        : "User";
            }
            
            return $"Hello, {displayName}!!";
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Failed to call Microsoft Graph API: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new McpException($"Failed to parse Microsoft Graph API response: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Exchanges an MCP access token for a Microsoft Graph access token using On-Behalf-Of flow.
    /// </summary>
    /// <param name="mcpAccessToken">The original MCP access token.</param>
    /// <returns>A Microsoft Graph access token.</returns>
    private async Task<string> GetGraphTokenAsync(string mcpAccessToken)
    {
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
        
        var client = _httpClientFactory.CreateClient();

        var tokenEndpoint = _entraIdConfig.TokenEndpoint;

        var requestParams = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["client_id"] = _entraIdConfig.ClientId,
            ["client_secret"] = clientSecret,
            ["assertion"] = mcpAccessToken,
            ["scope"] = "https://graph.microsoft.com/User.Read",
            ["requested_token_use"] = "on_behalf_of"
        };
        
        var requestContent = new FormUrlEncodedContent(requestParams);
        
        var response = await client.PostAsync(tokenEndpoint, requestContent);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new McpException($"On-Behalf-Of token exchange failed: {response.StatusCode} - {errorContent}");
        }
        
        var responseContent = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseContent);
        
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            throw new McpException("No access token in On-Behalf-Of response");
        }
        
        return tokenElement.GetString() ?? throw new McpException("Access token is null");
    }
}