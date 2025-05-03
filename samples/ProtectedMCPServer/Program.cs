using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Auth;
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
    // Configure the MCP authentication with the same Entra ID server
    options.ResourceMetadata.AuthorizationServers.Add(new Uri($"{instance}{tenantId}/v2.0"));
    options.ResourceMetadata.BearerMethodsSupported.Add("header");
    options.ResourceMetadata.ScopesSupported.AddRange(["api://167b4284-3f92-4436-92ed-38b38f83ae08/weather.read"]);
    options.ResourceMetadata.ResourceDocumentation = new Uri("https://docs.example.com/api/weather");
});

// Add authorization services
builder.Services.AddAuthorization(options =>
{
    options.AddMcpPolicy();
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

Console.WriteLine();
Console.WriteLine("Entra ID (Azure AD) JWT token validation is configured");
Console.WriteLine();
Console.WriteLine("To test the server:");
Console.WriteLine("1. Use an MCP client that supports OAuth flow with Microsoft Entra ID");
Console.WriteLine("2. The client should obtain a token for audience: api://weather-api");
Console.WriteLine("3. The token should be issued by Microsoft Entra ID tenant: " + tenantId);
Console.WriteLine("4. Include this token in the Authorization header of requests");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop the server");

app.Run("http://localhost:7071/");
