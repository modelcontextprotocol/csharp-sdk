using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using System.Diagnostics;

namespace ProtectedMCPClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("MCP Secure Weather Client with Authentication");
        Console.WriteLine("==============================================");
        Console.WriteLine();

        // Create a standard HttpClient with authentication configured
        var serverUrl = "http://localhost:7071/sse"; // Default server URL

        // Ask for the API key
        Console.WriteLine("Enter your API key (or press Enter to use default):");
        var apiKey = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = "demo-api-key-12345"; // Default API key for demonstration
            Console.WriteLine($"Using default API key: {apiKey}");
        }

        // Allow the user to specify a different server URL
        Console.WriteLine($"Server URL (press Enter for default: {serverUrl}):");
        var userInput = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(userInput))
        {
            serverUrl = userInput;
        }

        // Create a single HttpClient with authentication configured
        var tokenProvider = new SimpleAccessTokenProvider(apiKey, new Uri(serverUrl));
        var httpClient = new HttpClient().UseAuthenticationProvider(tokenProvider);

        Console.WriteLine();
        Console.WriteLine($"Connecting to weather server at {serverUrl}...");
        Console.WriteLine("When prompted for authorization, the challenge will be verified automatically.");
        Console.WriteLine("If required, you'll be guided through any necessary authentication steps.");
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
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Handle authentication failures specifically
            Console.WriteLine("Authentication failed. The server returned a 401 Unauthorized response.");
            Console.WriteLine($"Details: {ex.Message}");
            
            // Additional handling for 401 - could add manual authentication retry here
            Console.WriteLine("You might need to provide a different API key or authentication credentials.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner error: {ex.InnerException.Message}");
            }
            
            // Print stack trace in debug builds
            #if DEBUG
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            #endif
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}