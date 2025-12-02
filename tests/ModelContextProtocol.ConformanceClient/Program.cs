using System.Diagnostics;
using System.Net;
using System.Text;
using System.Web;

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

switch (scenario)
{
    case "initialize":
        return await ToolsCall();
    case "tools_call":
        return await ToolsCall();
    case "auth/metadata-default":
        return await AuthMetadata();
    default:
        Console.WriteLine($"Unknown scenario: {scenario}");
        return 1;
}

// Implements the "tools_call" conformance test scenario
// We can also use this for the "initialize" scenario
async Task<int> ToolsCall()
{
    var clientTransport = new HttpClientTransport(new()
    {
        Endpoint = new Uri(endpoint),
        TransportMode = HttpTransportMode.StreamableHttp
    });

    McpClientOptions options = new()
    {
        ClientInfo = new()
        {
            Name = "ConformanceClient",
            Version = "1.0.0"
        }
    };

    await using var mcpClient = await McpClient.CreateAsync(clientTransport, options);

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

    // Exit code 0 on success, 1 on failure
    return result.IsError != true ? 0 : 1;
}

async Task<int> AuthMetadata() {
    var clientTransport = new HttpClientTransport(new()
    {
        Endpoint = new Uri(endpoint),
        TransportMode = HttpTransportMode.StreamableHttp,
        OAuth = new()
        {
            RedirectUri = new Uri("http://localhost:1179/callback"),
            AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            DynamicClientRegistration = new()
            {
                ClientName = "ProtectedMcpClient",
            },
        }
    });

    McpClientOptions options = new()
    {
        ClientInfo = new()
        {
            Name = "ConformanceClient",
            Version = "1.0.0"
        }
    };

    try {
        await using var mcpClient = await McpClient.CreateAsync(clientTransport, options);
        await mcpClient.PingAsync();
        return 0;
    } catch (Exception ex) {
        Console.WriteLine($"Error during Ping: {ex.Message}");
    }
    return 1;
}

// Copied from ProtectedMcpClient sample
static async Task<string?> HandleAuthorizationUrlAsync(Uri authorizationUrl, Uri redirectUri, CancellationToken cancellationToken)
{
    Console.WriteLine("Starting OAuth authorization flow...");
    Console.WriteLine($"Opening browser to: {authorizationUrl}");

    var listenerPrefix = redirectUri.GetLeftPart(UriPartial.Authority);
    if (!listenerPrefix.EndsWith("/")) listenerPrefix += "/";

    using var listener = new HttpListener();
    listener.Prefixes.Add(listenerPrefix);

    try
    {
        listener.Start();
        Console.WriteLine($"Listening for OAuth callback on: {listenerPrefix}");

        OpenBrowser(authorizationUrl);

        var context = await listener.GetContextAsync();
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
        if (listener.IsListening) listener.Stop();
    }
}

/// <summary>
/// Opens the specified URL in the default browser.
/// </summary>
/// <param name="url">The URL to open.</param>
static void OpenBrowser(Uri url)
{
    // Validate the URI scheme - only allow safe protocols
    if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
    {
        Console.WriteLine($"Error: Only HTTP and HTTPS URLs are allowed.");
        return;
    }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = url.ToString(),
            UseShellExecute = true
        };
        Process.Start(psi);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error opening browser: {ex.Message}");
        Console.WriteLine($"Please manually open this URL: {url}");
    }
}
