using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Auth;
using ModelContextProtocol.Auth.Types;
using ProtectedMCPServer.Tools;
using System.Net.Http.Headers;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Define Entra ID (Azure AD) configuration
var tenantId = "a2213e1c-e51e-4304-9a0d-effe57f31655"; // This is the tenant ID from your existing configuration
var instance = "https://login.microsoftonline.com/";

// Configure authentication to use MCP for challenges and Entra ID JWT Bearer for token validation
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme; // Use MCP for challenges
})
.AddJwtBearer(options =>
{
    // Configure for Entra ID (Azure AD) token validation
    options.Authority = $"{instance}{tenantId}/v2.0";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        // Configure validation parameters for Entra ID tokens
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        // Default audience - you should replace this with your actual app/API registration ID
        ValidAudience = "167b4284-3f92-4436-92ed-38b38f83ae08",

        // This validates that tokens come from your Entra ID tenant
        ValidIssuer = $"{instance}{tenantId}/v2.0",

        // These claims are used by the app for identity representation
        NameClaimType = "name",
        RoleClaimType = "roles"
    };

    // Enable metadata-based issuer key retrieval
    options.MetadataAddress = $"{instance}{tenantId}/v2.0/.well-known/openid-configuration";

    // Add development mode debug logging for token validation
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
            Console.WriteLine($"Challenging client to authenticate with Entra ID");
            return Task.CompletedTask;
        }
    };
})
.AddMcp(options =>
{
    options.ResourceMetadataProvider = context => 
    {
        var metadata = new ProtectedResourceMetadata
        {
            BearerMethodsSupported = { "header" },
            ResourceDocumentation = new Uri("https://docs.example.com/api/weather"),
            AuthorizationServers = { new Uri($"{instance}{tenantId}/v2.0") }
        };

        metadata.ScopesSupported.AddRange(new[] {
            "api://167b4284-3f92-4436-92ed-38b38f83ae08/weather.read" 
        });
        
        return metadata;
    };
});

// Add authorization services
builder.Services.AddAuthorization(options =>
{
    // Modify the MCP policy to include both MCP and JWT Bearer schemes
    // This ensures the bearer token is properly authenticated while maintaining MCP for challenges
    options.AddMcpPolicy(configurePolicy: builder => 
        builder.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme));
});

// Configure MCP Server
builder.Services.AddMcpServer()
.WithTools<WeatherTools>()
.WithHttpTransport();

builder.Services.AddSingleton(_ =>
{
    var client = new HttpClient() { BaseAddress = new Uri("https://api.weather.gov") };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
    return client;
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp().RequireAuthorization(McpAuthenticationDefaults.AuthenticationScheme);

Console.WriteLine("Starting MCP server with authorization at http://localhost:7071");
Console.WriteLine("PRM Document URL: http://localhost:7071/.well-known/oauth-protected-resource");
Console.WriteLine("  - This endpoint returns different metadata based on the client type!");
Console.WriteLine("  - Try with different User-Agent headers or add ?mobile query parameter");

Console.WriteLine();
Console.WriteLine("Entra ID (Azure AD) JWT token validation is configured");
Console.WriteLine();
Console.WriteLine("To test the server with different client types:");
Console.WriteLine("1. Standard client: No special headers needed");
Console.WriteLine("2. Mobile client: Add 'mobile' in User-Agent or use ?mobile query parameter");
Console.WriteLine("3. Partner client: Include 'partner' in User-Agent or add X-Partner-API header");
Console.WriteLine();
Console.WriteLine("Each client type will receive different authorization requirements!");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop the server");

app.Run("http://localhost:7071/");
