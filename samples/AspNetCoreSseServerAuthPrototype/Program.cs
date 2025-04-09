using AspNetCoreSseServerAuthPrototype;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer().WithToolsFromAssembly();

builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(policy =>
	{
		policy
		.AllowAnyOrigin()
		.AllowAnyMethod()
		.AllowAnyHeader();
	});
});

builder.Services.AddAuthentication(config =>
{
	config.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
	config.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
	config.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
	//This is the cookie we are going to use for the OIDC authentication flow.
	.AddCookie(options =>
	{
		options.Cookie.Name = "MyMCPServer.Sse.Session";
	})
	//This is the remote OIDC authentication the MCP server should use.
	.AddOpenIdConnect(o =>
	{
		o.Authority = "https://localhost:5001";
		o.ClientId = "mcp_server";
		o.ClientSecret = "secret";
		o.ResponseType = OpenIdConnectResponseType.Code;
		o.Scope.Add("openid");
		o.Scope.Add("profile");
		o.Scope.Add("api");
		o.SaveTokens = true;
		o.Events.OnTicketReceived = ctx =>
		{
			// Add the access token claims to the cookie
			var accesstoken = ctx.Properties?.GetTokenValue("access_token");
			var handler = new JsonWebTokenHandler();
			var token = handler.ReadJsonWebToken(accesstoken);
			ctx.Principal!.AddIdentity(new ClaimsIdentity(token.Claims, OpenIdConnectDefaults.AuthenticationScheme));

			// Remove the access token from the cookie
			ctx.Properties?.GetTokens().ToList().ForEach(token => ctx.Properties.UpdateTokenValue(token.Name, string.Empty));

			return Task.CompletedTask;
		};
	})
	//This is the bearer authentication we are going to require for the MCP endpoints.
	.AddJwtBearer(options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = false,//We dont want to provide a valid issuer URL here, which is changing based on local config. Validating the issuer signing key is enough.
			ValidateAudience = true,
			ValidateLifetime = true,
			ValidateIssuerSigningKey = true,
			ValidAudience = "mcp_server",
		};
	});

builder.Services.AddAuthorization(options =>
{
	//With this policy we are going to require bearer authentication for the MCP endpoints.
	options.AddPolicy(JwtBearerDefaults.AuthenticationScheme, policy =>
	{
		policy.AuthenticationSchemes = [JwtBearerDefaults.AuthenticationScheme];
		policy.RequireAuthenticatedUser();
	});
});

//Lets configure MCP OAuth by providing the token signing key, a client repository for dynamic registration, and the JWT audience.
builder.Services.AddOAuth<SigningKey, ClientRespository>(options =>
{
	options.Audience = "mcp_server";
});

//We need to configure the signing key for the JWT bearer authentication.
builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, JwtBearerOptionsSigningKeyConfiguration>();







var app = builder.Build();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Hello MCP!");
app.MapMcp().RequireAuthorization(JwtBearerDefaults.AuthenticationScheme);
app.MapOAuth();

app.Run();








class ClientRespository : OAuth.IClientRepository
{
	readonly ConcurrentDictionary<string, OAuth.ClientRegistration> clientRegistrations;

	public ClientRespository()
	{
		clientRegistrations = new ConcurrentDictionary<string, OAuth.ClientRegistration>(StringComparer.Ordinal);
	}

	public Task<OAuth.ClientRegistration?> Get(string clientId)
	{
		return clientRegistrations.TryGetValue(clientId, out var clientRegistration)
			? Task.FromResult<OAuth.ClientRegistration?>(clientRegistration)
			: Task.FromResult<OAuth.ClientRegistration?>(null);
	}

	public Task Register(string clientId, OAuth.ClientRegistration clientRegistration)
	{
		clientRegistrations.AddOrUpdate(clientId, clientRegistration, (key, oldValue) => clientRegistration);
		return Task.CompletedTask;
	}
}

class SigningKey : OAuth.IKeyProvider
{
	public SigningKey(IWebHostEnvironment env)
	{
		var rsaKey = RSA.Create();
		var path = Path.Combine(env.ContentRootPath, "devkey.key");
		if (File.Exists(path))
		{
			rsaKey.ImportRSAPrivateKey(File.ReadAllBytes(path), out _);
		}
		else
		{
			var privateKey = rsaKey.ExportRSAPrivateKey();
			File.WriteAllBytes(path, privateKey);
		}

		this.SecurityKey = new RsaSecurityKey(rsaKey);
	}

	public SecurityKey SecurityKey { get; }

	public Task<SecurityKey> GetSigningKey()
	{
		return Task.FromResult(this.SecurityKey);
	}

	public void PostConfigure(string? name, JwtBearerOptions options)
	{
		options.TokenValidationParameters.IssuerSigningKey = SecurityKey;
	}
}

class JwtBearerOptionsSigningKeyConfiguration : IPostConfigureOptions<JwtBearerOptions>
{
	private readonly OAuth.IKeyProvider keyProvider;

	public JwtBearerOptionsSigningKeyConfiguration(OAuth.IKeyProvider keyProvider)
	{
		this.keyProvider = keyProvider;
	}

	public void PostConfigure(string? name, JwtBearerOptions options)
	{
		options.TokenValidationParameters.IssuerSigningKey = keyProvider.GetSigningKey().Result;
	}
}