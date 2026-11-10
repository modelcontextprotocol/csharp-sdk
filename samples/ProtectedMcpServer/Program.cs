using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore;
using ProtectedMcpServer.Tools;
using System.Net.Http.Headers;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var resource = builder.Configuration["Mcp:Resource"]
    ?? throw new InvalidOperationException("Configure Mcp:Resource with the canonical MCP server URL.");
var authority = builder.Configuration["Authentication:Authority"]
    ?? throw new InvalidOperationException("Configure Authentication:Authority with the authorization server issuer.");
var audience = builder.Configuration["Authentication:Audience"]
    ?? throw new InvalidOperationException("Configure Authentication:Audience with the expected access token audience.");
var scopesSupported = builder.Configuration.GetRequiredSection("Authentication:ScopesSupported").Get<string[]>()
    ?? throw new InvalidOperationException("Configure Authentication:ScopesSupported with the scopes clients should request.");
var requiredScopes = builder.Configuration.GetRequiredSection("Authentication:RequiredScopes").Get<string[]>()
    ?? throw new InvalidOperationException("Configure Authentication:RequiredScopes with the scope values required in access tokens.");

if (requiredScopes.Length == 0 || requiredScopes.Any(string.IsNullOrWhiteSpace))
{
    throw new InvalidOperationException("Authentication:RequiredScopes must contain at least one nonempty scope.");
}

var allowedOrigins = builder.Configuration.GetSection("Mcp:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];

// This sample runs the MCP server on localhost:7071, and it is intended to be callable from a
// companion web app running on a different host (localhost:5173), while preventing requests from
// other origins. This scenario requires enabling CORS and enabling a restrictive CORS policy.
//
// If your server is not meant for cross-origin browser access, leave CORS disabled.
//
// Only apply a lenient CORS policy if your server is intended to be callable from any browser.

builder.Services.AddCors(options =>
{
    options.AddPolicy("McpBrowserClient", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("POST")
            .WithHeaders(HeaderNames.ContentType, HeaderNames.Authorization, "MCP-Protocol-Version")
            .WithExposedHeaders(HeaderNames.WWWAuthenticate);
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = authority;
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        // Entra v2 access tokens use the API's application (client) ID, not its resource URL, as the audience.
        ValidAudience = audience,
        ValidIssuer = authority,
        NameClaimType = "name",
        RoleClaimType = "roles"
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var name = context.Principal?.Identity?.Name ?? "unknown";
            var email = context.Principal?.FindFirstValue("preferred_username") ?? "unknown";
            Console.WriteLine($"Token validated for: {name} ({email})");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Console.WriteLine($"Challenging client to authenticate with {authority}");
            return Task.CompletedTask;
        }
    };
})
.AddMcp(options =>
{
    options.ResourceMetadata = new()
    {
        Resource = resource,
        AuthorizationServers = { authority },
        ScopesSupported = scopesSupported,
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("McpTools", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            // Entra uses "scp"; the local test OAuth server uses "scope".
            var tokenScopes = context.User.FindAll("scp")
                .Concat(context.User.FindAll("scope"))
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.Ordinal);

            return requiredScopes.All(tokenScopes.Contains);
        });
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithTools<WeatherTools>()
    .WithHttpTransport(options =>
    {
        // Stateless mode is the default and recommended for servers that don't need 2025-11-25
        // protocol revision server-to-client requests like sampling or elicitation. Stateless model enables
        // horizontal scaling without session affinity and works with clients that don't send Mcp-Session-Id.
        options.SessionMode = HttpServerSessionMode.Stateless;
    });

// Configure HttpClientFactory for weather.gov API
builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
});

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapMcp().RequireAuthorization("McpTools").RequireCors("McpBrowserClient");

Console.WriteLine($"MCP resource: {resource}");
Console.WriteLine($"Using authorization server at {authority}");
Console.WriteLine($"Protected Resource Metadata URL: {resource.TrimEnd('/')}/.well-known/oauth-protected-resource");
Console.WriteLine("Press Ctrl+C to stop the server");

app.Run();
