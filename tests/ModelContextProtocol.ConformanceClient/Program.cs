using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
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

var consoleLoggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
});

// Parse MCP_CONFORMANCE_CONTEXT environment variable for test context
// This may contain client_id, client_secret, private_key_pem, signing_algorithm for pre-registration tests
string? contextClientId = null;
string? contextClientSecret = null;
string? contextPrivateKeyPem = null;
string? contextSigningAlgorithm = null;
var conformanceContext = Environment.GetEnvironmentVariable("MCP_CONFORMANCE_CONTEXT");
if (!string.IsNullOrEmpty(conformanceContext))
{
    try
    {
        using var contextJson = JsonDocument.Parse(conformanceContext);

        if (contextJson.RootElement.TryGetProperty("client_id", out var clientIdProp))
        {
            contextClientId = clientIdProp.GetString();
        }

        if (contextJson.RootElement.TryGetProperty("client_secret", out var clientSecretProp))
        {
            contextClientSecret = clientSecretProp.GetString();
        }

        if (contextJson.RootElement.TryGetProperty("private_key_pem", out var privateKeyProp))
        {
            contextPrivateKeyPem = privateKeyProp.GetString();
        }

        if (contextJson.RootElement.TryGetProperty("signing_algorithm", out var signingAlgProp))
        {
            contextSigningAlgorithm = signingAlgProp.GetString();
        }
    }
    catch (JsonException)
    {
        // Ignore malformed context
    }
}

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

// Build OAuth options.
// For client_credentials tests, don't set a redirect handler to trigger machine-to-machine flow.
var isClientCredentialsTest = scenario.StartsWith("auth/client-credentials-");

var oauthOptions = new ClientOAuthOptions
{
    RedirectUri = clientRedirectUri,
    // Configure the metadata document URI for CIMD.
    ClientMetadataDocumentUri = new Uri("https://conformance-test.local/client-metadata.json"),
    DynamicClientRegistration = new()
    {
        ClientName = "ProtectedMcpClient",
    },
};

// Only set authorization redirect handler for tests that need authorization code flow.
// Client credentials tests should NOT have a redirect handler to trigger machine-to-machine flow.
if (!isClientCredentialsTest)
{
    oauthOptions.AuthorizationRedirectDelegate = (authUrl, redirectUri, ct) => HandleAuthorizationUrlAsync(authUrl, redirectUri, ct);
}

// If pre-registered credentials are provided via context, use them.
// This allows the OAuth provider to skip dynamic client registration and
// potentially use client_credentials grant type if the server supports it.
if (!string.IsNullOrEmpty(contextClientId))
{
    oauthOptions.ClientId = contextClientId;
    oauthOptions.ClientSecret = contextClientSecret;
}

// If JWT private key is provided (for private_key_jwt authentication), use it.
if (!string.IsNullOrEmpty(contextPrivateKeyPem) && !string.IsNullOrEmpty(contextSigningAlgorithm))
{
    oauthOptions.JwtPrivateKeyPem = contextPrivateKeyPem;
    oauthOptions.JwtSigningAlgorithm = contextSigningAlgorithm;
}

// Select transport mode based on scenario.
// sse-retry test requires SSE transport mode to test SSE-specific reconnection behavior.
var transportMode = scenario == "sse-retry" ? HttpTransportMode.Sse : HttpTransportMode.StreamableHttp;

var clientTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(endpoint),
    TransportMode = transportMode,
    OAuth = oauthOptions
}, loggerFactory: consoleLoggerFactory);

// Wrapper delegate pattern: allows setting elicitation handler after client creation
// This allows the actual handler to be set dynamically based on scenario
Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler = null;

McpClientOptions options = new()
{
    ClientInfo = new()
    {
        Name = "ConformanceClient",
        Version = "1.0.0"
    },
    Handlers = new()
    {
        ElicitationHandler = (request, cancellationToken) =>
        {
            if (elicitationHandler is not null)
            {
                return elicitationHandler(request, cancellationToken);
            }
            Console.WriteLine("No elicitation handler set, rejecting by default");
            return ValueTask.FromResult(new ElicitResult()); // default - reject
        }
    }
};

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
    case "auth/scope-step-up":
    {
        // Just testing that we can authenticate and list tools
        var tools = await mcpClient.ListToolsAsync();
        Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

        // Call the "test_tool" tool
        var toolName = tools.FirstOrDefault()?.Name ?? "test-tool";
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
        // For scope-retry-limit, the server will keep returning 403 with insufficient_scope
        // until the client gives up (tests the max retry limit).
        // The client should catch McpException when the retry limit is exceeded.
        try
        {
            var tools = await mcpClient.ListToolsAsync();
            Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

            // Call the "test_tool" tool
            var toolName = tools.FirstOrDefault()?.Name ?? "test-tool";
            Console.WriteLine($"Calling tool: {toolName}");
            var result = await mcpClient.CallToolAsync(toolName: toolName, arguments: new Dictionary<string, object?>
            {
                { "foo", "bar" },
            });
            success &= !(result.IsError == true);
        }
        catch (McpException ex) when (ex.Message.Contains("retry limit"))
        {
            // Expected - the client correctly limited scope step-up retries
            Console.WriteLine($"Scope step-up retry limit reached (expected): {ex.Message}");
        }
        break;
    }
    case "elicitation-sep1034-client-defaults":
    {
        // In this test scenario, an elicitation request will be made that includes default values in the schema.
        // The client should apply these defaults to demonstrate that it received and processed them correctly.

        // Set the elicitation handler dynamically for this scenario
        elicitationHandler = (request, cancellationToken) =>
        {
            Console.WriteLine($"Received elicitation request: {request?.Message}");

            // Apply default values from the schema
            var content = new Dictionary<string, JsonElement>();

            if (request?.RequestedSchema?.Properties is not null)
            {
                foreach (var (key, schema) in request.RequestedSchema.Properties)
                {
                    switch (schema)
                    {
                        case ElicitRequestParams.StringSchema stringSchema when stringSchema.Default is not null:
                            content[key] = JsonSerializer.SerializeToElement(stringSchema.Default, McpJsonUtilities.DefaultOptions);
                            break;
                        case ElicitRequestParams.NumberSchema numberSchema when numberSchema.Default.HasValue:
                            content[key] = JsonSerializer.SerializeToElement(numberSchema.Default.Value, McpJsonUtilities.DefaultOptions);
                            break;
                        case ElicitRequestParams.BooleanSchema booleanSchema when booleanSchema.Default.HasValue:
                            content[key] = JsonSerializer.SerializeToElement(booleanSchema.Default.Value, McpJsonUtilities.DefaultOptions);
                            break;
                        case ElicitRequestParams.UntitledSingleSelectEnumSchema enumSchema when enumSchema.Default is not null:
                            content[key] = JsonSerializer.SerializeToElement(enumSchema.Default, McpJsonUtilities.DefaultOptions);
                            break;
                        case ElicitRequestParams.TitledSingleSelectEnumSchema titledEnumSchema when titledEnumSchema.Default is not null:
                            content[key] = JsonSerializer.SerializeToElement(titledEnumSchema.Default, McpJsonUtilities.DefaultOptions);
                            break;
                    }
                }
            }

            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept", Content = content });
        };

        // Call the test_client_elicitation_defaults tool
        var testToolName = "test_client_elicitation_defaults";
        Console.WriteLine($"Calling tool: {testToolName}");
        var result = await mcpClient.CallToolAsync(toolName: testToolName, arguments: new Dictionary<string, object?>());
        Console.WriteLine($"Tool result: {result}");
        success &= !(result.IsError == true);

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
