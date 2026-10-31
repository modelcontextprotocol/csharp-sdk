using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Authentication;

internal sealed partial class McpOAuthProxyService
{
    private const int MaxRequestBodySize = 32 * 1024;
    private const string BrowserBindingCookieName = "mcp-oauth-proxy-binding";
    private readonly McpOAuthProxyOptions _options;
    private readonly McpOAuthProxyProtectedStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<McpOAuthProxyService> _logger;
    private readonly SemaphoreSlim _metadataLock = new(1, 1);
    private OAuthProxyUpstreamMetadata? _upstreamMetadata;

    public McpOAuthProxyService(
        IOptions<McpOAuthProxyOptions> options,
        McpOAuthProxyProtectedStore store,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<McpOAuthProxyService> logger)
    {
        _options = options.Value;
        _store = store;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IResult HandleMetadataRequest()
    {
        var endpointBase = _options.Issuer;
        var metadata = new OAuthProxyAuthorizationServerMetadata
        {
            Issuer = _options.Issuer.AbsoluteUri.TrimEnd('/'),
            AuthorizationEndpoint = McpOAuthProxyUtilities.AppendPath(endpointBase, "authorize").AbsoluteUri,
            TokenEndpoint = McpOAuthProxyUtilities.AppendPath(endpointBase, "token").AbsoluteUri,
            RegistrationEndpoint = McpOAuthProxyUtilities.AppendPath(endpointBase, "register").AbsoluteUri,
            JwksUri = _options.JwksUri?.AbsoluteUri,
            ScopesSupported = _options.AllowedScopes.Order(StringComparer.Ordinal).ToArray(),
        };

        return Results.Json(metadata, McpOAuthProxyJsonContext.Default.OAuthProxyAuthorizationServerMetadata);
    }

    public async Task<IResult> HandleRegistrationAsync(HttpContext context)
    {
        OAuthProxyClientRegistrationRequest? request;
        try
        {
            var body = await ReadRequestBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            request = JsonSerializer.Deserialize(body, McpOAuthProxyJsonContext.Default.OAuthProxyClientRegistrationRequest);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_metadata", "The registration document is not valid JSON.");
        }

        if (request?.RedirectUris is not { Length: > 0 } redirectUris || redirectUris.Length > 20)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_redirect_uri", "At least one and no more than 20 redirect URIs are required.");
        }

        if (redirectUris.Any(static uri => !McpOAuthProxyUtilities.IsValidRedirectUri(uri)) ||
            redirectUris.Distinct(StringComparer.Ordinal).Count() != redirectUris.Length)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_redirect_uri", "Every redirect URI must be a unique HTTPS URI or HTTP loopback URI without user information or a fragment.");
        }

        if (request.TokenEndpointAuthMethod is not null and not "none" and not "client_secret_post")
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_metadata", "Only public clients are supported.");
        }

        var grantTypes = request.GrantTypes ?? ["authorization_code"];
        if (!grantTypes.Contains("authorization_code", StringComparer.Ordinal) ||
            grantTypes.Any(static value => value is not "authorization_code" and not "refresh_token"))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_metadata", "Only authorization_code and refresh_token grants are supported.");
        }

        var responseTypes = request.ResponseTypes ?? ["code"];
        if (responseTypes.Length != 1 || responseTypes[0] != "code")
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_metadata", "Only the code response type is supported.");
        }

        if (request.ApplicationType is not null and not "native" and not "web")
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_metadata", "application_type must be 'native' or 'web'.");
        }

        var scopes = McpOAuthProxyUtilities.ParseScopes(request.Scope);
        if (scopes.Length > 32 || !AreScopesAllowed(scopes))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_metadata", "The registration requests an unsupported scope.");
        }

        if (request.ClientName?.Length > 200 ||
            request.ClientUri?.Length > 2048 ||
            (request.ClientUri is not null &&
                (!Uri.TryCreate(request.ClientUri, UriKind.Absolute, out var clientUri) ||
                 !McpOAuthProxyUtilities.IsSecureEndpoint(clientUri))))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_client_metadata", "The client metadata is invalid.");
        }

        bool registrationAllowed;
        try
        {
            registrationAllowed = await _options.ClientRegistrationValidator(
                new McpOAuthProxyClientRegistrationContext
                {
                    HttpContext = context,
                    RedirectUris = redirectUris,
                    Scopes = scopes,
                    GrantTypes = grantTypes,
                    ClientName = request.ClientName,
                    ClientUri = request.ClientUri,
                    ApplicationType = request.ApplicationType,
                },
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRegistrationPolicyFailed(_logger, exception);
            return Error(StatusCodes.Status500InternalServerError, "server_error", "The registration policy could not evaluate the client.");
        }

        if (!registrationAllowed)
        {
            return Error(StatusCodes.Status403Forbidden, "access_denied", "The dynamic client registration was not approved.");
        }

        var clientId = $"mcp_{McpOAuthProxyUtilities.CreateRandomToken()}";
        await _store.SetAsync(
            "client",
            clientId,
            new OAuthProxyClientRecord
            {
                ClientId = clientId,
                RedirectUris = redirectUris,
                Scopes = scopes,
                ClientName = request.ClientName,
                ClientUri = request.ClientUri,
                ApplicationType = request.ApplicationType,
                SupportsRefreshTokens = grantTypes.Contains("refresh_token", StringComparer.Ordinal),
            },
            McpOAuthProxyJsonContext.Default.OAuthProxyClientRecord,
            _options.ClientRegistrationLifetime,
            context.RequestAborted).ConfigureAwait(false);

        var response = new OAuthProxyClientRegistrationResponse
        {
            ClientId = clientId,
            ClientIdIssuedAt = _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            RedirectUris = redirectUris,
            GrantTypes = grantTypes,
            ResponseTypes = responseTypes,
            ClientName = request.ClientName,
            ClientUri = request.ClientUri,
            Scope = scopes.Length == 0 ? null : string.Join(' ', scopes),
            ApplicationType = request.ApplicationType,
        };

        return Results.Json(
            response,
            McpOAuthProxyJsonContext.Default.OAuthProxyClientRegistrationResponse,
            statusCode: StatusCodes.Status201Created);
    }

    public async Task<IResult> HandleAuthorizationAsync(HttpContext context)
    {
        var query = context.Request.Query;
        var clientId = query["client_id"].ToString();
        var redirectUri = query["redirect_uri"].ToString();

        if (!McpOAuthProxyUtilities.IsValidClientId(clientId) || string.IsNullOrEmpty(redirectUri))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "client_id and redirect_uri are required.");
        }

        var client = await _store.GetAsync(
            "client",
            clientId,
            McpOAuthProxyJsonContext.Default.OAuthProxyClientRecord,
            context.RequestAborted).ConfigureAwait(false);

        if (client is null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The client is not registered.");
        }

        if (!client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "redirect_uri does not match the registered client.");
        }

        if (query["response_type"].ToString() != "code")
        {
            return RedirectError(redirectUri, query["state"], "unsupported_response_type", "Only the code response type is supported.");
        }

        var codeChallenge = query["code_challenge"].ToString();
        if (query["code_challenge_method"].ToString() != "S256" ||
            !McpOAuthProxyUtilities.IsValidCodeChallenge(codeChallenge))
        {
            return RedirectError(redirectUri, query["state"], "invalid_request", "A valid S256 PKCE challenge is required.");
        }

        var scopes = string.IsNullOrWhiteSpace(query["scope"])
            ? client.Scopes
            : McpOAuthProxyUtilities.ParseScopes(query["scope"]);
        if (!AreScopesAllowed(scopes) || scopes.Any(scope => !client.Scopes.Contains(scope, StringComparer.Ordinal)))
        {
            return RedirectError(redirectUri, query["state"], "invalid_scope", "The request includes a scope that was not registered.");
        }

        var resource = query["resource"].ToString();
        if (!string.IsNullOrEmpty(resource) && !_options.AllowedResources.Contains(resource))
        {
            return RedirectError(redirectUri, query["state"], "invalid_target", "The requested resource is not allowed by the proxy.");
        }

        bool authorizationAllowed;
        try
        {
            authorizationAllowed = await _options.AuthorizationValidator(
                new McpOAuthProxyAuthorizationContext
                {
                    HttpContext = context,
                    ClientId = clientId,
                    ClientName = client.ClientName,
                    RedirectUri = redirectUri,
                    Scopes = scopes,
                    Resource = string.IsNullOrEmpty(resource) ? null : resource,
                },
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuthorizationPolicyFailed(_logger, exception);
            return RedirectError(redirectUri, query["state"], "server_error", "The authorization policy could not evaluate the request.");
        }

        if (!authorizationAllowed)
        {
            return RedirectError(redirectUri, query["state"], "access_denied", "The authorization request was not approved.");
        }

        OAuthProxyUpstreamMetadata upstreamMetadata;
        try
        {
            upstreamMetadata = await GetUpstreamMetadataAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            LogDiscoveryFailed(_logger, exception);
            return RedirectError(redirectUri, query["state"], "server_error", "The upstream authorization server is unavailable.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            LogDiscoveryFailed(_logger, exception);
            return RedirectError(redirectUri, query["state"], "server_error", "The upstream authorization server is unavailable.");
        }

        var transactionId = McpOAuthProxyUtilities.CreateRandomToken();
        var browserBinding = McpOAuthProxyUtilities.CreateRandomToken();
        var upstreamCodeVerifier = McpOAuthProxyUtilities.CreateRandomToken(48);
        await _store.SetAsync(
            "transaction",
            transactionId,
            new OAuthProxyAuthorizationTransaction
            {
                ClientId = clientId,
                RedirectUri = redirectUri,
                CodeChallenge = codeChallenge,
                UpstreamCodeVerifier = upstreamCodeVerifier,
                BrowserBinding = browserBinding,
                Scopes = scopes,
                ClientState = query["state"],
                Resource = string.IsNullOrEmpty(resource) ? null : resource,
                SupportsRefreshTokens = client.SupportsRefreshTokens,
            },
            McpOAuthProxyJsonContext.Default.OAuthProxyAuthorizationTransaction,
            _options.AuthorizationTransactionLifetime,
            context.RequestAborted).ConfigureAwait(false);

        context.Response.Cookies.Append(
            GetBrowserBindingCookieName(transactionId),
            browserBinding,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = _options.CookieSecurePolicy is CookieSecurePolicy.Always ||
                    (_options.CookieSecurePolicy is CookieSecurePolicy.SameAsRequest && context.Request.IsHttps),
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                IsEssential = true,
                Path = GetCallbackPath(),
                MaxAge = _options.AuthorizationTransactionLifetime,
            });

        Dictionary<string, string?> parameters = new(StringComparer.Ordinal)
        {
            ["client_id"] = _options.UpstreamClientId,
            ["redirect_uri"] = _options.UpstreamRedirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["code_challenge"] = McpOAuthProxyUtilities.CreateCodeChallenge(upstreamCodeVerifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = scopes.Length == 0 ? null : string.Join(' ', scopes),
            ["state"] = transactionId,
        };

        if (_options.ForwardResourceIndicator && !string.IsNullOrEmpty(resource))
        {
            parameters["resource"] = resource;
        }

        foreach (var parameter in _options.AdditionalAuthorizationParameters)
        {
            parameters[parameter.Key] = parameter.Value;
        }

        return Results.Redirect(QueryHelpers.AddQueryString(upstreamMetadata.AuthorizationEndpoint!, parameters));
    }

    public async Task<IResult> HandleCallbackAsync(HttpContext context)
    {
        var state = context.Request.Query["state"].ToString();
        if (!McpOAuthProxyUtilities.IsValidOpaqueToken(state))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "state is required.");
        }

        var transaction = await _store.GetAsync(
            "transaction",
            state,
            McpOAuthProxyJsonContext.Default.OAuthProxyAuthorizationTransaction,
            context.RequestAborted).ConfigureAwait(false);

        if (transaction is null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The authorization transaction is invalid or expired.");
        }

        var browserBindingCookieName = GetBrowserBindingCookieName(state);
        if (!context.Request.Cookies.TryGetValue(browserBindingCookieName, out var browserBinding) ||
            !McpOAuthProxyUtilities.FixedTimeEquals(transaction.BrowserBinding, browserBinding))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The authorization response is not bound to the initiating browser.");
        }

        transaction = await _store.TakeAsync(
            "transaction",
            state,
            McpOAuthProxyJsonContext.Default.OAuthProxyAuthorizationTransaction,
            context.RequestAborted).ConfigureAwait(false);
        if (transaction is null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The authorization transaction was already consumed.");
        }

        context.Response.Cookies.Delete(browserBindingCookieName, new CookieOptions { Path = GetCallbackPath() });

        var upstreamError = context.Request.Query["error"].ToString();
        if (!string.IsNullOrEmpty(upstreamError))
        {
            return RedirectError(transaction.RedirectUri, transaction.ClientState, upstreamError, "The upstream authorization request was rejected.");
        }

        var code = context.Request.Query["code"].ToString();
        if (string.IsNullOrEmpty(code))
        {
            return RedirectError(transaction.RedirectUri, transaction.ClientState, "invalid_request", "The upstream authorization response did not include a code.");
        }

        var exchange = await ExchangeAuthorizationCodeAsync(
            code,
            transaction.UpstreamCodeVerifier,
            transaction.Resource,
            context.RequestAborted).ConfigureAwait(false);
        if (exchange.Token is null)
        {
            return RedirectError(transaction.RedirectUri, transaction.ClientState, "server_error", "The upstream token exchange failed.");
        }

        var grantedScopes = ResolveGrantedScopes(transaction.Scopes, exchange.Token.Scope);
        if (grantedScopes is null)
        {
            return RedirectError(transaction.RedirectUri, transaction.ClientState, "server_error", "The upstream authorization server returned an invalid scope grant.");
        }

        var proxyCode = McpOAuthProxyUtilities.CreateRandomToken();
        await _store.SetAsync(
            "code",
            proxyCode,
            new OAuthProxyAuthorizationCode
            {
                ClientId = transaction.ClientId,
                RedirectUri = transaction.RedirectUri,
                CodeChallenge = transaction.CodeChallenge,
                Scopes = grantedScopes,
                Resource = transaction.Resource,
                SupportsRefreshTokens = transaction.SupportsRefreshTokens,
                UpstreamToken = exchange.Token,
            },
            McpOAuthProxyJsonContext.Default.OAuthProxyAuthorizationCode,
            _options.AuthorizationCodeLifetime,
            context.RequestAborted).ConfigureAwait(false);

        Dictionary<string, string?> parameters = new(StringComparer.Ordinal)
        {
            ["code"] = proxyCode,
            ["state"] = transaction.ClientState,
            ["iss"] = _options.Issuer.AbsoluteUri.TrimEnd('/'),
        };
        return Results.Redirect(QueryHelpers.AddQueryString(transaction.RedirectUri, parameters));
    }

    public async Task<IResult> HandleTokenAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The token request must use form encoding.");
        }

        Dictionary<string, string> form;
        try
        {
            var body = await ReadRequestBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            var parsed = QueryHelpers.ParseQuery(Encoding.UTF8.GetString(body));
            if (parsed.Any(static field => field.Value.Count != 1))
            {
                return Error(StatusCodes.Status400BadRequest, "invalid_request", "Token request parameters must occur exactly once.");
            }

            form = parsed.ToDictionary(
                static field => field.Key,
                static field => field.Value.ToString(),
                StringComparer.Ordinal);
        }
        catch (InvalidDataException)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The token request body is too large.");
        }

        var grantType = form.GetValueOrDefault("grant_type") ?? string.Empty;
        return grantType switch
        {
            "authorization_code" => await ExchangeProxyAuthorizationCodeAsync(context, form).ConfigureAwait(false),
            "refresh_token" => await ExchangeProxyRefreshTokenAsync(context, form).ConfigureAwait(false),
            _ => Error(StatusCodes.Status400BadRequest, "unsupported_grant_type", "Only authorization_code and refresh_token grants are supported."),
        };
    }

    private async Task<IResult> ExchangeProxyAuthorizationCodeAsync(HttpContext context, IReadOnlyDictionary<string, string> form)
    {
        var code = form.GetValueOrDefault("code") ?? string.Empty;
        var clientId = form.GetValueOrDefault("client_id") ?? string.Empty;
        var redirectUri = form.GetValueOrDefault("redirect_uri") ?? string.Empty;
        var codeVerifier = form.GetValueOrDefault("code_verifier") ?? string.Empty;
        if (!McpOAuthProxyUtilities.IsValidOpaqueToken(code) ||
            !McpOAuthProxyUtilities.IsValidClientId(clientId) ||
            string.IsNullOrEmpty(redirectUri) ||
            !McpOAuthProxyUtilities.IsValidCodeVerifier(codeVerifier))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "code, client_id, redirect_uri, and code_verifier are required.");
        }

        var authorizationCode = await _store.GetAsync(
            "code",
            code,
            McpOAuthProxyJsonContext.Default.OAuthProxyAuthorizationCode,
            context.RequestAborted).ConfigureAwait(false);
        if (authorizationCode is null ||
            !McpOAuthProxyUtilities.FixedTimeEquals(authorizationCode.ClientId, clientId) ||
            !McpOAuthProxyUtilities.FixedTimeEquals(authorizationCode.RedirectUri, redirectUri) ||
            !McpOAuthProxyUtilities.FixedTimeEquals(
                authorizationCode.CodeChallenge,
                McpOAuthProxyUtilities.CreateCodeChallenge(codeVerifier)))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "The authorization code is invalid, expired, or does not match the request.");
        }


        authorizationCode = await _store.TakeAsync(
            "code",
            code,
            McpOAuthProxyJsonContext.Default.OAuthProxyAuthorizationCode,
            context.RequestAborted).ConfigureAwait(false);
        if (authorizationCode is null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "The authorization code was already consumed.");
        }

        var token = await MintTokenAsync(
            authorizationCode.ClientId,
            authorizationCode.Resource,
            authorizationCode.Scopes,
            authorizationCode.UpstreamToken,
            isRefresh: false,
            context.RequestAborted).ConfigureAwait(false);
        if (token is null)
        {
            return Error(StatusCodes.Status500InternalServerError, "server_error", "The proxy could not issue an access token.");
        }

        string? refreshToken = null;
        if (authorizationCode.SupportsRefreshTokens && !string.IsNullOrEmpty(authorizationCode.UpstreamToken.RefreshToken))
        {
            refreshToken = McpOAuthProxyUtilities.CreateRandomToken();
            var familyId = McpOAuthProxyUtilities.CreateRandomToken();
            var expiresAt = _timeProvider.GetUtcNow() + _options.RefreshTokenLifetime;
            await StoreRefreshRecordAsync(
                refreshToken,
                authorizationCode.ClientId,
                authorizationCode.Resource,
                authorizationCode.Scopes,
                authorizationCode.UpstreamToken,
                familyId,
                expiresAt,
                context.RequestAborted).ConfigureAwait(false);
            _ = await StoreRefreshFamilyAsync(familyId, refreshToken, expiresAt, context.RequestAborted).ConfigureAwait(false);
        }

        return TokenResponse(token, refreshToken, authorizationCode.Scopes);
    }

    private async Task<IResult> ExchangeProxyRefreshTokenAsync(HttpContext context, IReadOnlyDictionary<string, string> form)
    {
        var refreshToken = form.GetValueOrDefault("refresh_token") ?? string.Empty;
        var clientId = form.GetValueOrDefault("client_id") ?? string.Empty;
        if (!McpOAuthProxyUtilities.IsValidOpaqueToken(refreshToken) ||
            !McpOAuthProxyUtilities.IsValidClientId(clientId))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "refresh_token and client_id are required.");
        }

        var refreshRecord = await _store.GetAsync(
            "refresh",
            refreshToken,
            McpOAuthProxyJsonContext.Default.OAuthProxyRefreshRecord,
            context.RequestAborted).ConfigureAwait(false);
        if (refreshRecord is null)
        {
            await RevokeRefreshFamilyOnReuseAsync(refreshToken, context.RequestAborted).ConfigureAwait(false);
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "The refresh token is invalid or expired.");
        }

        if (!McpOAuthProxyUtilities.FixedTimeEquals(refreshRecord.ClientId, clientId) ||
            string.IsNullOrEmpty(refreshRecord.UpstreamToken.RefreshToken))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "The refresh token is invalid or expired.");
        }

        var remainingLifetime = refreshRecord.ExpiresAt - _timeProvider.GetUtcNow();
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "The refresh token is expired.");
        }

        await _store.SetAsync(
            "consumed-refresh",
            refreshToken,
            new OAuthProxyConsumedRefreshRecord
            {
                FamilyId = refreshRecord.FamilyId,
                ExpiresAt = refreshRecord.ExpiresAt,
            },
            McpOAuthProxyJsonContext.Default.OAuthProxyConsumedRefreshRecord,
            remainingLifetime,
            context.RequestAborted).ConfigureAwait(false);

        refreshRecord = await _store.TakeAsync(
            "refresh",
            refreshToken,
            McpOAuthProxyJsonContext.Default.OAuthProxyRefreshRecord,
            context.RequestAborted).ConfigureAwait(false);
        if (refreshRecord is null)
        {
            await RevokeRefreshFamilyOnReuseAsync(refreshToken, context.RequestAborted).ConfigureAwait(false);
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "The refresh token was already consumed.");
        }

        var family = await _store.TakeAsync(
            "refresh-family",
            refreshRecord.FamilyId,
            McpOAuthProxyJsonContext.Default.OAuthProxyRefreshFamilyRecord,
            context.RequestAborted).ConfigureAwait(false);
        if (family is null || !McpOAuthProxyUtilities.FixedTimeEquals(family.CurrentRefreshToken, refreshToken))
        {
            if (family is not null)
            {
                await _store.RemoveAsync("refresh", family.CurrentRefreshToken, context.RequestAborted).ConfigureAwait(false);
            }

            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "Refresh-token reuse was detected and the token family was revoked.");
        }

        var requestedScope = form.GetValueOrDefault("scope") ?? string.Empty;
        var requestedScopes = string.IsNullOrWhiteSpace(requestedScope)
            ? refreshRecord.Scopes
            : McpOAuthProxyUtilities.ParseScopes(requestedScope);
        if (requestedScopes.Length > 32 ||
            requestedScopes.Any(scope => !refreshRecord.Scopes.Contains(scope, StringComparer.Ordinal)))
        {
            await RestoreRefreshFamilyAsync(refreshToken, refreshRecord, context.RequestAborted).ConfigureAwait(false);
            return Error(StatusCodes.Status400BadRequest, "invalid_scope", "A refresh request cannot expand the original scope grant.");
        }

        var exchange = await ExchangeRefreshTokenAsync(
            refreshRecord.UpstreamToken.RefreshToken!,
            refreshRecord.Resource,
            requestedScopes,
            context.RequestAborted).ConfigureAwait(false);
        if (exchange.Token is null)
        {
            if (exchange.IsTransient)
            {
                await RestoreRefreshFamilyAsync(refreshToken, refreshRecord, context.RequestAborted).ConfigureAwait(false);
            }

            return Error(
                exchange.IsTransient ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest,
                exchange.IsTransient ? "temporarily_unavailable" : "invalid_grant",
                "The upstream token refresh failed.");
        }

        exchange.Token.RefreshToken ??= refreshRecord.UpstreamToken.RefreshToken;
        var grantedScopes = ResolveGrantedScopes(requestedScopes, exchange.Token.Scope);
        if (grantedScopes is null)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "The upstream authorization server returned an invalid scope grant.");
        }

        var token = await MintTokenAsync(
            refreshRecord.ClientId,
            refreshRecord.Resource,
            grantedScopes,
            exchange.Token,
            isRefresh: true,
            context.RequestAborted).ConfigureAwait(false);
        if (token is null)
        {
            refreshRecord.Scopes = grantedScopes;
            refreshRecord.UpstreamToken = exchange.Token;
            await RestoreRefreshFamilyAsync(refreshToken, refreshRecord, context.RequestAborted).ConfigureAwait(false);
            return Error(StatusCodes.Status500InternalServerError, "server_error", "The proxy could not issue an access token.");
        }

        var rotatedRefreshToken = McpOAuthProxyUtilities.CreateRandomToken();
        await StoreRefreshRecordAsync(
            rotatedRefreshToken,
            refreshRecord.ClientId,
            refreshRecord.Resource,
            grantedScopes,
            exchange.Token,
            refreshRecord.FamilyId,
            refreshRecord.ExpiresAt,
            context.RequestAborted).ConfigureAwait(false);
        var familyPublished = await StoreRefreshFamilyAsync(
            refreshRecord.FamilyId,
            rotatedRefreshToken,
            refreshRecord.ExpiresAt,
            context.RequestAborted).ConfigureAwait(false);
        if (!familyPublished)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_grant", "Refresh-token reuse was detected and the token family was revoked.");
        }

        return TokenResponse(token, rotatedRefreshToken, grantedScopes);
    }

    private async Task StoreRefreshRecordAsync(
        string refreshToken,
        string clientId,
        string? resource,
        string[] scopes,
        OAuthProxyUpstreamTokenResponse upstreamToken,
        string familyId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var remainingLifetime = expiresAt - _timeProvider.GetUtcNow();
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return;
        }

        await _store.SetAsync(
            "refresh",
            refreshToken,
            new OAuthProxyRefreshRecord
            {
                ClientId = clientId,
                Resource = resource,
                Scopes = scopes,
                UpstreamToken = upstreamToken,
                FamilyId = familyId,
                ExpiresAt = expiresAt,
            },
            McpOAuthProxyJsonContext.Default.OAuthProxyRefreshRecord,
            remainingLifetime,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> StoreRefreshFamilyAsync(
        string familyId,
        string currentRefreshToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var remainingLifetime = expiresAt - _timeProvider.GetUtcNow();
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return false;
        }

        await _store.SetAsync(
            "refresh-family",
            familyId,
            new OAuthProxyRefreshFamilyRecord
            {
                CurrentRefreshToken = currentRefreshToken,
                ExpiresAt = expiresAt,
            },
            McpOAuthProxyJsonContext.Default.OAuthProxyRefreshFamilyRecord,
            remainingLifetime,
            cancellationToken).ConfigureAwait(false);

        var revoked = await _store.GetAsync(
            "revoked-refresh-family",
            familyId,
            McpOAuthProxyJsonContext.Default.OAuthProxyConsumedRefreshRecord,
            cancellationToken).ConfigureAwait(false);
        if (revoked is null)
        {
            return true;
        }

        var publishedFamily = await _store.TakeAsync(
            "refresh-family",
            familyId,
            McpOAuthProxyJsonContext.Default.OAuthProxyRefreshFamilyRecord,
            cancellationToken).ConfigureAwait(false);
        if (publishedFamily is not null)
        {
            await _store.RemoveAsync("refresh", publishedFamily.CurrentRefreshToken, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task RestoreRefreshFamilyAsync(
        string refreshToken,
        OAuthProxyRefreshRecord refreshRecord,
        CancellationToken cancellationToken)
    {
        await StoreRefreshRecordAsync(
            refreshToken,
            refreshRecord.ClientId,
            refreshRecord.Resource,
            refreshRecord.Scopes,
            refreshRecord.UpstreamToken,
            refreshRecord.FamilyId,
            refreshRecord.ExpiresAt,
            cancellationToken).ConfigureAwait(false);
        _ = await StoreRefreshFamilyAsync(
            refreshRecord.FamilyId,
            refreshToken,
            refreshRecord.ExpiresAt,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RevokeRefreshFamilyOnReuseAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var consumed = await _store.GetAsync(
            "consumed-refresh",
            refreshToken,
            McpOAuthProxyJsonContext.Default.OAuthProxyConsumedRefreshRecord,
            cancellationToken).ConfigureAwait(false);
        if (consumed is null)
        {
            return;
        }

        var remainingLifetime = consumed.ExpiresAt - _timeProvider.GetUtcNow();
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return;
        }

        await _store.SetAsync(
            "revoked-refresh-family",
            consumed.FamilyId,
            consumed,
            McpOAuthProxyJsonContext.Default.OAuthProxyConsumedRefreshRecord,
            remainingLifetime,
            cancellationToken).ConfigureAwait(false);

        var family = await _store.TakeAsync(
            "refresh-family",
            consumed.FamilyId,
            McpOAuthProxyJsonContext.Default.OAuthProxyRefreshFamilyRecord,
            cancellationToken).ConfigureAwait(false);
        if (family is not null)
        {
            await _store.RemoveAsync("refresh", family.CurrentRefreshToken, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<McpOAuthProxyTokenResult?> MintTokenAsync(
        string clientId,
        string? resource,
        string[] scopes,
        OAuthProxyUpstreamTokenResponse upstreamToken,
        bool isRefresh,
        CancellationToken cancellationToken)
    {
        McpOAuthProxyTokenResult token;
        try
        {
            token = await _options.TokenFactory(
                new McpOAuthProxyTokenContext
                {
                    ClientId = clientId,
                    Resource = resource,
                    Scopes = scopes,
                    UpstreamAccessToken = upstreamToken.AccessToken!,
                    UpstreamIdToken = upstreamToken.IdToken,
                    UpstreamTokenType = upstreamToken.TokenType,
                    UpstreamExpiresIn = upstreamToken.ExpiresIn is long expiresIn ? TimeSpan.FromSeconds(expiresIn) : null,
                    IsRefresh = isRefresh,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogTokenFactoryFailed(_logger, exception);
            return null;
        }

        if (string.IsNullOrWhiteSpace(token.AccessToken) ||
            string.IsNullOrWhiteSpace(token.Subject) ||
            string.IsNullOrWhiteSpace(token.Audience) ||
            string.IsNullOrWhiteSpace(token.TokenId) ||
            !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            token.ExpiresIn < TimeSpan.FromSeconds(1) ||
            token.ExpiresIn > _options.MaximumAccessTokenLifetime ||
            (!string.IsNullOrEmpty(resource) && !string.Equals(token.Audience, resource, StringComparison.Ordinal)) ||
            IsUpstreamToken(token.AccessToken, upstreamToken))
        {
            LogInvalidTokenFactoryResult(_logger);
            return null;
        }

        LogTokenMinted(
            _logger,
            token.Subject,
            token.Audience,
            string.Join(' ', scopes),
            clientId,
            token.TokenId,
            _timeProvider.GetUtcNow());
        return token;
    }

    private IResult TokenResponse(McpOAuthProxyTokenResult token, string? refreshToken, string[] scopes) =>
        new NoStoreResult(Results.Json(
            new OAuthProxyTokenResponse
            {
                AccessToken = token.AccessToken,
                TokenType = token.TokenType,
                ExpiresIn = checked((long)token.ExpiresIn.TotalSeconds),
                RefreshToken = refreshToken,
                Scope = scopes.Length == 0 ? null : string.Join(' ', scopes),
            },
            McpOAuthProxyJsonContext.Default.OAuthProxyTokenResponse));

    private async Task<UpstreamExchangeResult> ExchangeAuthorizationCodeAsync(
        string code,
        string codeVerifier,
        string? resource,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.UpstreamRedirectUri.AbsoluteUri,
            ["code_verifier"] = codeVerifier,
        };
        if (_options.ForwardResourceIndicator && !string.IsNullOrEmpty(resource))
        {
            parameters["resource"] = resource;
        }

        return await ExchangeUpstreamTokenAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpstreamExchangeResult> ExchangeRefreshTokenAsync(
        string refreshToken,
        string? resource,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        if (scopes.Length > 0)
        {
            parameters["scope"] = string.Join(' ', scopes);
        }
        if (_options.ForwardResourceIndicator && !string.IsNullOrEmpty(resource))
        {
            parameters["resource"] = resource;
        }

        return await ExchangeUpstreamTokenAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpstreamExchangeResult> ExchangeUpstreamTokenAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        OAuthProxyUpstreamMetadata metadata;
        try
        {
            metadata = await GetUpstreamMetadataAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogDiscoveryFailed(_logger, exception);
            return new(null, IsTransient: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            LogDiscoveryFailed(_logger, exception);
            return new(null, IsTransient: true);
        }
        if (_options.UpstreamClientAuthenticationMethod is not McpOAuthProxyClientAuthenticationMethod.ClientSecretBasic)
        {
            parameters["client_id"] = _options.UpstreamClientId;
        }

        if (_options.UpstreamClientAuthenticationMethod is McpOAuthProxyClientAuthenticationMethod.ClientSecretPost)
        {
            parameters["client_secret"] = _options.UpstreamClientSecret!;
        }

        foreach (var parameter in _options.AdditionalTokenParameters)
        {
            parameters[parameter.Key] = parameter.Value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, metadata.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_options.UpstreamClientAuthenticationMethod is McpOAuthProxyClientAuthenticationMethod.ClientSecretBasic)
        {
            var encodedClient = WebUtility.UrlEncode(_options.UpstreamClientId);
            var encodedSecret = WebUtility.UrlEncode(_options.UpstreamClientSecret!);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{encodedClient}:{encodedSecret}")));
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClientFactory.CreateClient(McpOAuthProxyServiceCollectionExtensions.HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUpstreamTokenRequestException(_logger, exception);
            return new(null, IsTransient: true);
        }
        catch (HttpRequestException exception)
        {
            LogUpstreamTokenRequestException(_logger, exception);
            return new(null, IsTransient: true);
        }

        using (response)
        {
            OAuthProxyUpstreamTokenResponse? token;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                token = await JsonSerializer.DeserializeAsync(
                    stream,
                    McpOAuthProxyJsonContext.Default.OAuthProxyUpstreamTokenResponse,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogInvalidUpstreamTokenResponse(_logger, response.StatusCode, exception);
                return new(null, IsTransient: true);
            }
            catch (Exception exception) when (exception is JsonException or HttpRequestException)
            {
                LogInvalidUpstreamTokenResponse(_logger, response.StatusCode, exception);
                return new(
                    null,
                    IsTransient: IsTransientUpstreamStatus(response.StatusCode));
            }

            if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(token?.AccessToken))
            {
                LogUpstreamTokenRequestFailed(_logger, response.StatusCode, token?.Error);
                return new(null, IsTransient: IsTransientUpstreamStatus(response.StatusCode));
            }

            return new(token, IsTransient: false);
        }
    }

    private async Task<OAuthProxyUpstreamMetadata> GetUpstreamMetadataAsync(CancellationToken cancellationToken)
    {
        if (_upstreamMetadata is not null)
        {
            return _upstreamMetadata;
        }

        await _metadataLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_upstreamMetadata is not null)
            {
                return _upstreamMetadata;
            }

            if (_options.UpstreamAuthorizationEndpoint is not null && _options.UpstreamTokenEndpoint is not null)
            {
                return _upstreamMetadata = new OAuthProxyUpstreamMetadata
                {
                    Issuer = _options.UpstreamIssuer?.AbsoluteUri,
                    AuthorizationEndpoint = _options.UpstreamAuthorizationEndpoint.AbsoluteUri,
                    TokenEndpoint = _options.UpstreamTokenEndpoint.AbsoluteUri,
                    CodeChallengeMethodsSupported = ["S256"],
                };
            }

            var discoveryUri = McpOAuthProxyUtilities.AppendPath(_options.UpstreamIssuer!, "/.well-known/openid-configuration");
            using var response = await _httpClientFactory.CreateClient(McpOAuthProxyServiceCollectionExtensions.HttpClientName)
                .GetAsync(discoveryUri, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var metadata = await JsonSerializer.DeserializeAsync(
                stream,
                McpOAuthProxyJsonContext.Default.OAuthProxyUpstreamMetadata,
                cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException("The upstream discovery document is empty.");

            if (!SameIssuer(metadata.Issuer, _options.UpstreamIssuer!) ||
                !Uri.TryCreate(metadata.AuthorizationEndpoint, UriKind.Absolute, out var authorizationEndpoint) ||
                !Uri.TryCreate(metadata.TokenEndpoint, UriKind.Absolute, out var tokenEndpoint) ||
                !McpOAuthProxyUtilities.IsSecureEndpoint(authorizationEndpoint) ||
                !McpOAuthProxyUtilities.IsSecureEndpoint(tokenEndpoint) ||
                metadata.CodeChallengeMethodsSupported?.Contains("S256", StringComparer.Ordinal) is not true)
            {
                throw new InvalidOperationException("The upstream discovery document has an invalid issuer, endpoint, or PKCE configuration.");
            }

            return _upstreamMetadata = metadata;
        }
        finally
        {
            _metadataLock.Release();
        }
    }

    private bool AreScopesAllowed(IEnumerable<string> scopes) =>
        scopes.All(_options.AllowedScopes.Contains);

    private static string[]? ResolveGrantedScopes(string[] requestedScopes, string? upstreamScope)
    {
        if (string.IsNullOrWhiteSpace(upstreamScope))
        {
            return requestedScopes;
        }

        var grantedScopes = McpOAuthProxyUtilities.ParseScopes(upstreamScope);
        return grantedScopes.All(scope => requestedScopes.Contains(scope, StringComparer.Ordinal))
            ? grantedScopes
            : null;
    }

    private static bool IsTransientUpstreamStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxRequestBodySize)
        {
            throw new InvalidDataException("The request body exceeds the configured limit.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var body = new MemoryStream();
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return body.ToArray();
                }

                if (body.Length + read > MaxRequestBodySize)
                {
                    throw new InvalidDataException("The request body exceeds the configured limit.");
                }

                body.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string GetBrowserBindingCookieName(string transactionId) =>
        $"{BrowserBindingCookieName}-{transactionId}";

    private string GetCallbackPath() =>
        $"{_options.Issuer.AbsolutePath.TrimEnd('/')}/callback";

    private static bool SameIssuer(string? discoveredIssuer, Uri configuredIssuer) =>
        !string.IsNullOrEmpty(discoveredIssuer) &&
        string.Equals(discoveredIssuer.TrimEnd('/'), configuredIssuer.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal);

    private static bool IsUpstreamToken(string candidate, OAuthProxyUpstreamTokenResponse upstreamToken) =>
        McpOAuthProxyUtilities.FixedTimeEquals(candidate, upstreamToken.AccessToken!) ||
        (!string.IsNullOrEmpty(upstreamToken.RefreshToken) && McpOAuthProxyUtilities.FixedTimeEquals(candidate, upstreamToken.RefreshToken)) ||
        (!string.IsNullOrEmpty(upstreamToken.IdToken) && McpOAuthProxyUtilities.FixedTimeEquals(candidate, upstreamToken.IdToken));

    private IResult RedirectError(
        string redirectUri,
        string? state,
        string error,
        string description)
    {
        Dictionary<string, string?> parameters = new(StringComparer.Ordinal)
        {
            ["error"] = error,
            ["error_description"] = description,
            ["state"] = state,
            ["iss"] = _options.Issuer.AbsoluteUri.TrimEnd('/'),
        };
        return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, parameters));
    }

    private static IResult Error(int statusCode, string error, string description) =>
        new NoStoreResult(Results.Json(
            new OAuthProxyErrorResponse { Error = error, ErrorDescription = description },
            McpOAuthProxyJsonContext.Default.OAuthProxyErrorResponse,
            statusCode: statusCode));

    private readonly record struct UpstreamExchangeResult(OAuthProxyUpstreamTokenResponse? Token, bool IsTransient);

    private sealed class NoStoreResult(IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            return inner.ExecuteAsync(httpContext);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "OAuth proxy upstream discovery failed.")]
    private static partial void LogDiscoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "OAuth proxy token factory failed.")]
    private static partial void LogTokenFactoryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "OAuth proxy client registration policy failed.")]
    private static partial void LogRegistrationPolicyFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "OAuth proxy authorization policy failed.")]
    private static partial void LogAuthorizationPolicyFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "OAuth proxy token factory returned an invalid or unsafe token result.")]
    private static partial void LogInvalidTokenFactoryResult(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "OAuth proxy minted token: subject={Subject}, audience={Audience}, scopes={Scopes}, client_id={ClientId}, jti={TokenId}, minted_at={MintedAt}.")]
    private static partial void LogTokenMinted(
        ILogger logger,
        string subject,
        string audience,
        string scopes,
        string clientId,
        string tokenId,
        DateTimeOffset mintedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OAuth proxy could not reach the upstream token endpoint.")]
    private static partial void LogUpstreamTokenRequestException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OAuth proxy received an invalid token response with status {StatusCode} from the upstream provider.")]
    private static partial void LogInvalidUpstreamTokenResponse(ILogger logger, HttpStatusCode statusCode, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OAuth proxy upstream token request failed with status {StatusCode} and error {Error}.")]
    private static partial void LogUpstreamTokenRequestFailed(ILogger logger, HttpStatusCode statusCode, string? error);
}
