using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Web;

Console.WriteLine("Protected MCP Weather Server");
Console.WriteLine();

var serverUrl = "http://localhost:7071/";
var clientId = Environment.GetEnvironmentVariable("CLIENT_ID") ?? throw new Exception("The CLIENT_ID environment variable is not set.");

// We can customize a shared HttpClient with a custom handler if desired
var sharedHandler = new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
};

var httpClient = new HttpClient(sharedHandler);
// Create the token provider with our custom HttpClient and authorization URL handler
var tokenProvider = new GenericOAuthProvider(
    new Uri(serverUrl),
    httpClient,
    clientId: clientId,
    redirectUri: new Uri("http://localhost:1179/callback"),
    authorizationRedirectDelegate: HandleAuthorizationUrlAsync);

Console.WriteLine();
Console.WriteLine($"Connecting to weather server at {serverUrl}...");

try
{
    var transport = new SseClientTransport(new()
    {
        Endpoint = new Uri(serverUrl),
        Name = "Secure Weather Client",
        CredentialProvider = tokenProvider,
    });

    var client = await McpClientFactory.CreateAsync(transport);

    var tools = await client.ListToolsAsync();
    if (tools.Count == 0)
    {
        Console.WriteLine("No tools available on the server.");
        return;
    }

    Console.WriteLine($"Found {tools.Count} tools on the server.");
    Console.WriteLine();

    if (tools.Any(t => t.Name == "GetAlerts"))
    {
        Console.WriteLine("Calling GetAlerts tool...");

        var result = await client.CallToolAsync(
            "GetAlerts",
            new Dictionary<string, object?> { { "state", "WA" } }
        );

        Console.WriteLine("Result: " + ((TextContentBlock)result.Content[0]).Text);
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner error: {ex.InnerException.Message}");
    }

#if DEBUG
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
#endif
}
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

/// <summary>
/// Handles the OAuth authorization URL by starting a local HTTP server and opening a browser.
/// This implementation demonstrates how SDK consumers can provide their own authorization flow.
/// </summary>
/// <param name="authorizationUrl">The authorization URL to open in the browser.</param>
/// <param name="redirectUri">The redirect URI where the authorization code will be sent.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>The authorization code extracted from the callback, or null if the operation failed.</returns>
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
        Console.WriteLine($"Error opening browser. {ex.Message}");
    }
}