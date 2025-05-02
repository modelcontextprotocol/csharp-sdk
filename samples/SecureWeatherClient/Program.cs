// filepath: c:\Users\ddelimarsky\source\csharp-sdk-anm\samples\SecureWeatherClient\Program.cs
using System.Net.Http;
using System.Threading.Tasks;
using ModelContextProtocol.Auth;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace SecureWeatherClient;

class Program
{
    // The URI for our OAuth redirect - in a real app, this would be a registered URI or a local server
    private static readonly Uri RedirectUri = new("http://localhost:8888/oauth-callback");

    static async Task Main(string[] args)
    {
        Console.WriteLine("MCP Secure Weather Client with OAuth Authentication");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        // Create an HTTP client with OAuth handling
        var oauthHandler = new OAuthDelegatingHandler(
            redirectUri: RedirectUri,
            clientName: "SecureWeatherClient", 
            scopes: new[] { "weather.read" },
            authorizationHandler: HandleAuthorizationRequestAsync);

        var httpClient = new HttpClient(oauthHandler);
        var serverUrl = "http://localhost:5000"; // Default server URL
        
        // Allow the user to specify a different server URL
        Console.WriteLine($"Server URL (press Enter for default: {serverUrl}):");
        var userInput = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(userInput))
        {
            serverUrl = userInput;
        }
        
        Console.WriteLine();
        Console.WriteLine($"Connecting to weather server at {serverUrl}...");
        
        // Create an MCP client with the server URL
        var client = new McpClient(new Uri(serverUrl), httpClient);

        try
        {
            // Get the list of available tools
            var tools = await client.GetToolsAsync();
            if (tools.Count == 0)
            {
                Console.WriteLine("No tools available on the server.");
                return;
            }

            Console.WriteLine($"Found {tools.Count} tools on the server.");
            Console.WriteLine();

            // Find the weather tool
            var weatherTool = tools.FirstOrDefault(t => t.Name == "get_weather");
            if (weatherTool == null)
            {
                Console.WriteLine("The server does not provide a weather tool.");
                return;
            }

            // Get the weather for different locations
            string[] locations = { "New York", "London", "Tokyo", "Sydney", "Moscow" };

            foreach (var location in locations)
            {
                try
                {
                    Console.WriteLine($"Getting weather for {location}...");
                    var result = await client.InvokeToolAsync(weatherTool.Name, new Dictionary<string, object>
                    {
                        ["location"] = location
                    });

                    if (result.TryGetValue("temperature", out var temperature) &&
                        result.TryGetValue("conditions", out var conditions) &&
                        result.TryGetValue("humidity", out var humidity) &&
                        result.TryGetValue("windSpeed", out var windSpeed))
                    {
                        Console.WriteLine($"Weather in {location}:");
                        Console.WriteLine($"  Temperature: {temperature}°C");
                        Console.WriteLine($"  Conditions: {conditions}");
                        Console.WriteLine($"  Humidity: {humidity}%");
                        Console.WriteLine($"  Wind speed: {windSpeed} km/h");
                    }
                    else
                    {
                        Console.WriteLine($"Invalid response format for {location}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting weather for {location}: {ex.Message}");
                }

                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
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