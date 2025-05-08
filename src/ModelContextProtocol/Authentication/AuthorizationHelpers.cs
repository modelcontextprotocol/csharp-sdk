using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Provides utility methods for handling authentication in MCP clients.
/// </summary>
public class AuthorizationHelpers
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private static readonly Lazy<HttpClient> _defaultHttpClient = new(() => new HttpClient());
    
    /// <summary>
    /// The common well-known path prefix for resource metadata.
    /// </summary>
    private static readonly string WellKnownPathPrefix = "/.well-known/";

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
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
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
    /// Verifies that the resource URI in the metadata exactly matches the server URL as required by the RFC.
    /// </summary>
    /// <param name="protectedResourceMetadata">The metadata to verify.</param>
    /// <param name="resourceLocation">The server URL to compare against.</param>
    /// <returns>True if the resource URI exactly matches the server, otherwise false.</returns>
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
    /// Normalizes a URI for consistent comparison by removing ports and trailing slashes.
    /// </summary>
    /// <param name="uri">The URI to normalize.</param>
    /// <returns>A normalized string representation of the URI.</returns>
    private static string NormalizeUri(Uri uri)
    {
        // Create a builder that will normalize the URI
        var builder = new UriBuilder(uri)
        {
            Port = -1  // Always remove port specification regardless of whether it's default or not
        };
        
        // Ensure consistent path representation (remove trailing slash if it's just "/")
        if (builder.Path == "/")
        {
            builder.Path = string.Empty;
        }
        // Remove trailing slash for other paths
        else if (builder.Path.Length > 1 && builder.Path.EndsWith("/"))
        {
            builder.Path = builder.Path.TrimEnd('/');
        }
        
        return builder.Uri.ToString().TrimEnd('/');
    }

    /// <summary>
    /// Extracts the base resource URI from a well-known path URL.
    /// </summary>
    /// <param name="metadataUri">The metadata URI containing a well-known path.</param>
    /// <returns>The base URI without the well-known path component.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the URI does not contain a valid well-known path.</exception>
    private Uri ExtractBaseResourceUri(Uri metadataUri)
    {
        // Get the absolute URI path to check for well-known path
        string absoluteUriString = metadataUri.AbsoluteUri;
        
        // Find the well-known path index directly with string operations
        // This avoids the allocation from WellKnownPathPrefix.AsSpan()
        int wellKnownIndex = absoluteUriString.IndexOf(WellKnownPathPrefix, StringComparison.OrdinalIgnoreCase);
        
        // Validate that the URL contains the well-known path
        if (wellKnownIndex <= 0)
        {
            throw new InvalidOperationException(
                $"Resource metadata URL '{metadataUri}' does not contain a valid well-known path format (/.well-known/)");
        }
        
        // Get just the path segment before .well-known directly on the URI
        int wellKnownPathIndex = metadataUri.AbsolutePath.IndexOf(WellKnownPathPrefix, StringComparison.OrdinalIgnoreCase);
        
        // Create a new URI builder using the original scheme and authority
        var baseUriBuilder = new UriBuilder(metadataUri)
        {
            Path = wellKnownPathIndex > 0 ? metadataUri.AbsolutePath.Substring(0, wellKnownPathIndex) : "/",
            Fragment = string.Empty,
            Query = string.Empty
        };
        
        // Ensure the path ends with exactly one slash for consistency
        string path = baseUriBuilder.Path;
        if (string.IsNullOrEmpty(path))
        {
            baseUriBuilder.Path = "/";
        }
        else if (!path.EndsWith("/"))
        {
            baseUriBuilder.Path += "/";
        }
        
        return baseUriBuilder.Uri;
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
        
        // Extract the base URI from the metadata URL
        Uri urlToValidate = ExtractBaseResourceUri(metadataUri);
        _logger.LogDebug($"Validating resource metadata against base URL: {urlToValidate}");
        
        if (!VerifyResourceMatch(metadata, urlToValidate))
        {
            throw new InvalidOperationException(
                $"Resource URI in metadata ({metadata.Resource}) does not match the expected URI ({urlToValidate})");
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
        if (!parameters.Contains(parameterName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = parameters.Split(',');
        foreach (var part in parts)
        {
            int equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex == part.Length - 1)
            {
                continue;
            }
            
            string key = part.Substring(0, equalsIndex).Trim();

            if (string.IsNullOrEmpty(key) || !string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = part.Substring(equalsIndex + 1).Trim();

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
            }
            
            return value;
        }
        
        return null;
    }
}