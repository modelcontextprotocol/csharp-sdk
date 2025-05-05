using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Types.Authentication;
using ProtectedMCPServer.Tools;

var builder = WebApplication.CreateBuilder(args);

var serverUrl = "http://localhost:7071/";
var tenantId = "a2213e1c-e51e-4304-9a0d-effe57f31655";
var instance = "https://login.microsoftonline.com/";

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = $"{instance}{tenantId}/v2.0";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidAudience = "167b4284-3f92-4436-92ed-38b38f83ae08",
        ValidIssuer = $"{instance}{tenantId}/v2.0",
        NameClaimType = "name",
        RoleClaimType = "roles"
    };

    options.MetadataAddress = $"{instance}{tenantId}/v2.0/.well-known/openid-configuration";

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
            
            // Skip the default Bearer header - MCP handler will provide the complete one
            context.HandleResponse();
            
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
    
    // Specify authentication schemes that this server supports
    options.SupportedAuthenticationSchemes.Add("Bearer");
    options.SupportedAuthenticationSchemes.Add("Basic");
    
    // For a server that doesn't want to support Bearer, you would simply not add it:
    // options.SupportedAuthenticationSchemes.Add("Basic");
    // options.SupportedAuthenticationSchemes.Add("Digest");
    
    // You can also use the dynamic provider for more flexible scheme selection:
    /*
    options.SupportedAuthenticationSchemesProvider = context =>
    {
        // You can use context information to determine which schemes to offer
        var schemes = new List<string>();
        
        // Add Bearer for most clients
        schemes.Add("Bearer");
        
        // Example of conditional scheme based on client type or other factors
        if (context.Request.Headers.UserAgent.ToString().Contains("SpecialClient"))
        {
            schemes.Add("Basic");
        }
        
        return schemes;
    };
    */
});

builder.Services.AddAuthorization(options =>
{
    options.AddMcpPolicy(configurePolicy: builder => 
        builder.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme));
});

builder.Services.AddHttpContextAccessor();
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

Console.WriteLine($"Starting MCP server with authorization at {serverUrl}");
Console.WriteLine($"PRM Document URL: {serverUrl}.well-known/oauth-protected-resource");
Console.WriteLine("Press Ctrl+C to stop the server");

app.Run(serverUrl);
