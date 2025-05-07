using Microsoft.Extensions.DependencyInjection;
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

        var services = new ServiceCollection();
        services.AddHttpClient();
        
        var sharedHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
        };
        
        services.AddHttpClient(BasicOAuthAuthorizationProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => sharedHandler);
            
        services.AddHttpClient(AuthorizationHelpers.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => sharedHandler);
        
        services.AddTransient<AuthorizationHelpers>();
        
        var serviceProvider = services.BuildServiceProvider();
        
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var authorizationHelpers = serviceProvider.GetRequiredService<AuthorizationHelpers>();

        // Create the token provider with proper dependencies
        var tokenProvider = new BasicOAuthAuthorizationProvider(
            new Uri(serverUrl),
            httpClientFactory,
            authorizationHelpers,
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

            var transport = new SseClientTransport(transportOptions, tokenProvider);
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