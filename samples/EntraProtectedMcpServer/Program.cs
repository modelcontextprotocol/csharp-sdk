using EntraProtectedMcpServer.Configuration;
using EntraProtectedMcpServer.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using System.Net.Http.Headers;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Load configuration from multiple sources
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>();

// Bind configuration sections to strongly typed objects
var serverConfig = builder.Configuration.GetSection(ServerConfiguration.SectionName).Get<ServerConfiguration>();
var entraConfig = builder.Configuration.GetSection(EntraIdConfiguration.SectionName).Get<EntraIdConfiguration>();

// Validate required configuration
ValidateConfiguration(serverConfig, entraConfig);

// Inject IOptions<EntraIdConfiguration>
builder.Services.Configure<EntraIdConfiguration>(
    builder.Configuration.GetSection(EntraIdConfiguration.SectionName));

// Build derived configuration values
var authorityUrl = entraConfig!.AuthorityUrl;
var validAudiences = entraConfig.ValidAudiences.Count > 0 ? entraConfig.ValidAudiences : [entraConfig.ClientId, $"api://{entraConfig.ClientId}"];
var validIssuers = entraConfig.ValidIssuers.Count > 0 ? entraConfig.ValidIssuers : [authorityUrl!, $"https://sts.windows.net/{entraConfig.TenantId}/"];
var scopesSupported = entraConfig.ScopesSupported.Select(scope => $"api://{entraConfig.ClientId}/{scope}").ToList();

// Configure authentication
ConfigureAuthentication(builder.Services, serverConfig!, entraConfig, authorityUrl!, validAudiences, validIssuers, scopesSupported);

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithToolsFromAssembly()
    .WithHttpTransport();

// Configure HttpClientFactory for weather.gov API
builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
});

// Configure HttpClientFactory for Microsoft Graph API
builder.Services.AddHttpClient("GraphApi", client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("graph-tool", "1.0"));
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp().RequireAuthorization();

// Display startup information
DisplayStartupInfo(serverConfig!, entraConfig, authorityUrl!);

app.Run(serverConfig!.Url);

static void ValidateConfiguration(ServerConfiguration? serverConfig, EntraIdConfiguration? entraConfig)
{
    if (serverConfig?.Url is null)
    {
        throw new InvalidOperationException("Server:Url configuration is required.");
    }

    if (entraConfig is null)
    {
        throw new InvalidOperationException("EntraId configuration section is required.");
    }

    if (string.IsNullOrEmpty(entraConfig.TenantId))
    {
        throw new InvalidOperationException("EntraId:TenantId configuration is required.");
    }

    if (string.IsNullOrEmpty(entraConfig.ClientId))
    {
        throw new InvalidOperationException("EntraId:ClientId configuration is required.");
    }

    if (string.IsNullOrEmpty(entraConfig.ClientSecret))
    {
        throw new InvalidOperationException("EntraId:ClientSecret configuration is required. Consider using user secrets or environment variables for production.");
    }

    if (string.IsNullOrEmpty(entraConfig.AuthorityUrl))
    {
        throw new InvalidOperationException("EntraId:AuthorityUrl configuration is required.");
    }

    if (string.IsNullOrEmpty(entraConfig.TokenEndpoint))
    {
        throw new InvalidOperationException("EntraId:TokenEndpoint configuration is required.");
    }
}

static void ConfigureAuthentication(
    IServiceCollection services,
    ServerConfiguration serverConfig,
    EntraIdConfiguration entraConfig,
    string authorityUrl,
    IList<string> validAudiences,
    IList<string> validIssuers,
    IList<string> scopesSupported)
{
    services.AddAuthentication(options =>
    {
        options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.Authority = authorityUrl;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAudiences = validAudiences,
            ValidIssuers = validIssuers,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var name = context.Principal?.FindFirstValue("name") ??
                          context.Principal?.FindFirstValue("preferred_username") ?? "unknown";
                var upn = context.Principal?.FindFirstValue("upn") ??
                         context.Principal?.FindFirstValue("email") ??
                         context.Principal?.FindFirstValue("preferred_username") ?? "unknown";
                var tenantId = context.Principal?.FindFirstValue("tid");

                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Token validated for: {Name} ({Upn}) from tenant: {TenantId}", name, upn, tenantId);
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Authentication failed: {Message}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Challenging client to authenticate with Entra ID");
                return Task.CompletedTask;
            }
        };
    })
    .AddMcp(options =>
    {
        options.ResourceMetadata = new()
        {
            Resource = new Uri(serverConfig.Url),
            ResourceDocumentation = serverConfig.ResourceDocumentationUrl != null ? new Uri(serverConfig.ResourceDocumentationUrl) : null,
            AuthorizationServers = { new Uri(entraConfig.AuthorityUrl!) },
            ScopesSupported = scopesSupported.ToList(),
        };
    });
}

static void DisplayStartupInfo(ServerConfiguration serverConfig, EntraIdConfiguration entraConfig, string authorityUrl)
{
    Console.WriteLine($"Starting MCP server with Entra ID authorization at {serverConfig.Url}");
    Console.WriteLine($"Using Microsoft Entra ID tenant: {entraConfig.TenantId}");
    Console.WriteLine($"Client ID: {entraConfig.ClientId}");
    Console.WriteLine($"Authority: {authorityUrl}");
    Console.WriteLine($"Protected Resource Metadata URL: {serverConfig.Url}.well-known/oauth-protected-resource");
    Console.WriteLine("Press Ctrl+C to stop the server");
}