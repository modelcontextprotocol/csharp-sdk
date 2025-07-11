using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using AzureB2CClientCredentials.Tools;
using System.Net.Http.Headers;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var serverUrl = "http://localhost:7071/";

// Azure B2C Configuration
var azureB2CInstance = builder.Configuration["AzureB2C:Instance"] ?? "https://yourtenant.b2clogin.com";
var azureB2CTenant = builder.Configuration["AzureB2C:Tenant"] ?? "yourtenant.onmicrosoft.com";
var azureB2CPolicy = builder.Configuration["AzureB2C:Policy"] ?? "B2C_1_signupsignin";
var azureB2CClientId = builder.Configuration["AzureB2C:ClientId"] ?? "your-client-id";
var azureB2CAuthority = $"{azureB2CInstance}/{azureB2CTenant}/{azureB2CPolicy}/v2.0";
var azureB2CMetadataAddress = $"{azureB2CAuthority}/.well-known/openid-configuration";

builder.Services.AddAuthentication(options =>
{
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Configure to validate tokens from Azure B2C
    options.Authority = azureB2CAuthority;
    options.MetadataAddress = azureB2CMetadataAddress;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidAudience = azureB2CClientId, // The client ID of your application
        ValidIssuer = azureB2CAuthority,
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
        // Azure B2C uses 'aud' claim for audience validation
        ValidAudiences = new[] { azureB2CClientId },
        // Allow for clock skew
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var name = context.Principal?.Identity?.Name ?? "unknown";
            var email = context.Principal?.FindFirstValue("preferred_username") ?? 
                       context.Principal?.FindFirstValue("email") ?? "unknown";
            var objectId = context.Principal?.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier") ?? "unknown";
            Console.WriteLine($"Token validated for: {name} ({email}) - ObjectId: {objectId}");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Console.WriteLine($"Challenging client to authenticate with Azure B2C");
            return Task.CompletedTask;
        }
    };
})
.AddMcp(options =>
{
    options.ResourceMetadata = new()
    {
        Resource = new Uri(serverUrl),
        ResourceDocumentation = new Uri("https://docs.example.com/api/weather"),
        AuthorizationServers = { new Uri(azureB2CAuthority) },
        ScopesSupported = ["mcp:tools"],
    };
});

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithTools<WeatherTools>()
    .WithHttpTransport();

// Configure HttpClientFactory for weather.gov API
builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Use the default MCP policy name that we've configured
app.MapMcp().RequireAuthorization();

Console.WriteLine($"Starting Azure B2C Client Credentials MCP server with authorization at {serverUrl}");
Console.WriteLine($"Using Azure B2C authority: {azureB2CAuthority}");
Console.WriteLine($"Protected Resource Metadata URL: {serverUrl}.well-known/oauth-protected-resource");
Console.WriteLine("Press Ctrl+C to stop the server");

app.Run(serverUrl);
