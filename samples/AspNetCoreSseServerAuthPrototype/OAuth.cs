using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace AspNetCoreSseServerAuthPrototype
{
	/// <summary>
	/// Experimental implementation of OAuth endpoints for MCP servers.
	/// </summary>
	public static class OAuth
	{
		public class Options
		{
			/// <summary>
			/// The audience for the issued JWT token.
			/// </summary>
			public string? Audience { get; set; }
		}

		public interface IKeyProvider
		{
			/// <summary>
			/// Get the signing key ti sign the issued JWT token.
			/// </summary>
			Task<SecurityKey> GetSigningKey();
		}

		public interface IClientRepository
		{
			/// <summary>
			/// Remember a newly registered client.
			/// </summary>
			Task Register(string clientId, ClientRegistration clientRegistration);

			/// <summary>
			/// Recalla registered client.
			/// </summary>
			Task<ClientRegistration?> Get(string clientId);
		}

		public class ClientRegistration
		{
			[JsonPropertyName("redirect_uris")]
			public string[] RedirectUris { get; set; } = null!;
			[JsonPropertyName("grant_types")]
			public string[] GrantTypes { get; set; } = null!;
			[JsonPropertyName("response_types")]
			public string[] ResponseTypes { get; set; } = null!;
			[JsonPropertyName("client_name")]
			public string ClientName { get; set; } = null!;
			[JsonPropertyName("client_uri")]
			public string ClientUri { get; set; } = null!;
			[JsonPropertyName("token_endpoint_auth_method")]
			public string TokenEndpointAuthMethod { get; set; } = null!;
			[JsonPropertyName("client_secret")]
			public string? ClientSecret { get; set; }
			[JsonPropertyName("scopes")]
			public string[]? Scopes { get; set; }
		}

		class AuthCode
		{
			public string UserId { get; set; } = null!;
			public string? UserName { get; set; }
			public string ClientId { get; set; } = null!;
			public string[]? Scopes { get; set; }
			public string RedirectUri { get; set; } = null!;
			public string CodeChallenge { get; set; } = null!;
			public string CodeChallengeMethod { get; set; } = null!;
			public DateTime Expiry { get; set; }
		}

		/// <summary>
		/// Register reguired services for OAuth endpoints.
		/// </summary>
		public static IServiceCollection AddOAuth<TSigningKeyProvider, TClientRepository>(this IServiceCollection services, Action<Options> optionsConfiguration)
			where TSigningKeyProvider : class, IKeyProvider
			where TClientRepository : class, IClientRepository
		{
			services.TryAddSingleton<IKeyProvider, TSigningKeyProvider>();
			services.TryAddSingleton<IClientRepository, TClientRepository>();
			services.Configure(optionsConfiguration);

			return services;
		}

		/// <summary>
		/// Map OAuth endpoints.
		/// </summary>
		public static IEndpointConventionBuilder MapOAuth(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
		{
			var routePathPrefix = !string.IsNullOrEmpty(pattern) ? $"/{pattern}" : string.Empty;
			var supported = new
			{
				ResponseTypesSupported = new[] { "code" },
				ResponseModesSupported = new[] { "query" },
				GrantTypesSupported = new[] { "authorization_code", "refresh_token" },
				TokenEndpointAuthMethodsSupported = new[] { "client_secret_post", "none" },
				CodeChallengeMethodsSupported = new[] { "S256" },
			};

			endpoints.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) =>
			{
				var iss = new Uri($"{request.Scheme}://{request.Host}").AbsoluteUri.TrimEnd('/');
				return Results.Ok(new
				{
					issuer = iss,
					authorization_endpoint = $"{iss}{routePathPrefix}/authorize",
					token_endpoint = $"{iss}{routePathPrefix}/token",
					registration_endpoint = $"{iss}{routePathPrefix}/register",
					//revocation_endpoint = $"{iss}{pathPrefix}/token",
					response_types_supported = supported.ResponseTypesSupported,
					response_modes_supported = supported.ResponseModesSupported,
					grant_types_supported = supported.GrantTypesSupported,
					token_endpoint_auth_methods_supported = supported.TokenEndpointAuthMethodsSupported,
					code_challenge_methods_supported = supported.CodeChallengeMethodsSupported,
				});
			});

			var routeGroup = endpoints.MapGroup(pattern);

			routeGroup.MapPost("/register", async ([FromBody] ClientRegistration clientRegistration, IClientRepository clientRepository) =>
			{
				var client_id = Guid.NewGuid().ToString();

				if (string.IsNullOrEmpty(clientRegistration.ClientName))
				{
					return Results.BadRequest(new { error = "invalid_request", error_description = "Client name is required" });
				}

				if (clientRegistration.ResponseTypes.Intersect(supported.ResponseTypesSupported).Count() != clientRegistration.ResponseTypes.Length)
				{
					return Results.BadRequest(new { error = "invalid_request", error_description = "Invalid response types" });
				}

				if (clientRegistration.GrantTypes.Intersect(supported.GrantTypesSupported).Count() != clientRegistration.GrantTypes.Length)
				{
					return Results.BadRequest(new { error = "invalid_request", error_description = "Invalid grant types" });
				}

				if (!supported.TokenEndpointAuthMethodsSupported.Contains(clientRegistration.TokenEndpointAuthMethod))
				{
					return Results.BadRequest(new { error = "invalid_request", error_description = "Invalid token endpoint auth" });
				}

				await clientRepository.Register(client_id, clientRegistration);

				var createdAt = $"{routePathPrefix}/register/{client_id}";
				return Results.Created(createdAt, new
				{
					client_id,
					redirect_uris = clientRegistration.RedirectUris,
					client_name = clientRegistration.ClientName,
					client_uri = clientRegistration.ClientUri,
					grant_types = clientRegistration.GrantTypes,
					response_types = clientRegistration.ResponseTypes,
					token_endpoint_auth_method = clientRegistration.TokenEndpointAuthMethod,
					registration_client_uri = createdAt,
					client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				});
			});

			routeGroup.MapGet("/authorize", async (
				HttpRequest request,
				IDataProtectionProvider dataProtectionProvider,
				IOptions<Options> options,
				IClientRepository clientRepository) =>
			{
				var iss = new Uri($"{request.Scheme}://{request.Host}").AbsoluteUri.TrimEnd('/');
				request.Query.TryGetValue("state", out var state);

				if (!request.Query.TryGetValue("client_id", out var clientId))
				{
					return Results.BadRequest(new { error = "unauthorized_client", state, iss, });
				}

				var client = await clientRepository.Get(clientId.ToString());
				if (client is null)
				{
					return Results.BadRequest(new { error = "unauthorized_client", state, iss, });
				}

				if (!request.Query.TryGetValue("response_type", out var responseType) || !client.ResponseTypes.Contains(responseType.ToString()))
				{
					return Results.BadRequest(new { error = "invalid_request", state, iss, });
				}

				request.Query.TryGetValue("code_challenge", out var codeChallenge);

				if (!request.Query.TryGetValue("code_challenge_method", out var codeChallengeMethod) || !supported.CodeChallengeMethodsSupported.Contains(codeChallengeMethod.ToString()))
				{
					return Results.BadRequest(new { error = "invalid_request", state, iss, });
				}

				request.Query.TryGetValue("redirect_uri", out var redirectUri);

				var requestScopes = default(string[]?);
				if (request.Query.TryGetValue("scope", out var scope))
				{
					var scopes = scope.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (scopes.Intersect(client.Scopes ?? []).Count() != scopes.Length)
					{
						return Results.BadRequest(new { error = "invalid_scope", state, iss, });
					}

					var userScopes = request.HttpContext.User.Claims
						.Where(c => c.Type == "scope")
						.Select(c => c.Value)
						.ToList();

					requestScopes = [.. scopes.Where(userScopes.Contains)];
				}

				var protector = dataProtectionProvider.CreateProtector("oauth");
				var authCode = new AuthCode
				{
					UserId = request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!,
					UserName = request.HttpContext.User.FindFirstValue("name"),
					ClientId = clientId!,
					Scopes = requestScopes,
					RedirectUri = redirectUri!,
					CodeChallenge = codeChallenge!,
					CodeChallengeMethod = codeChallengeMethod!,
					Expiry = DateTime.UtcNow.AddMinutes(5)
				};
				var code = protector.Protect(JsonSerializer.Serialize(authCode));
				return Results.Redirect($"{redirectUri}?code={code}&state={state}&iss={HttpUtility.UrlEncode(iss)}");
			}).RequireAuthorization();

			routeGroup.MapPost("/token", async (
				HttpRequest request,
				IDataProtectionProvider dataProtectionProvider,
				IOptions<Options> options,
				IClientRepository clientRepository,
				IKeyProvider keyProvider) =>
			{
				var bodyBytes = await request.BodyReader.ReadAsync();
				var bodyContent = Encoding.UTF8.GetString(bodyBytes.Buffer);
				request.BodyReader.AdvanceTo(bodyBytes.Buffer.End);

				string grantType = "", code = "", redirectUri = "", codeVerifier = "", clientId = "", clientSecret = "", refreshToken = "";
				foreach (var part in bodyContent.Split('&'))
				{
					var subParts = part.Split('=');
					var key = subParts[0];
					var value = subParts[1];
					if (key == "grant_type") grantType = value;
					else if (key == "code") code = value;
					else if (key == "redirect_uri") redirectUri = HttpUtility.UrlDecode(value);
					else if (key == "code_verifier") codeVerifier = HttpUtility.UrlDecode(value);
					else if (key == "client_id") clientId = value;
					else if (key == "client_secret") clientSecret = value;
					else if (key == "refresh_token") refreshToken = value;
				}

				var client = await clientRepository.Get(clientId);
				if (client is null)
				{
					return Results.BadRequest(new { error = "invalid_client", error_description = "Invalid client id" });
				}

				if (client.TokenEndpointAuthMethod == "client_secret_post")
				{
					if (clientSecret != client.ClientSecret)
					{
						return Results.BadRequest(new { error = "invalid_client", error_description = "Invalid client secret" });
					}
				}
				else if (client.TokenEndpointAuthMethod == "none")
				{
					if (!string.IsNullOrEmpty(clientSecret))
					{
						return Results.BadRequest(new { error = "invalid_client", error_description = "Client secret not allowed" });
					}
				}
				else
				{
					return Results.BadRequest(new { error = "invalid_client", error_description = "Invalid client auth method" });
				}


				if (string.IsNullOrEmpty(grantType) || !client.GrantTypes.Contains(grantType))
				{
					return Results.BadRequest(new { error = "invalid_grant", error_description = "Invalid grant type" });
				}

				string userId;
				string userName;
				string[] scopes;
				if (grantType == "authorization_code")
				{
					if (code == string.Empty)
					{
						return Results.BadRequest(new { error = "invalid_grant", error_description = "Authorization code missing" });
					}

					var protector = dataProtectionProvider.CreateProtector("oauth");
					var codeString = protector.Unprotect(code);
					var authCode = JsonSerializer.Deserialize<AuthCode>(codeString);

					if (authCode == null)
					{
						return Results.BadRequest(new { error = "invalid_grant", error_description = "Authorization code missing" });
					}

					if (authCode.Expiry < DateTime.UtcNow)
					{
						return Results.BadRequest(new { error = "invalid_grant", error_description = "Authorization code expired" });
					}

					if (authCode.RedirectUri != redirectUri)
					{
						return Results.BadRequest(new { error = "invalid_grant", error_description = "Invalid redirect uri" });
					}

					using var sha256 = SHA256.Create();
					var codeChallenge = Base64UrlEncoder.Encode(sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)));
					if (authCode.CodeChallenge != codeChallenge)
					{
						return Results.BadRequest(new { error = "invalid_grant", error_description = "Invalid code verifier" });
					}

					userId = authCode.UserId;
					userName = authCode.UserName ?? string.Empty;
					scopes = authCode.Scopes ?? [];
				}
				else if (grantType == "refresh_token")
				{
					throw new NotImplementedException();
				}
				else
				{
					return Results.BadRequest(new { error = "invalid_grant", error_description = "Invalid grant type" });
				}

				var handler = new JsonWebTokenHandler();
				var iss = new Uri($"{request.Scheme}://{request.Host}").AbsoluteUri.TrimEnd('/');
				var accessToken = handler.CreateToken(new SecurityTokenDescriptor
				{
					Issuer = iss,
					Subject = new ClaimsIdentity(
					[
						new Claim("sub", userId),
						new Claim("name", userName),
						..scopes.Select(s => new Claim("scope", s)),
					]),
					Audience = options.Value.Audience,
					Expires = DateTime.UtcNow.AddMinutes(5),
					TokenType = "Bearer",
					SigningCredentials = new SigningCredentials(await keyProvider.GetSigningKey(), SecurityAlgorithms.RsaSha256),
				});
				return Results.Ok(new
				{
					access_token = accessToken,
					token_type = "Bearer",
					//refresh_token = "",
				});
			});

			return routeGroup;
		}
	}
}