using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol.Types;
using System.Security.Claims;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// Configure MCP Server
builder.Services.AddMcpServer(options =>
{
    options.ServerInstructions = "This is an MCP server with OAuth authorization enabled.";

    // Configure regular server capabilities like tools, prompts, resources
    options.Capabilities = new()
    {
        Tools = new()
        {
            // Simple Echo tool
            CallToolHandler = (request, cancellationToken) =>
            {
                if (request.Params?.Name == "echo")
                {
                    if (request.Params.Arguments?.TryGetValue("message", out var message) is not true)
                    {
                        throw new Exception("It happens.");
                    }

                    return new ValueTask<CallToolResponse>(new CallToolResponse()
                    {
                        Content = [new Content() { Text = $"Echo: {message}", Type = "text" }]
                    });
                }

                // Protected tool that requires authorization
                if (request.Params?.Name == "protected-data")
                {
                    // This tool will only be accessible to authenticated clients
                    return new ValueTask<CallToolResponse>(new CallToolResponse()
                    {
                        Content = [new Content() { Text = "This is protected data that only authorized clients can access" }]
                    });
                }

                throw new Exception("It happens.");
            },

            ListToolsHandler = async (_, _) => new()
            {
                Tools =
                [
                    new()
                    {
                        Name = "echo",
                        Description = "Echoes back the message you send"
                    },
                    new()
                    {
                        Name = "protected-data",
                        Description = "Returns protected data that requires authorization"
                    }
                ]
            }
        }
    };
})
.WithHttpTransport()
.WithAuthorization(metadata => 
{
    // Configure the OAuth metadata for this server
    metadata.AuthorizationServers.Add(new Uri("https://auth.example.com"));

    
    // Define the scopes this server supports
    metadata.ScopesSupported.AddRange(["weather.read", "weather.write"]);
    
    // Add optional documentation
    metadata.ResourceDocumentation = new Uri("https://docs.example.com/api/weather");
});

// Configure authentication using the built-in authentication system
builder.Services.AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, SimpleAuthHandler>("Bearer", options => { });

// Add authorization policy for MCP
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("McpAuth", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "weather.read");
    });
});

var app = builder.Build();

// Set up the middleware pipeline
app.UseAuthentication();
app.UseAuthorization();

// Map MCP endpoints with authorization
app.MapMcp();

// Configure the server URL
app.Urls.Add("http://localhost:7071");

Console.WriteLine("Starting MCP server with authorization at http://localhost:7071");
Console.WriteLine("PRM Document URL: http://localhost:7071/.well-known/oauth-protected-resource");

Console.WriteLine();
Console.WriteLine("To test the server:");
Console.WriteLine("1. Use an MCP client that supports authorization");
Console.WriteLine("2. When prompted for authorization, enter 'valid_token' to gain access");
Console.WriteLine("3. Any other token value will be rejected with a 401 Unauthorized");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop the server");

await app.RunAsync();

// Simple auth handler that validates a test token
// In a real app, you'd use a JWT handler or other proper authentication
class SimpleAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public SimpleAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) 
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Get the Authorization header
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header missing"));
        }
        
        // Parse the token
        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Bearer token missing"));
        }
        
        var token = headerValue["Bearer ".Length..].Trim();
        
        // Validate the token - in a real app, this would validate a JWT
        if (token != "valid_token")
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid token"));
        }
        
        // Create a claims identity with required claims
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "demo_user"),
            new Claim(ClaimTypes.NameIdentifier, "user123"),
            new Claim("scope", "weather.read")
        };
        
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Bearer");
        
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
