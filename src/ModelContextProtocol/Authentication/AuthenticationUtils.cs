using ModelContextProtocol.Types.Authentication;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides utility methods for handling authentication in MCP clients.
/// </summary>
public static class AuthenticationUtils
{
    /// <summary>
    /// Extracts protected resource metadata from an unauthorized response.
    /// </summary>
    /// <param name="response">The HTTP response containing the WWW-Authenticate header.</param>
    /// <returns>The extracted ProtectedResourceMetadata, or null if it couldn't be extracted.</returns>
    public static ProtectedResourceMetadata? ExtractProtectedResourceMetadata(HttpResponseMessage response)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        // Extract the WWW-Authenticate header
        if (!response.Headers.WwwAuthenticate.Any())
        {
            return null;
        }

        // Look for the Bearer authentication scheme with resource_metadata parameter
        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                var parameters = header.Parameter;
                if (string.IsNullOrEmpty(parameters))
                {
                    continue;
                }

                // Parse the parameters to find resource_metadata
                var resourceMetadataUrl = ParseWwwAuthenticateParameters(parameters, "resource_metadata");
                if (resourceMetadataUrl != null)
                {
                    return FetchProtectedResourceMetadataAsync(new Uri(resourceMetadataUrl)).GetAwaiter().GetResult();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fetches the protected resource metadata from the provided URL.
    /// </summary>
    /// <param name="metadataUrl">The URL to fetch the metadata from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fetched ProtectedResourceMetadata, or null if it couldn't be fetched.</returns>
    public static async Task<ProtectedResourceMetadata?> FetchProtectedResourceMetadataAsync(
        Uri metadataUrl, 
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        try
        {
            var response = await httpClient.GetAsync(metadataUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync(content, 
                McpJsonUtilities.JsonContext.Default.ProtectedResourceMetadata, 
                cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches the authorization server metadata from a server URL, trying both well-known endpoints.
    /// </summary>
    /// <param name="authorizationServerUrl">The base URL of the authorization server.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fetched AuthorizationServerMetadata, or null if it couldn't be fetched.</returns>
    public static async Task<AuthorizationServerMetadata?> FetchAuthorizationServerMetadataAsync(
        Uri authorizationServerUrl, 
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        
        // Try OpenID Connect configuration endpoint first, then OAuth Authorization Server Metadata endpoint
        string[] wellKnownEndpoints = {
            "/.well-known/openid-configuration",
            "/.well-known/oauth-authorization-server"
        };

        foreach (var endpoint in wellKnownEndpoints)
        {
            var metadataUrl = new Uri(authorizationServerUrl, endpoint);
            var metadata = await TryFetchMetadataAsync(httpClient, metadataUrl, cancellationToken);
            if (metadata != null)
            {
                return metadata;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to fetch metadata from a specific URL.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for the request.</param>
    /// <param name="metadataUrl">The URL to fetch metadata from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The metadata if successful, or null if the fetch fails.</returns>
    private static async Task<AuthorizationServerMetadata?> TryFetchMetadataAsync(
        HttpClient httpClient, 
        Uri metadataUrl, 
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync(metadataUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStreamAsync();
                return await JsonSerializer.DeserializeAsync(content,
                    McpJsonUtilities.JsonContext.Default.AuthorizationServerMetadata, 
                    cancellationToken);
            }
        }
        catch (Exception)
        {
            // Ignore exceptions and return null
        }

        return null;
    }

    /// <summary>
    /// Verifies that the resource URI in the metadata matches the server URL.
    /// </summary>
    /// <param name="protectedResourceMetadata">The metadata to verify.</param>
    /// <param name="serverUrl">The server URL to compare against.</param>
    /// <returns>True if the resource URI matches the server, otherwise false.</returns>
    public static bool VerifyResourceMatch(ProtectedResourceMetadata protectedResourceMetadata, Uri serverUrl)
    {
        if (protectedResourceMetadata.Resource == null || serverUrl == null)
        {
            return false;
        }

        // Compare hosts using Uri properties directly
        return Uri.Compare(
            protectedResourceMetadata.Resource, 
            serverUrl, 
            UriComponents.Host, 
            UriFormat.UriEscaped, 
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>
    /// Responds to a 401 challenge by parsing the WWW-Authenticate header, fetching the resource metadata,
    /// verifying the resource match, and returning the metadata if valid.
    /// </summary>
    /// <param name="response">The HTTP response containing the WWW-Authenticate header.</param>
    /// <param name="serverUrl">The server URL to verify against the resource metadata.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resource metadata if the resource matches the server, otherwise throws an exception.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the response is not a 401, lacks a WWW-Authenticate header,
    /// lacks a resource_metadata parameter, the metadata can't be fetched, or the resource URI doesn't match the server URL.</exception>
    public static async Task<ProtectedResourceMetadata> HandleAuthenticationChallengeAsync(
        HttpResponseMessage response,
        Uri serverUrl,
        CancellationToken cancellationToken = default)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException($"Expected a 401 Unauthorized response, but received {(int)response.StatusCode} {response.StatusCode}");
        }

        // Extract the WWW-Authenticate header
        if (!response.Headers.WwwAuthenticate.Any())
        {
            throw new InvalidOperationException("The 401 response does not contain a WWW-Authenticate header");
        }

        // Look for the Bearer authentication scheme with resource_metadata parameter
        string? resourceMetadataUrl = null;
        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                var parameters = header.Parameter;
                if (string.IsNullOrEmpty(parameters))
                {
                    continue;
                }

                // Parse the parameters to find resource_metadata
                resourceMetadataUrl = ParseWwwAuthenticateParameters(parameters, "resource_metadata");
                if (resourceMetadataUrl != null)
                {
                    break;
                }
            }
        }

        if (resourceMetadataUrl == null)
        {
            throw new InvalidOperationException("The WWW-Authenticate header does not contain a resource_metadata parameter");
        }

        Uri metadataUri = new Uri(resourceMetadataUrl);
        
        // Fetch the resource metadata
        var metadata = await FetchProtectedResourceMetadataAsync(metadataUri, cancellationToken);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Failed to fetch resource metadata from {resourceMetadataUrl}");
        }

        // Verify the resource matches the server
        if (!VerifyResourceMatch(metadata, serverUrl))
        {
            throw new InvalidOperationException(
                $"Resource URI in metadata ({metadata.Resource}) does not match the server URI ({serverUrl})");
        }

        return metadata;
    }

    /// <summary>
    /// Parses the WWW-Authenticate header parameters to extract a specific parameter.
    /// </summary>
    /// <param name="parameters">The parameter string from the WWW-Authenticate header.</param>
    /// <param name="parameterName">The name of the parameter to extract.</param>
    /// <returns>The value of the parameter, or null if not found.</returns>
    public static string? ParseWwwAuthenticateParameters(string parameters, string parameterName)
    {
        // Handle parameters in the format: param1="value1", param2="value2"
        var paramDict = parameters.Split(',')
            .Select(p => p.Trim())
            .Select(p => 
            {
                var parts = p.Split(new[] { '=' }, 2);
                if (parts.Length != 2)
                {
                    return new KeyValuePair<string, string>(string.Empty, string.Empty);
                }
                
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                
                // Remove surrounding quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\""))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                
                return new KeyValuePair<string, string>(key, value);
            })
            .Where(kvp => !string.IsNullOrEmpty(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        if (paramDict.TryGetValue(parameterName, out var value))
        {
            return value;
        }

        return null;
    }
}