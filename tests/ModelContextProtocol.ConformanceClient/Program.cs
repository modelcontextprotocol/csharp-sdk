using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

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
    }
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

var listenerPrefix = $"http://localhost:{callbackPort}/";
var preStartedListener = new HttpListener();
preStartedListener.Prefixes.Add(listenerPrefix);
preStartedListener.Start();

var clientRedirectUri = new Uri($"http://localhost:{callbackPort}/callback");

var clientTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
    OAuth = new()
    {
        RedirectUri = clientRedirectUri,
        AuthorizationRedirectDelegate = (authUrl, redirectUri, ct) => HandleAuthorizationUrlWithListenerAsync(authUrl, redirectUri, preStartedListener, ct),
        DynamicClientRegistration = new()
        {
            ClientName = "ProtectedMcpClient",
        },
    }
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
    default:
        // No extra processing for other scenarios
        break;
}

// Exit code 0 on success, 1 on failure
return success ? 0 : 1;

// Copied from ProtectedMcpClient sample
static async Task<string?> HandleAuthorizationUrlWithListenerAsync(Uri authorizationUrl, Uri redirectUri, HttpListener listener, CancellationToken cancellationToken)
{
    Console.WriteLine("Starting OAuth authorization flow...");
    Console.WriteLine($"Opening browser to: {authorizationUrl}");

    try
    {
        _ = OpenBrowserAsync(authorizationUrl);

        Console.WriteLine($"Listening for OAuth callback on: {listener.Prefixes.Cast<string>().FirstOrDefault()}");
        var contextTask = listener.GetContextAsync();
        var context = await contextTask.WaitAsync(cancellationToken);
        var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
        var code = query["code"];
        var error = query["error"];

        string responseHtml = "<html><body><h1>Authentication complete</h1><p>You can close this window now.</p></body></html>";
        byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentLength64 = buffer.Length;
        context.Response.ContentType = "text/html";
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();

        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"Auth error: {error}");
            return null;
        }

        if (string.IsNullOrEmpty(code))
        {
            Console.WriteLine("No authorization code received");
            return null;
        }

        Console.WriteLine("Authorization code received successfully.");
        return code;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error getting auth code: {ex.Message}");
        return null;
    }
    finally
    {
        try { if (listener.IsListening) listener.Stop(); } catch { }
    }
}

// Simulate a user opening the browser and logging in
static async Task OpenBrowserAsync(Uri url)
{
    // Validate the URI scheme - only allow safe protocols
    if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
    {
        Console.WriteLine($"Error: Only HTTP and HTTPS URLs are allowed.");
        return;
    }

    try
    {
        using var httpClient = new HttpClient();
        using var authResponse = await httpClient.GetAsync(url);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error opening browser: {ex.Message}");
        Console.WriteLine($"Please manually open this URL: {url}");
    }
}
