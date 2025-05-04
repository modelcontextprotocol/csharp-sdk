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

        var tokenProvider = new SimpleAccessTokenProvider(new Uri(serverUrl));
        var httpClient = new HttpClient().UseMcpAuthorizationProvider(tokenProvider);

        Console.WriteLine();
        Console.WriteLine($"Connecting to weather server at {serverUrl}...");

        try
        {
            var transportOptions = new SseClientTransportOptions
            {
                Endpoint = new Uri(serverUrl),
                Name = "Secure Weather Client"
            };

            var transport = new SseClientTransport(transportOptions, httpClient);

            var client = await McpClientFactory.CreateAsync(transport);

            var tools = await client.ListToolsAsync();
            if (tools.Count == 0)
            {
                Console.WriteLine("No tools available on the server.");
                return;
            }

            Console.WriteLine($"Found {tools.Count} tools on the server.");
            Console.WriteLine();

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