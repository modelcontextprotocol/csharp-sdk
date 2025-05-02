using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Auth;
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
    metadata.AuthorizationServers.Add(new Uri("https://login.microsoftonline.com/a2213e1c-e51e-4304-9a0d-effe57f31655/v2.0"));
    metadata.BearerMethodsSupported.Add("header");
    metadata.ScopesSupported.AddRange(["weather.read", "weather.write"]);
    
    // Add optional documentation
    metadata.ResourceDocumentation = new Uri("https://docs.example.com/api/weather");
});

// Configure authentication using the built-in authentication system
builder.Services.AddAuthentication(options => 
{
    options.DefaultScheme = "Bearer";
    options.DefaultChallengeScheme = "Bearer"; // Ensure challenges use Bearer scheme
})
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

app.UseAuthentication();
app.UseMcpAuthenticationResponse();
app.UseAuthorization();

// Map MCP endpoints with authorization
app.MapMcp();

// Configure the server URL
app.Urls.Add("http://localhost:7071");

Console.WriteLine("Starting MCP server with authorization at http://localhost:7071");
Console.WriteLine("PRM Document URL: http://localhost:7071/.well-known/oauth-protected-resource");

Console.WriteLine();
Console.WriteLine("Testing mode: Server will accept ANY non-empty token for authentication");
Console.WriteLine();
Console.WriteLine("To test the server:");
Console.WriteLine("1. Use an MCP client that supports authorization");
Console.WriteLine("2. The server will accept any non-empty token sent by the client");
Console.WriteLine("3. Tokens will be logged to the console for debugging");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop the server");

await app.RunAsync();

// Simple auth handler that accepts any non-empty token for testing
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
        
        // Accept any non-empty token for testing purposes
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Token cannot be empty"));
        }
        
        // Log the received token for debugging
        Console.WriteLine($"Received and accepted token: {token}");
        
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

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // No need to manually set WWW-Authenticate header anymore - handled by middleware
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }
}
