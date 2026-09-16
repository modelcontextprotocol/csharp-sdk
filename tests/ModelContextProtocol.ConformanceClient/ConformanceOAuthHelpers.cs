using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;

internal sealed class ConformanceContext
{
    private readonly JsonElement _root;

    private ConformanceContext(JsonElement root)
    {
        _root = root;
    }

    public static ConformanceContext? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return new ConformanceContext(document.RootElement.Clone());
    }

    public string GetRequiredString(string propertyName)
    {
        if (!_root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(value.GetString()))
        {
            throw new InvalidOperationException(
                $"MCP_CONFORMANCE_CONTEXT is missing required string property '{propertyName}'.");
        }

        return value.GetString()!;
    }

    public string? GetString(string propertyName) =>
        _root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal static class ConformanceOAuthHelpers
{
    private const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    public static async Task<string> AcquireClientCredentialsTokenAsync(
        Uri resourceUri,
        ConformanceContext context,
        bool usePrivateKeyJwt,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var discovery = await DiscoverAsync(httpClient, resourceUri, cancellationToken).ConfigureAwait(false);
        var clientId = context.GetRequiredString("client_id");

        Dictionary<string, string> formFields = new()
        {
            ["grant_type"] = "client_credentials",
            ["resource"] = discovery.Resource,
        };

        if (discovery.Scopes.Count > 0)
        {
            formFields["scope"] = string.Join(" ", discovery.Scopes);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint);
        if (usePrivateKeyJwt)
        {
            var algorithm = context.GetString("signing_algorithm") ?? "ES256";
            if (!string.Equals(algorithm, "ES256", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported client assertion signing algorithm '{algorithm}'.");
            }

            formFields["client_assertion_type"] = ClientAssertionType;
            formFields["client_assertion"] = CreateEs256ClientAssertion(
                clientId,
                discovery.AuthorizationServer,
                context.GetRequiredString("private_key_pem"));
        }
        else
        {
            var credentials = $"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(context.GetRequiredString("client_secret"))}";
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }

        request.Content = new FormUrlEncodedContent(formFields);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAccessTokenAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> AcquireEnterpriseTokenAsync(
        Uri resourceUri,
        ConformanceContext context,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var discovery = await DiscoverAsync(httpClient, resourceUri, cancellationToken).ConfigureAwait(false);
        var provider = new IdentityAssertionGrantProvider(
            new IdentityAssertionGrantProviderOptions
            {
                ClientId = context.GetRequiredString("client_id"),
                ClientSecret = context.GetRequiredString("client_secret"),
                TokenEndpointAuthMethod = "client_secret_basic",
                IdpClientId = context.GetRequiredString("idp_client_id"),
                IdpTokenEndpoint = context.GetRequiredString("idp_token_endpoint"),
                IdTokenCallback = (_, _) => Task.FromResult(context.GetRequiredString("idp_id_token")),
            },
            httpClient,
            loggerFactory);

        var tokens = await provider.GetAccessTokenAsync(
            resourceUri,
            new Uri(discovery.AuthorizationServer),
            cancellationToken).ConfigureAwait(false);
        return tokens.AccessToken;
    }

    public static HttpClient CreateBearerHttpClient(string accessToken)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return httpClient;
    }

    private static async Task<OAuthDiscovery> DiscoverAsync(
        HttpClient httpClient,
        Uri resourceUri,
        CancellationToken cancellationToken)
    {
        JsonElement? protectedResourceMetadata = null;
        foreach (var metadataUri in GetProtectedResourceMetadataUris(resourceUri))
        {
            using var response = await httpClient.GetAsync(metadataUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            protectedResourceMetadata = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            break;
        }

        if (protectedResourceMetadata is not { } prm ||
            !prm.TryGetProperty("authorization_servers", out var authorizationServers) ||
            authorizationServers.ValueKind != JsonValueKind.Array ||
            authorizationServers.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"No authorization server was discovered for MCP resource '{resourceUri}'.");
        }

        var authorizationServer = authorizationServers[0].GetString()
            ?? throw new InvalidOperationException("The discovered authorization server URI was null.");
        var resource = prm.TryGetProperty("resource", out var resourceProperty)
            ? resourceProperty.GetString() ?? resourceUri.ToString()
            : resourceUri.ToString();
        List<string> scopes = [];
        if (prm.TryGetProperty("scopes_supported", out var scopesProperty) &&
            scopesProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var scope in scopesProperty.EnumerateArray())
            {
                if (scope.GetString() is { Length: > 0 } value)
                {
                    scopes.Add(value);
                }
            }
        }

        var authorizationServerUri = new Uri(authorizationServer);
        foreach (var metadataUri in GetAuthorizationServerMetadataUris(authorizationServerUri))
        {
            using var response = await httpClient.GetAsync(metadataUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var metadata = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            if (metadata.TryGetProperty("token_endpoint", out var tokenEndpointProperty) &&
                tokenEndpointProperty.GetString() is { Length: > 0 } tokenEndpoint)
            {
                var issuer = metadata.TryGetProperty("issuer", out var issuerProperty)
                    ? issuerProperty.GetString() ?? authorizationServer
                    : authorizationServer;
                return new OAuthDiscovery(resource, issuer, new Uri(tokenEndpoint), scopes);
            }
        }

        throw new InvalidOperationException(
            $"No authorization server metadata with a token endpoint was discovered for '{authorizationServer}'.");
    }

    private static IEnumerable<Uri> GetProtectedResourceMetadataUris(Uri resourceUri)
    {
        var authority = resourceUri.GetLeftPart(UriPartial.Authority);
        var path = resourceUri.AbsolutePath.Trim('/');
        if (path.Length > 0)
        {
            yield return new Uri($"{authority}/.well-known/oauth-protected-resource/{path}");
        }
        yield return new Uri($"{authority}/.well-known/oauth-protected-resource");
    }

    private static IEnumerable<Uri> GetAuthorizationServerMetadataUris(Uri authorizationServer)
    {
        var authority = authorizationServer.GetLeftPart(UriPartial.Authority);
        var path = authorizationServer.AbsolutePath.Trim('/');
        if (path.Length == 0)
        {
            yield return new Uri($"{authority}/.well-known/oauth-authorization-server");
            yield return new Uri($"{authority}/.well-known/openid-configuration");
        }
        else
        {
            yield return new Uri($"{authority}/.well-known/oauth-authorization-server/{path}");
            yield return new Uri($"{authority}/.well-known/openid-configuration/{path}");
            yield return new Uri($"{authority}/{path}/.well-known/openid-configuration");
        }
    }

    private static string CreateEs256ClientAssertion(string clientId, string audience, string privateKeyPem)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"ES256","typ":"JWT"}"""));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var payloadStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payloadStream))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", clientId);
            writer.WriteString("sub", clientId);
            writer.WriteString("aud", audience);
            writer.WriteNumber("iat", now);
            writer.WriteNumber("exp", now + 300);
            writer.WriteString("jti", Guid.NewGuid());
            writer.WriteEndObject();
        }

        var payload = Base64UrlEncode(payloadStream.ToArray());
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        var signature = key.SignData(
            signingInput,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64UrlEncode(signature)}";
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadAccessTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Client credentials token request failed with status {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("access_token", out var accessToken) ||
            accessToken.GetString() is not { Length: > 0 } value)
        {
            throw new InvalidOperationException("The token response did not contain an access_token.");
        }

        return value;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record OAuthDiscovery(
        string Resource,
        string AuthorizationServer,
        Uri TokenEndpoint,
        IReadOnlyList<string> Scopes);
}
