using ModelContextProtocol.Types.Authentication;
using System.Text.Json;

namespace ProtectedMCPClient.Utils
{
    internal class AuthorizationServerUtils
    {
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
                    return await JsonSerializer.DeserializeAsync<AuthorizationServerMetadata>(content, new JsonSerializerOptions() {  WriteIndented = true },
                        cancellationToken);
                }
            }
            catch (Exception)
            {
                // Ignore exceptions and return null
            }

            return null;
        }
    }
}
