using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides utility methods for handling authentication in MCP clients.
/// </summary>
public class AuthorizationHelpers
{    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private static readonly Lazy<HttpClient> _defaultHttpClient = new(() => new HttpClient());

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationHelpers"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests. If null, a default HttpClient will be used.</param>
    /// <param name="logger">The logger to use. If null, a NullLogger will be used.</param>
    public AuthorizationHelpers(HttpClient? httpClient = null, ILogger? logger = null)
    {
        _httpClient = httpClient ?? _defaultHttpClient.Value;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Fetches the protected resource metadata from the provided URL.
    /// </summary>
    /// <param name="metadataUrl">The URL to fetch the metadata from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fetched ProtectedResourceMetadata, or null if it couldn't be fetched.</returns>
    private async Task<ProtectedResourceMetadata?> FetchProtectedResourceMetadataAsync(
        Uri metadataUrl, 
        CancellationToken cancellationToken = default)    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            
            using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(content, 
                McpJsonUtilities.JsonContext.Default.ProtectedResourceMetadata, 
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to fetch protected resource metadata from {metadataUrl}");
            return null;
        }
    }

    /// <summary>
    /// Verifies that the resource URI in the metadata exactly matches the original request URL as required by the RFC.
    /// Per RFC: The resource value must be identical to the URL that the client used to make the request to the resource server.
    /// </summary>
    /// <param name="protectedResourceMetadata">The metadata to verify.</param>
    /// <param name="resourceLocation">The original URL the client used to make the request to the resource server.</param>
    /// <returns>True if the resource URI exactly matches the original request URL, otherwise false.</returns>
    private static bool VerifyResourceMatch(ProtectedResourceMetadata protectedResourceMetadata, Uri resourceLocation)
    {
        if (protectedResourceMetadata.Resource == null || resourceLocation == null)
        {
            return false;
        }

        // Per RFC: The resource value must be identical to the URL that the client used
        // to make the request to the resource server. Compare entire URIs, not just the host.

        // Normalize the URIs to ensure consistent comparison
        string normalizedMetadataResource = NormalizeUri(protectedResourceMetadata.Resource);
        string normalizedResourceLocation = NormalizeUri(resourceLocation);

        return string.Equals(normalizedMetadataResource, normalizedResourceLocation, StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Normalizes a URI for consistent comparison.
    /// </summary>
    /// <param name="uri">The URI to normalize.</param>
    /// <returns>A normalized string representation of the URI.</returns>
    private static string NormalizeUri(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Port = -1  // Always remove port
        };
        
        if (builder.Path == "/")
        {
            builder.Path = string.Empty;
        }
        else if (builder.Path.Length > 1 && builder.Path.EndsWith("/"))
        {
            builder.Path = builder.Path.TrimEnd('/');
        }
        
        return builder.Uri.ToString();    }

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
    internal async Task<ProtectedResourceMetadata> ExtractProtectedResourceMetadata(
        HttpResponseMessage response,
        Uri serverUrl,
        CancellationToken cancellationToken = default)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException($"Expected a 401 Unauthorized response, but received {(int)response.StatusCode} {response.StatusCode}");
        }

        // Extract the WWW-Authenticate header
        if (response.Headers.WwwAuthenticate.Count == 0)
        {
            throw new InvalidOperationException("The 401 response does not contain a WWW-Authenticate header");
        }

        // Look for the Bearer authentication scheme with resource_metadata parameter
        string? resourceMetadataUrl = null;
        foreach (var header in response.Headers.WwwAuthenticate)
        {
            if (string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(header.Parameter))
            {
                resourceMetadataUrl = ParseWwwAuthenticateParameters(header.Parameter, "resource_metadata");
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
        var metadata = await FetchProtectedResourceMetadataAsync(metadataUri, cancellationToken).ConfigureAwait(false);
        if (metadata == null)
        {
            throw new InvalidOperationException($"Failed to fetch resource metadata from {resourceMetadataUrl}");
        }

        // Per RFC: The resource value must be identical to the URL that the client used
        // to make the request to the resource server
        _logger.LogDebug($"Validating resource metadata against original server URL: {serverUrl}");

        if (!VerifyResourceMatch(metadata, serverUrl))
        {
            throw new InvalidOperationException(
                $"Resource URI in metadata ({metadata.Resource}) does not match the expected URI ({serverUrl})");
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
        if (parameters.IndexOf(parameterName, StringComparison.OrdinalIgnoreCase) == -1)
        {
            return null;
        }

        foreach (var part in parameters.Split(','))
        {
            string trimmedPart = part.Trim();
            int equalsIndex = trimmedPart.IndexOf('=');
            
            if (equalsIndex <= 0)
            {
                continue;
            }
            
            string key = trimmedPart.Substring(0, equalsIndex).Trim();
            
            if (string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                string value = trimmedPart.Substring(equalsIndex + 1).Trim();
                
                if (value.StartsWith("\"") && value.EndsWith("\""))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                
                return value;
            }
        }
        
        return null;
    }

    /// <summary>
    /// Handles a 401 Unauthorized response and returns all available authorization servers.
    /// This is the primary method for OAuth discovery - use this when you want full control 
    /// over authorization server selection.
    /// </summary>
    /// <param name="response">The 401 HTTP response.</param>
    /// <param name="serverUrl">The server URL that returned the 401.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of available authorization server URIs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when response is null.</exception>    
    public async Task<IReadOnlyList<Uri>> GetAvailableAuthorizationServersAsync(
        HttpResponseMessage response,
        Uri serverUrl,
        CancellationToken cancellationToken = default)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        try
        {
            // Extract resource metadata behind the scenes
            var metadata = await ExtractProtectedResourceMetadata(response, serverUrl, cancellationToken);
            return metadata.AuthorizationServers ?? new List<Uri>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available authorization servers");
            return new List<Uri>();
        }
    }
}