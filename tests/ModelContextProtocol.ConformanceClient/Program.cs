using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// This program expects the following command-line arguments:
// 1. The client conformance test scenario to run (e.g., "tools_call")
// 2. The endpoint URL (e.g., "http://localhost:3001")

if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run --project ModelContextProtocol.ConformanceClient.csproj <scenario> [endpoint]");
    return 1;
}

var scenario = args[0];
var endpoint =  args[1];

McpClientOptions options = new()
{
    ClientInfo = new()
    {
        Name = "ConformanceClient",
        Version = "1.0.0"
    },
    Handlers = new()
    {
        ElicitationHandler = (request, ct) =>
        {
            // Accept with empty content; the SDK applies schema defaults automatically.
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept", Content = new Dictionary<string, System.Text.Json.JsonElement>() });
        },
    },
};

var consoleLoggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
});

// Configure OAuth callback port via environment or pick an ephemeral port.
var callbackPortEnv = Environment.GetEnvironmentVariable("OAUTH_CALLBACK_PORT");
int callbackPort = 0;
if (!string.IsNullOrEmpty(callbackPortEnv) && int.TryParse(callbackPortEnv, out var parsedPort))
{
    callbackPort = parsedPort;
}

if (callbackPort == 0)
{
    var tcp = new TcpListener(IPAddress.Loopback, 0);
    tcp.Start();
    callbackPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
    tcp.Stop();
}

var clientRedirectUri = new Uri($"http://localhost:{callbackPort}/callback");

// Read conformance context for scenarios that provide additional data (e.g., pre-registered credentials).
string? preRegisteredClientId = null;
string? preRegisteredClientSecret = null;
var conformanceContext = Environment.GetEnvironmentVariable("MCP_CONFORMANCE_CONTEXT");
if (!string.IsNullOrEmpty(conformanceContext))
{
    using var doc = JsonDocument.Parse(conformanceContext);
    if (doc.RootElement.TryGetProperty("client_id", out var clientIdEl))
    {
        preRegisteredClientId = clientIdEl.GetString();
    }
    if (doc.RootElement.TryGetProperty("client_secret", out var clientSecretEl))
    {
        preRegisteredClientSecret = clientSecretEl.GetString();
    }
}

var oauthOptions = new ModelContextProtocol.Authentication.ClientOAuthOptions
{
    RedirectUri = clientRedirectUri,
    // Configure the metadata document URI for CIMD.
    ClientMetadataDocumentUri = new Uri("https://conformance-test.local/client-metadata.json"),
    AuthorizationRedirectDelegate = (authUrl, redirectUri, ct) => HandleAuthorizationUrlAsync(authUrl, redirectUri, ct),
};

if (preRegisteredClientId is not null)
{
    // Use pre-registered credentials instead of DCR.
    oauthOptions.ClientId = preRegisteredClientId;
    oauthOptions.ClientSecret = preRegisteredClientSecret;
}
else
{
    oauthOptions.DynamicClientRegistration = new()
    {
        ClientName = "ProtectedMcpClient",
    };
}

var clientTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
    OAuth = oauthOptions,
}, loggerFactory: consoleLoggerFactory);

await using var mcpClient = await McpClient.CreateAsync(clientTransport, options, loggerFactory: consoleLoggerFactory);

bool success = true;

switch (scenario)
{
    case "tools_call":
    {
        var tools = await mcpClient.ListToolsAsync();
        Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

        // Call the "add_numbers" tool
        var toolName = "add_numbers";
        Console.WriteLine($"Calling tool: {toolName}");
        var result = await mcpClient.CallToolAsync(toolName: toolName, arguments: new Dictionary<string, object?>
        {
            { "a", 5 },
            { "b", 10 }
        });
        success &= !(result.IsError == true);
        break;
    }
    case "elicitation-sep1034-client-defaults":
    {
        var tools = await mcpClient.ListToolsAsync();
        Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");
        var toolName = "test_client_elicitation_defaults";
        Console.WriteLine($"Calling tool: {toolName}");
        var result = await mcpClient.CallToolAsync(toolName: toolName, arguments: new Dictionary<string, object?>());
        success &= !(result.IsError == true);
        break;
    }
    case "sse-retry":
    {
        var tools = await mcpClient.ListToolsAsync();
        Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");
        var toolName = "test_reconnection";
        Console.WriteLine($"Calling tool: {toolName}");
        var result = await mcpClient.CallToolAsync(toolName: toolName, arguments: new Dictionary<string, object?>());
        success &= !(result.IsError == true);
        break;
    }
    case "auth/scope-step-up":
    {
        // Just testing that we can authenticate and list tools
        var tools = await mcpClient.ListToolsAsync();
        Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

        // Call the "test_tool" tool
        var toolName = "test-tool";
        Console.WriteLine($"Calling tool: {toolName}");
        var result = await mcpClient.CallToolAsync(toolName: toolName, arguments: new Dictionary<string, object?>
        {
            { "foo", "bar" },
        });
        success &= !(result.IsError == true);
        break;
    }
    case "auth/scope-retry-limit":
    {
        // Try to list tools - this triggers the auth flow that always fails with 403.
        // The test validates the client doesn't retry indefinitely.
        try
        {
            await mcpClient.ListToolsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Expected auth failure: {ex.Message}");
        }
        break;
    }
    default:
        // No extra processing for other scenarios
        break;
}

// Exit code 0 on success, 1 on failure
return success ? 0 : 1;

// Copied from ProtectedMcpClient sample
// Simulate a user opening the browser and logging in
// Copied from OAuthTestBase
static async Task<string?> HandleAuthorizationUrlAsync(Uri authorizationUrl, Uri redirectUri, CancellationToken cancellationToken)
{
    Console.WriteLine("Starting OAuth authorization flow...");
    Console.WriteLine($"Simulating opening browser to: {authorizationUrl}");

    using var handler = new HttpClientHandler()
    {
        AllowAutoRedirect = false,
    };
    using var httpClient = new HttpClient(handler);
    using var redirectResponse = await httpClient.GetAsync(authorizationUrl, cancellationToken);
    var location = redirectResponse.Headers.Location;

    if (location is not null && !string.IsNullOrEmpty(location.Query))
    {
        // Parse query string to extract "code" parameter
        var query = location.Query.TrimStart('?');
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "code")
            {
                return HttpUtility.UrlDecode(parts[1]);
            }
        }
    }

    return null;
}
