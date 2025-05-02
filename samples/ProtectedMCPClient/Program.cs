using System.Net.Http;
using System.Threading.Tasks;
using ModelContextProtocol.Auth;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace ProtectedMCPClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("MCP Secure Weather Client with OAuth Authentication");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        // Create the authorization config with HTTP listener
        var authConfig = new AuthorizationConfig
        {
            ClientId = "04f79824-ab56-4511-a7cb-d7deaea92dc0",
            Scopes = ["User.Read"]
        }.UseHttpListener(hostname: "localhost", listenPort: 1170);

        // Create an HTTP client with OAuth handling
        var oauthHandler = new OAuthDelegatingHandler(
            redirectUri: authConfig.RedirectUri,
            clientId: authConfig.ClientId,
            clientName: authConfig.ClientName,
            scopes: authConfig.Scopes,
            authorizationHandler: authConfig.AuthorizationHandler)
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
        Console.WriteLine("When prompted for authorization, a browser window will open automatically.");
        Console.WriteLine("Complete the authentication in the browser, and this application will continue automatically.");
        Console.WriteLine();

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

            // Call the protected-data tool which requires authentication
            if (tools.Any(t => t.Name == "protected-data"))
            {
                Console.WriteLine("Calling protected-data tool...");
                var result = await client.CallToolAsync("protected-data");
                Console.WriteLine("Result: " + result.Content[0].Text);
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
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}