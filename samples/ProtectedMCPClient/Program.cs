using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace ProtectedMCPClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Protected MCP Weather Server");
        Console.WriteLine();

        var serverUrl = "http://localhost:7071/sse";

        var httpClient = new HttpClient();

        var tokenProvider = new BasicOAuthAuthorizationProvider(
            new Uri(serverUrl), 
            clientId: "6ad97b5f-7a7b-413f-8603-7a3517d4adb8",
            redirectUri: new Uri("http://localhost:1179/callback"),
            scopes: new List<string> { "api://167b4284-3f92-4436-92ed-38b38f83ae08/weather.read" },
            httpClient: httpClient);

        // Use the same HttpClient instance with the authentication provider
        var authenticatedClient = httpClient.UseMcpAuthorizationProvider(tokenProvider);

        Console.WriteLine();
        Console.WriteLine($"Connecting to weather server at {serverUrl}...");

        try
        {
            var transportOptions = new SseClientTransportOptions
            {
                Endpoint = new Uri(serverUrl),
                Name = "Secure Weather Client"
            };

            var transport = new SseClientTransport(transportOptions, authenticatedClient);

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
                // Update the dictionary to match the expected type IReadOnlyDictionary<string, object?>?
                var result = await client.CallToolAsync(
                    "GetAlerts",
                    new Dictionary<string, object?> { { "state", "WA" } }
                );

                //var result = await client.CallToolAsync("GetAuthorizationInfo");
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