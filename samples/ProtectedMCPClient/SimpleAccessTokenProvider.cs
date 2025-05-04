using ModelContextProtocol.Authentication;
using System.Collections.Concurrent;

namespace ProtectedMCPClient;

/// <summary>
/// A simple implementation of IAccessTokenProvider that uses a fixed API key.
/// This is just for demonstration purposes.
/// </summary>
public class SimpleAccessTokenProvider : IAccessTokenProvider
{
    private readonly string _apiKey;
    private readonly ConcurrentDictionary<string, string> _tokenCache = new();
    private readonly Uri _serverUrl;

    public SimpleAccessTokenProvider(string apiKey, Uri serverUrl)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _serverUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
    }

    /// <inheritdoc />
    public Task<string?> GetAuthenticationTokenAsync(Uri resourceUri, CancellationToken cancellationToken = default)
    {
        // In a real implementation, you might use different tokens for different resources,
        // or refresh tokens when they're about to expire
        return Task.FromResult<string?>(_apiKey);
    }

    /// <inheritdoc />
    public async Task<bool> HandleUnauthorizedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the updated AuthenticationChallengeHandler to handle the 401 challenge
            var resourceMetadata = await AuthenticationUtils.ExtractProtectedResourceMetadata(
                response,
                _serverUrl,
                cancellationToken);
            
            // If we get here, the resource metadata is valid and matches our server
            Console.WriteLine($"Successfully validated resource metadata for: {resourceMetadata.Resource}");
            
            // For a real implementation, you would:
            // 1. Use the metadata to get information about the authorization servers
            // 2. Obtain a new token from one of those authorization servers
            // 3. Store the new token for future requests
            
            // Example of what a real implementation might do:
            /*
            if (resourceMetadata.AuthorizationServers?.Count > 0) 
            {
                var authServerUrl = resourceMetadata.AuthorizationServers[0];
                var authServerMetadata = await AuthenticationUtils.FetchAuthorizationServerMetadataAsync(
                    authServerUrl, cancellationToken);
                
                if (authServerMetadata != null)
                {
                    // Use auth server metadata to obtain a new token
                    // Store the token in _tokenCache
                    // Return true to indicate the unauthorized response was handled
                    return true;
                }
            }
            */
            
            // For now, we still return false since we're not actually refreshing the token
            Console.WriteLine("API key is valid, but might not have sufficient permissions.");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            // Log the specific error about why the challenge handling failed
            Console.WriteLine($"Authentication challenge failed: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            // Log any unexpected errors
            Console.WriteLine($"Unexpected error during authentication challenge: {ex.Message}");
            return false;
        }
    }
}