using EntraProtectedMcpClient.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Web;

var builder = Host.CreateApplicationBuilder(args);

var configuration = builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

// Bind configuration sections to strongly typed objects
var mcpConfig = configuration.GetSection(McpClientConfiguration.SectionName).Get<McpClientConfiguration>();
var entraConfig = configuration.GetSection(EntraIdConfiguration.SectionName).Get<EntraIdConfiguration>();
var spoConfig = configuration.GetSection(SecuredSpoSiteConfiguration.SectionName).Get<SecuredSpoSiteConfiguration>();

// Validate required configuration
ValidateConfiguration(mcpConfig, entraConfig, spoConfig);

// Display startup information
DisplayStartupInfo(mcpConfig!, entraConfig!, spoConfig!);

// Create the Mcp Client
var client = await CreateMcpClient(mcpConfig!, entraConfig!);

var tools = await client.ListToolsAsync();

DisplayTools(tools);

await CallWeatherTool(tools, client);
await CallGraphTool(tools, client);
await CallSpoTool(spoConfig!, tools, client);


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
    Console.WriteLine($"Opening browser to Microsoft Entra ID: {authorizationUrl}");

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
        var errorDescription = query["error_description"];

        string responseHtml = "<html><body><h1>Authentication complete</h1><p>You can close this window now.</p></body></html>";
        byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentLength64 = buffer.Length;
        context.Response.ContentType = "text/html";
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();

        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"Auth error: {error}");
            if (!string.IsNullOrEmpty(errorDescription))
            {
                Console.WriteLine($"Error description: {errorDescription}");
            }
            return null;
        }

        if (string.IsNullOrEmpty(code))
        {
            Console.WriteLine("No authorization code received");
            return null;
        }

        Console.WriteLine("Authorization code received successfully from Microsoft Entra ID.");
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

/// <summary>
/// Validates the required configuration sections and throws exceptions if any required values are missing.
/// </summary>
/// <param name="mcpConfig">The MCP client configuration containing server connection details.</param>
/// <param name="entraConfig">The Microsoft Entra ID configuration containing authentication details.</param>
/// <param name="spoConfig">The secured SharePoint site configuration containing site URL.</param>
/// <exception cref="InvalidOperationException">Thrown when any required configuration value is missing or invalid.</exception>
static void ValidateConfiguration(McpClientConfiguration? mcpConfig, EntraIdConfiguration? entraConfig, SecuredSpoSiteConfiguration? spoConfig)
{
    if (mcpConfig?.Url is null)
    {
        throw new InvalidOperationException("McpServer:Url configuration is missing.");
    }

    if (entraConfig is null)
    {
        throw new InvalidOperationException("EntraId configuration section is missing.");
    }

    if (string.IsNullOrEmpty(entraConfig.ClientId))
    {
        throw new InvalidOperationException("EntraId:ClientId configuration is missing.");
    }

    if (string.IsNullOrEmpty(entraConfig.ClientSecret))
    {
        throw new InvalidOperationException("EntraId:ClientSecret configuration is required. Consider using user secrets or environment variables for production.");
    }

    if (string.IsNullOrEmpty(spoConfig?.Url))
    {
        throw new InvalidOperationException("SecuredSpoSite:Url configuration is missing.");
    }
}

/// <summary>
/// Displays startup information to the console, including server URLs and authentication details.
/// </summary>
/// <param name="mcpConfig">The MCP client configuration containing server connection details.</param>
/// <param name="entraConfig">The Microsoft Entra ID configuration containing authentication details.</param>
/// <param name="spoConfig">The secured SharePoint site configuration containing site URL.</param>
static void DisplayStartupInfo(McpClientConfiguration mcpConfig, EntraIdConfiguration entraConfig, SecuredSpoSiteConfiguration spoConfig)
{
    Console.WriteLine("Protected MCP Client");
    Console.WriteLine($"Connecting to MCP server at {mcpConfig.Url}...");
    Console.WriteLine($"Using Microsoft Entra ID tenant: {entraConfig.TenantId}");
    Console.WriteLine($"Client ID: {entraConfig.ClientId}");
    Console.WriteLine($"Secured SharePoint Site URL: {spoConfig.Url}");
    Console.WriteLine("Press Ctrl+C to stop the server");
}

/// <summary>
/// Creates and configures an MCP client with OAuth authentication for secure server communication.
/// </summary>
/// <param name="mcpConfig">The MCP client configuration containing server connection details.</param>
/// <param name="entraConfig">The Microsoft Entra ID configuration containing OAuth authentication details.</param>
/// <returns>A task that represents the asynchronous operation. The task result contains the configured <see cref="McpClient"/>.</returns>
static async Task<McpClient> CreateMcpClient(McpClientConfiguration mcpConfig, EntraIdConfiguration entraConfig)
{
    // We can customize a shared HttpClient with a custom handler if desired
    var sharedHandler = new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
    };
    var httpClient = new HttpClient(sharedHandler);

    var consoleLoggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
    });

    var transport = new HttpClientTransport(new()
    {
        Endpoint = new Uri(mcpConfig.Url),
        Name = "Secure MCP Client",
        OAuth = new()
        {
            ClientId = entraConfig.ClientId,
            ClientSecret = entraConfig.ClientSecret,
            RedirectUri = new Uri(entraConfig.RedirectUri),
            AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            Scopes = [
                $"api://{entraConfig.ServerClientId}/{entraConfig.Scope}"
            ],

            AdditionalAuthorizationParameters = new Dictionary<string, string>
            {
                ["tenant"] = entraConfig.TenantId,
                ["response_mode"] = "query"
            }
        }
    }, httpClient, consoleLoggerFactory);

    return await McpClient.CreateAsync(transport, loggerFactory: consoleLoggerFactory);
}

/// <summary>
/// Displays information about the available tools on the MCP server.
/// </summary>
/// <param name="tools">The list of tools retrieved from the MCP server.</param>
static void DisplayTools(IList<McpClientTool> tools)
{
    if (tools.Count == 0)
    {
        Console.WriteLine("No tools available on the server.");
        return;
    }

    Console.WriteLine($"Found {tools.Count} tools on the server.");
    Console.WriteLine();
}

/// <summary>
/// Calls the weather alerts tool if available on the server and displays the result.
/// </summary>
/// <param name="tools">The list of available tools from the MCP server.</param>
/// <param name="client">The MCP client instance used to invoke the tool.</param>
/// <returns>A task that represents the asynchronous tool invocation operation.</returns>
static async Task CallWeatherTool(IList<McpClientTool> tools, McpClient client)
{
    if (tools.Any(t => t.Name == "get_alerts"))
    {
        Console.WriteLine("Calling get_alerts tool...");

        var result = await client.CallToolAsync(
            "get_alerts",
            new Dictionary<string, object?> { { "state", "WA" } }
        );

        Console.WriteLine("Result: " + ((TextContentBlock)result.Content[0]).Text);
        Console.WriteLine();
    }
}

/// <summary>
/// Calls the Microsoft Graph hello tool if available on the server and displays the result.
/// </summary>
/// <param name="tools">The list of available tools from the MCP server.</param>
/// <param name="client">The MCP client instance used to invoke the tool.</param>
/// <returns>A task that represents the asynchronous tool invocation operation.</returns>
static async Task CallGraphTool(IList<McpClientTool> tools, McpClient client)
{
    if (tools.Any(t => t.Name == "hello"))
    {
        Console.WriteLine("Calling Hello tool...");

        var result = await client.CallToolAsync("hello");

        Console.WriteLine("Result: " + ((TextContentBlock)result.Content[0]).Text);
        Console.WriteLine();
    }
}

/// <summary>
/// Calls the SharePoint site information tool if available on the server and displays the result.
/// </summary>
/// <param name="spoConfig">The SharePoint site configuration containing the site URL.</param>
/// <param name="tools">The list of available tools from the MCP server.</param>
/// <param name="client">The MCP client instance used to invoke the tool.</param>
/// <returns>A task that represents the asynchronous tool invocation operation.</returns>
static async Task CallSpoTool(SecuredSpoSiteConfiguration spoConfig, IList<McpClientTool> tools, McpClient client)
{
    if (tools.Any(t => t.Name == "get_site_info"))
    {
        Console.WriteLine("Calling get_site_info tool...");
        var result = await client.CallToolAsync(
            "get_site_info",
            new Dictionary<string, object?>
            {
            { "siteUrl", spoConfig.Url }
            }
        );
        Console.WriteLine("Result: " + ((TextContentBlock)result.Content[0]).Text);
        Console.WriteLine();
    }
}