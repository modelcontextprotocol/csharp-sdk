using System.Net.Http;
using System.Threading.Tasks;
using ModelContextProtocol.Auth;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace SecureWeatherClient;

class Program
{
    // The URI for our OAuth redirect - in a real app, this would be a registered URI or a local server
    private static readonly Uri RedirectUri = new("http://localhost:1170/oauth-callback");

    static async Task Main(string[] args)
    {
        Console.WriteLine("MCP Secure Weather Client with OAuth Authentication");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        // Create an HTTP client with OAuth handling
        var oauthHandler = new OAuthDelegatingHandler(
            clientId: "04f79824-ab56-4511-a7cb-d7deaea92dc0",
            redirectUri: RedirectUri,
            clientName: "SecureWeatherClient",
            scopes: ["weather.read"],
            authorizationHandler: HandleAuthorizationRequestAsync)
        {
            // The OAuth handler needs an inner handler
            InnerHandler = new HttpClientHandler()
        };

        var httpClient = new HttpClient(oauthHandler);
        var serverUrl = "http://localhost:7071/sse"; // Default server URL

        // Allow the user to specify a different server URL
        Console.WriteLine($"Server URL (press Enter for default: {serverUrl}):");
        var userInput = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(userInput))
        {
            serverUrl = userInput;
        }

        Console.WriteLine();
        Console.WriteLine($"Connecting to weather server at {serverUrl}...");

        try
        {
            // Create SseClientTransportOptions with the server URL
            var transportOptions = new SseClientTransportOptions
            {
                Endpoint = new Uri(serverUrl),
                Name = "Secure Weather Client"
            };

            // Create SseClientTransport with our authenticated HTTP client
            var transport = new SseClientTransport(transportOptions, httpClient);

            // Create an MCP client using the factory method with our transport
            var client = await McpClientFactory.CreateAsync(transport);

            // Get the list of available tools
            var tools = await client.ListToolsAsync();
            if (tools.Count == 0)
            {
                Console.WriteLine("No tools available on the server.");
                return;
            }

            Console.WriteLine($"Found {tools.Count} tools on the server.");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner error: {ex.InnerException.Message}");
            }
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    /// <summary>
    /// Handles the OAuth authorization request by showing the URL to the user and getting the authorization code.
    /// In a real application, this would launch a browser and listen for the callback.
    /// </summary>
    private static Task<string> HandleAuthorizationRequestAsync(Uri authorizationUri)
    {
        Console.WriteLine();
        Console.WriteLine("Authentication Required");
        Console.WriteLine("======================");
        Console.WriteLine();
        Console.WriteLine("Please open the following URL in your browser to authenticate:");
        Console.WriteLine(authorizationUri);
        Console.WriteLine();
        Console.WriteLine("After authentication, you will be redirected to a page with a code.");
        Console.WriteLine("Please enter the code parameter from the URL:");

        var authorizationCode = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            throw new InvalidOperationException("Authorization code is required.");
        }

        return Task.FromResult(authorizationCode);
    }
}