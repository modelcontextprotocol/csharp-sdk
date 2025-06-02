using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ProtectedMCPClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Protected MCP Weather Server");
        Console.WriteLine();

        var serverUrl = "http://localhost:7071/sse";

        // We can customize a shared HttpClient with a custom handler if desired
        var sharedHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
        };
        
        var httpClient = new HttpClient(sharedHandler);
        
        // Create the token provider with our custom HttpClient, 
        // letting the AuthorizationHelpers be created automatically
        var tokenProvider = new GenericOAuthProvider(
            new Uri(serverUrl),
            httpClient,
            null, // AuthorizationHelpers will be created automatically
            clientId: "6ad97b5f-7a7b-413f-8603-7a3517d4adb8",
            redirectUri: new Uri("http://localhost:1179/callback"),
            scopes: ["api://167b4284-3f92-4436-92ed-38b38f83ae08/weather.read"]
        );

        Console.WriteLine();
        Console.WriteLine($"Connecting to weather server at {serverUrl}...");

        try
        {
            var transportOptions = new SseClientTransportOptions
            {
                Endpoint = new Uri(serverUrl),
                Name = "Secure Weather Client"
            };

            // Create a transport with authentication support using the correct constructor parameters
            var transport = new SseClientTransport(
                transportOptions,
                tokenProvider
            );
            
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

            #if DEBUG
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            #endif
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}