using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides utility methods for handling authentication in MCP clients.
/// </summary>
public class AuthorizationHelpers
{
    private readonly HttpClient _httpClient;
    
    /// <summary>
    /// Client name for IHttpClientFactory used by the AuthorizationHelpers.
    /// </summary>
    public const string HttpClientName = "ModelContextProtocol.Authentication";

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationHelpers"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory to use for creating HTTP clients.</param>
    public AuthorizationHelpers(IHttpClientFactory httpClientFactory)
    {
        Throw.IfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
    }

    /// <summary>
    /// Fetches the protected resource metadata from the provided URL.
    /// </summary>
    /// <param name="metadataUrl">The URL to fetch the metadata from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fetched ProtectedResourceMetadata, or null if it couldn't be fetched.</returns>
    private async Task<ProtectedResourceMetadata?> FetchProtectedResourceMetadataAsync(
        Uri metadataUrl, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(content, 
                McpJsonUtilities.JsonContext.Default.ProtectedResourceMetadata, 
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies that the resource URI in the metadata matches the server URL.
    /// </summary>
    /// <param name="protectedResourceMetadata">The metadata to verify.</param>
    /// <param name="serverUrl">The server URL to compare against.</param>
    /// <returns>True if the resource URI matches the server, otherwise false.</returns>
    private static bool VerifyResourceMatch(ProtectedResourceMetadata protectedResourceMetadata, Uri serverUrl)
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
    public async Task<ProtectedResourceMetadata> ExtractProtectedResourceMetadata(
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

        Uri metadataUri = new(resourceMetadataUrl);
        
        var metadata = await FetchProtectedResourceMetadataAsync(metadataUri, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException($"Failed to fetch resource metadata from {resourceMetadataUrl}");
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
    private static string? ParseWwwAuthenticateParameters(string parameters, string parameterName)
    {
        // Handle parameters in the format: param1="value1", param2="value2"
        var paramDict = parameters.Split(',')
            .Select(p => p.Trim())
            .Select(p => 
            {
                var parts = p.Split(['='], 2);
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
            .ToDictionary();

        if (paramDict.TryGetValue(parameterName, out var value))
        {
            return value;
        }

        return null;
    }
}