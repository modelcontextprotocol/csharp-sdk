using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.TestOAuthServer;

public sealed class Program
{
    private const int _port = 7029;
    private static readonly string _url = $"https://localhost:{_port}";
    private static readonly string _clientMetadataDocumentUrl = $"{_url}/client-metadata/cimd-client.json";

    // Port 5000 is used by tests and port 7071 is used by the ProtectedMcpServer sample
    // Per MCP spec, URIs should not have trailing slashes unless semantically significant
    public string[] ValidResources { get; set; } = [
        "http://localhost:5000",
        "http://localhost:5000/mcp",
        "http://localhost:7071"
    ];

    private readonly ConcurrentDictionary<string, AuthorizationCodeInfo> _authCodes = new();
    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new();
    private readonly ConcurrentDictionary<string, ClientInfo> _clients = new();

    private readonly ConcurrentQueue<string> _metadataRequests = new();

    private readonly RSA _rsa;
    private readonly string _keyId;

    private readonly ILoggerProvider? _loggerProvider;
    private readonly IConnectionListenerFactory? _kestrelTransport;
    private readonly TaskCompletionSource _serverStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="Program"/> class with logging and transport parameters.
    /// </summary>
    /// <param name="loggerProvider">Optional logger provider for logging.</param>
    /// <param name="kestrelTransport">Optional Kestrel transport for in-memory connections.</param>
    public Program(ILoggerProvider? loggerProvider = null, IConnectionListenerFactory? kestrelTransport = null)
    {
        _rsa = RSA.Create(2048);
        _keyId = Guid.NewGuid().ToString();
        _loggerProvider = loggerProvider;
        _kestrelTransport = kestrelTransport;
    }

    /// <summary>
    /// Gets a task that completes when the server has started and is ready to accept connections.
    /// </summary>
    public Task ServerStarted => _serverStarted.Task;

    // Track if we've already issued an already-expired token for the CanAuthenticate_WithTokenRefresh test which uses the test-refresh-client registration.
    public bool HasRefreshedToken { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the authorization server
    /// advertises support for client ID metadata documents in its discovery
    /// document. This is used by tests to toggle CIMD support.
    /// </summary>
    /// <remarks>
    /// The default value is <c>true</c>.
    /// </remarks>
    public bool ClientIdMetadataDocumentSupported { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the authorization server expects a resource parameter.
    /// When <c>true</c>, the resource parameter must be present and match a valid resource.
    /// When <c>false</c>, the resource parameter must be absent to simulate legacy servers that
    /// do not support RFC 8707 resource indicators.
    /// </summary>
    /// <remarks>
    /// The default value is <c>true</c>.
    /// </remarks>
    public bool ExpectResource { get; set; } = true;

    public HashSet<string> DisabledMetadataPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> MetadataRequests => _metadataRequests.ToArray();

    /// <summary>
    /// Entry point for the application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static Task Main(string[] args) => new Program().RunServerAsync(args);

    /// <summary>
    /// Runs the OAuth server with the specified parameters.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="cancellationToken">Cancellation token to stop the server.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunServerAsync(string[]? args = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Starting in-memory test-only OAuth Server...");

        var builder = WebApplication.CreateEmptyBuilder(new()
        {
            Args = args,
        });

        if (_kestrelTransport is not null)
        {
            // Add passed-in transport before calling UseKestrel() to avoid the SocketsHttpHandler getting added.
            builder.Services.AddSingleton(_kestrelTransport);
        }

        builder.WebHost.UseKestrel(kestrelOptions =>
        {
            kestrelOptions.ListenLocalhost(_port, listenOptions =>
            {
                listenOptions.UseHttps();
            });
        });

        builder.Services.AddRoutingCore();
        builder.Services.AddLogging();

        builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
        {
            jsonOptions.SerializerOptions.TypeInfoResolverChain.Add(OAuthJsonContext.Default);
        });

        builder.Logging.AddConsole();
        if (_loggerProvider is not null)
        {
            builder.Logging.AddProvider(_loggerProvider);
        }

        var app = builder.Build();

        var clientId = "demo-client";
        var clientSecret = "demo-secret";

        _clients[clientId] = new ClientInfo
        {
            ClientId = clientId,
            ClientSecret = clientSecret,

            RequiresClientSecret = true,
            RedirectUris = ["http://localhost:1179/callback"],
        };

        // This client is pre-registered to support testing Client ID Metadata Documents (CIMD).
        // A non-test OAuth server implementation would fetch the metadata document from the client-specified
        // URL during authorization, but we just register the client here to keep the test implementation simple.
        // We also set 'RequiresClientSecret' to 'false' here because client secrets are disallowed when using CIMD.
        // See https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-00#section-4.1
        _clients[_clientMetadataDocumentUrl] = new ClientInfo
        {
            ClientId = _clientMetadataDocumentUrl,

            RequiresClientSecret = false,
            RedirectUris = ["http://localhost:1179/callback"],
        };

        // The MCP spec tells the client to use /.well-known/oauth-authorization-server but AddJwtBearer looks for
        // /.well-known/openid-configuration by default.
        //
        // The requirements for these endpoints are at https://www.rfc-editor.org/rfc/rfc8414 and
        // https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderMetadata respectively.
        // They do differ, but it's close enough at least for our current testing to use the same response for both.
        // See https://gist.github.com/localden/26d8bcf641703c08a5d8741aa9c3336c
        IResult HandleMetadataRequest(HttpContext context, string? issuerPath = null)
        {
            _metadataRequests.Enqueue(context.Request.Path);

            if (DisabledMetadataPaths.Contains(context.Request.Path))
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrEmpty(issuerPath))
            {
                issuerPath = $"/{issuerPath}";
            }

            var metadata = new OAuthServerMetadata
            {
                Issuer = $"{_url}{issuerPath}",
                AuthorizationEndpoint = $"{_url}/authorize",
                TokenEndpoint = $"{_url}/token",
                JwksUri = $"{_url}/.well-known/jwks.json",
                ResponseTypesSupported = ["code"],
                SubjectTypesSupported = ["public"],
                IdTokenSigningAlgValuesSupported = ["RS256"],
                ScopesSupported = ["openid", "profile", "email", "mcp:tools"],
                TokenEndpointAuthMethodsSupported = ["client_secret_post"],
                ClaimsSupported = ["sub", "iss", "name", "email", "aud"],
                CodeChallengeMethodsSupported = ["S256"],
                GrantTypesSupported = ["authorization_code", "refresh_token"],
                IntrospectionEndpoint = $"{_url}/introspect",
                RegistrationEndpoint = $"{_url}/register",
                ClientIdMetadataDocumentSupported = ClientIdMetadataDocumentSupported,
            };

            return Results.Ok(metadata);
        }

        app.MapGet("/.well-known/oauth-authorization-server", HandleMetadataRequest);
        app.MapGet("/.well-known/openid-configuration", HandleMetadataRequest);
        app.MapGet("/.well-known/oauth-authorization-server/{**issuerPath}", HandleMetadataRequest);
        app.MapGet("/.well-known/openid-configuration/{**issuerPath}", HandleMetadataRequest);
        app.MapGet("/{**fullPath}", (HttpContext context, string fullPath) =>
        {
            if (fullPath.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
            {
                return HandleMetadataRequest(context, fullPath[..^"/.well-known/openid-configuration".Length]);
            }

            return Results.NotFound();
        });

        // JWKS endpoint to expose the public key
        app.MapGet("/.well-known/jwks.json", () =>
        {
            var parameters = _rsa.ExportParameters(false);

            // Convert parameters to base64url encoding
            var e = WebEncoders.Base64UrlEncode(parameters.Exponent ?? Array.Empty<byte>());
            var n = WebEncoders.Base64UrlEncode(parameters.Modulus ?? Array.Empty<byte>());

            var jwks = new JsonWebKeySet
            {
                Keys = [
                    new JsonWebKey
                    {
                        KeyType = "RSA",
                        Use = "sig",
                        KeyId = _keyId,
                        Algorithm = "RS256",
                        Exponent = e,
                        Modulus = n
                    }
                ]
            };

            return Results.Ok(jwks);
        });

        // Authorize endpoint
        app.MapGet("/authorize", (
            [FromQuery] string client_id,
            [FromQuery] string? redirect_uri,
            [FromQuery] string response_type,
            [FromQuery] string code_challenge,
            [FromQuery] string code_challenge_method,
            [FromQuery] string? scope,
            [FromQuery] string? state,
            [FromQuery] string? resource) =>
        {
            // Validate client
            if (!_clients.TryGetValue(client_id, out var client))
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = "invalid_client",
                    ErrorDescription = "Client not found"
                });
            }

            // Validate redirect_uri
            if (string.IsNullOrEmpty(redirect_uri))
            {
                if (client.RedirectUris.Count == 1)
                {
                    redirect_uri = client.RedirectUris[0];
                }
                else
                {
                    return Results.BadRequest(new OAuthErrorResponse
                    {
                        Error = "invalid_request",
                        ErrorDescription = "redirect_uri is required when client has multiple registered URIs"
                    });
                }
            }
            else if (!client.RedirectUris.Contains(redirect_uri))
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Unregistered redirect_uri"
                });
            }

            // Validate response_type
            if (response_type != "code")
            {
                return Results.Redirect($"{redirect_uri}?error=unsupported_response_type&error_description=Only+code+response_type+is+supported&state={state}");
            }

            // Validate code challenge method
            if (code_challenge_method != "S256")
            {
                return Results.Redirect($"{redirect_uri}?error=invalid_request&error_description=Only+S256+code_challenge_method+is+supported&state={state}");
            }

            // Validate resource in accordance with RFC 8707.
            // When ExpectResource is false, the resource parameter must be absent (legacy mode).
            if (ExpectResource ? (string.IsNullOrEmpty(resource) || !ValidResources.Contains(resource)) : !string.IsNullOrEmpty(resource))
            {
                return Results.Redirect($"{redirect_uri}?error=invalid_target&error_description=The+specified+resource+is+not+valid&state={state}");
            }

            // Generate a new authorization code
            var code = GenerateRandomToken();
            var requestedScopes = scope?.Split(' ').ToList() ?? [];

            // Store code information for later verification
            _authCodes[code] = new AuthorizationCodeInfo
            {
                ClientId = client_id,
                RedirectUri = redirect_uri,
                CodeChallenge = code_challenge,
                Scope = requestedScopes,
                Resource = !string.IsNullOrEmpty(resource) ? new Uri(resource) : null
            };

            // Redirect back to client with the code
            var redirectUrl = $"{redirect_uri}?code={code}";
            if (!string.IsNullOrEmpty(state))
            {
                redirectUrl += $"&state={Uri.EscapeDataString(state)}";
            }

            return Results.Redirect(redirectUrl);
        });

        // Token endpoint
        app.MapPost("/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();

            // Authenticate client
            var client = AuthenticateClient(context, form);
            if (client == null)
            {
                context.Response.StatusCode = 401;
                return Results.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Invalid client credentials",
                    type: "https://tools.ietf.org/html/rfc6749#section-5.2");
            }

            // Validate resource in accordance with RFC 8707.
            // When ExpectResource is false, the resource parameter must be absent (legacy mode).
            var resource = form["resource"].ToString();
            if (ExpectResource ? (string.IsNullOrEmpty(resource) || !ValidResources.Contains(resource)) : !string.IsNullOrEmpty(resource))
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = "invalid_target",
                    ErrorDescription = "The specified resource is not valid."
                });
            }

            var grant_type = form["grant_type"].ToString();
            if (grant_type == "authorization_code")
            {
                var code = form["code"].ToString();
                var code_verifier = form["code_verifier"].ToString();
                var redirect_uri = form["redirect_uri"].ToString();

                // Validate code
                if (string.IsNullOrEmpty(code) || !_authCodes.TryRemove(code, out var codeInfo))
                {
                    return Results.BadRequest(new OAuthErrorResponse
                    {
                        Error = "invalid_grant",
                        ErrorDescription = "Invalid authorization code"
                    });
                }

                // Validate client_id
                if (codeInfo.ClientId != client.ClientId)
                {
                    return Results.BadRequest(new OAuthErrorResponse
                    {
                        Error = "invalid_grant",
                        ErrorDescription = "Authorization code was not issued to this client"
                    });
                }

                // Validate redirect_uri if provided
                if (!string.IsNullOrEmpty(redirect_uri) && redirect_uri != codeInfo.RedirectUri)
                {
                    return Results.BadRequest(new OAuthErrorResponse
                    {
                        Error = "invalid_grant",
                        ErrorDescription = "Redirect URI mismatch"
                    });
                }

                // Validate code verifier
                if (string.IsNullOrEmpty(code_verifier) || !VerifyCodeChallenge(code_verifier, codeInfo.CodeChallenge))
                {
                    return Results.BadRequest(new OAuthErrorResponse
                    {
                        Error = "invalid_grant",
                        ErrorDescription = "Code verifier does not match the challenge"
                    });
                }

                // Generate JWT token response
                var response = GenerateJwtTokenResponse(client.ClientId, codeInfo.Scope, codeInfo.Resource);
                return Results.Ok(response);
            }
            else if (grant_type == "refresh_token")
            {
                var refresh_token = form["refresh_token"].ToString();

                // Validate refresh token
                if (string.IsNullOrEmpty(refresh_token) || !_tokens.TryGetValue(refresh_token, out var tokenInfo) || tokenInfo.ClientId != client.ClientId)
                {
                    return Results.BadRequest(new OAuthErrorResponse
                    {
                        Error = "invalid_grant",
                        ErrorDescription = "Invalid refresh token"
                    });
                }

                // Generate new token response, keeping the same scopes
                var response = GenerateJwtTokenResponse(client.ClientId, tokenInfo.Scopes, tokenInfo.Resource);

                // Remove the old refresh token
                if (!string.IsNullOrEmpty(refresh_token))
                {
                    _tokens.TryRemove(refresh_token, out _);
                }

                HasRefreshedToken = true;
                return Results.Ok(response);
            }
            else
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = "unsupported_grant_type",
                    ErrorDescription = "Unsupported grant type"
                });
            }
        });

        // Introspection endpoint
        app.MapPost("/introspect", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var token = form["token"].ToString();

            if (string.IsNullOrEmpty(token))
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Token is required"
                });
            }

            // Check opaque access tokens
            if (_tokens.TryGetValue(token, out var tokenInfo))
            {
                if (tokenInfo.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    return Results.Ok(new TokenIntrospectionResponse { Active = false });
                }

                return Results.Ok(new TokenIntrospectionResponse
                {
                    Active = true,
                    ClientId = tokenInfo.ClientId,
                    Scope = string.Join(" ", tokenInfo.Scopes),
                    ExpirationTime = tokenInfo.ExpiresAt.ToUnixTimeSeconds(),
                    Audience = tokenInfo.Resource?.ToString()
                });
            }

            return Results.Ok(new TokenIntrospectionResponse { Active = false });
        });

        // Dynamic Client Registration endpoint (RFC 7591)
        app.MapPost("/register", async (HttpContext context) =>
        {
            using var stream = context.Request.Body;
            var registrationRequest = await JsonSerializer.DeserializeAsync(
                stream,
                OAuthJsonContext.Default.ClientRegistrationRequest,
                context.RequestAborted);

            if (registrationRequest is null)
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Invalid registration request"
                });
            }

            // Validate redirect URIs are provided
            if (registrationRequest.RedirectUris.Count == 0)
            {
                return Results.BadRequest(new OAuthErrorResponse
                {
                    Error = "invalid_redirect_uri",
                    ErrorDescription = "At least one redirect URI must be provided"
                });
            }

            // Validate redirect URIs
            foreach (var redirectUri in registrationRequest.RedirectUris)
            {
                if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    return Results.BadRequest(new OAuthErrorResponse
                    {
                        Error = "invalid_redirect_uri",
                        ErrorDescription = $"Invalid redirect URI: {redirectUri}"
                    });
                }
            }

            // Generate client credentials
            var clientId = $"dyn-{Guid.NewGuid():N}";
            var clientSecret = GenerateRandomToken();
            var issuedAt = DateTimeOffset.UtcNow;

            // Store the registered client
            _clients[clientId] = new ClientInfo
            {
                ClientId = clientId,
                RequiresClientSecret = true,
                ClientSecret = clientSecret,
                RedirectUris = registrationRequest.RedirectUris,
            };

            var registrationResponse = new ClientRegistrationResponse
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                ClientIdIssuedAt = issuedAt.ToUnixTimeSeconds(),
                RedirectUris = registrationRequest.RedirectUris,
                GrantTypes = ["authorization_code", "refresh_token"],
                ResponseTypes = ["code"],
                TokenEndpointAuthMethod = "client_secret_post",
            };

            return Results.Ok(registrationResponse);
        });

        app.MapGet("/", () => "Demo In-Memory OAuth 2.0 Server with JWT Support");

        Console.WriteLine($"OAuth Authorization Server running at {_url}");
        Console.WriteLine($"OAuth Server Metadata at {_url}/.well-known/oauth-authorization-server");
        Console.WriteLine($"JWT keys available at {_url}/.well-known/jwks.json");
        Console.WriteLine($"Demo Client ID: {clientId}");
        Console.WriteLine($"Demo Client Secret: {clientSecret}");

        await app.StartAsync(cancellationToken);
        _serverStarted.TrySetResult();

        // Wait until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }

        await app.StopAsync();
    }

    /// <summary>
    /// Authenticates a client based on client credentials in the request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="form">The form collection containing client credentials.</param>
    /// <returns>The client info if authentication succeeds, null otherwise.</returns>
    private ClientInfo? AuthenticateClient(HttpContext context, IFormCollection form)
    {
        var clientId = form["client_id"].ToString();
        var clientSecret = form["client_secret"].ToString();

        if (string.IsNullOrEmpty(clientId) || !_clients.TryGetValue(clientId, out var client))
        {
            return null;
        }

        if (client.RequiresClientSecret && client.ClientSecret != clientSecret)
        {
            return null;
        }

        return client;
    }

    /// <summary>
    /// Generates a JWT token response.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <param name="scopes">The approved scopes.</param>
    /// <param name="resource">The resource URI.</param>
    /// <returns>A token response.</returns>
    private TokenResponse GenerateJwtTokenResponse(string clientId, List<string> scopes, Uri? resource)
    {
        var expiresIn = TimeSpan.FromHours(1);
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.Add(expiresIn);
        var jwtId = Guid.NewGuid().ToString();

        // Create JWT header and payload
        var header = new Dictionary<string, string>
        {
            { "alg", "RS256" },
            { "typ", "JWT" },
            { "kid", _keyId },
        };

        var payload = new Dictionary<string, string>
        {
            { "iss", _url },
            { "sub", $"user-{clientId}" },
            { "name", $"user-{clientId}" },
            { "aud", resource?.ToString() ?? clientId },
            { "client_id", clientId },
            { "jti", jwtId },
            { "iat", issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
            { "exp", expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
            { "scope", string.Join(" ", scopes) },
        };

        // Create JWT token
        var headerJson = JsonSerializer.Serialize(header, OAuthJsonContext.Default.DictionaryStringString);
        var payloadJson = JsonSerializer.Serialize(payload, OAuthJsonContext.Default.DictionaryStringString);

        var headerBase64 = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var dataToSign = $"{headerBase64}.{payloadBase64}";
        var signature = _rsa.SignData(Encoding.UTF8.GetBytes(dataToSign), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureBase64 = WebEncoders.Base64UrlEncode(signature);

        var jwtToken = $"{headerBase64}.{payloadBase64}.{signatureBase64}";

        // Generate opaque refresh token
        var refreshToken = GenerateRandomToken();

        // Store token info (for refresh token and introspection)
        var tokenInfo = new TokenInfo
        {
            ClientId = clientId,
            Scopes = scopes,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            Resource = resource,
            JwtId = jwtId
        };

        _tokens[refreshToken] = tokenInfo;

        return new TokenResponse
        {
            AccessToken = jwtToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = (int)expiresIn.TotalSeconds,
            Scope = string.Join(" ", scopes)
        };
    }

    /// <summary>
    /// Generates a random token for authorization code or refresh token.
    /// </summary>
    /// <returns>A Base64Url encoded random token.</returns>
    public static string GenerateRandomToken()
    {
        var bytes = new byte[32];
        Random.Shared.NextBytes(bytes);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Verifies a PKCE code challenge against a code verifier.
    /// </summary>
    /// <param name="codeVerifier">The code verifier to verify.</param>
    /// <param name="codeChallenge">The code challenge to verify against.</param>
    /// <returns>True if the code challenge is valid, false otherwise.</returns>
    public static bool VerifyCodeChallenge(string codeVerifier, string codeChallenge)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var computedChallenge = WebEncoders.Base64UrlEncode(challengeBytes);

        return computedChallenge == codeChallenge;
    }
}
