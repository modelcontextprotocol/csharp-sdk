using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using AspNetCoreMcpPerSessionTools.Tools;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Create and populate the tool dictionary at startup
var toolDictionary = new ConcurrentDictionary<string, McpServerTool[]>();
PopulateToolDictionary(toolDictionary);

// Register all MCP server tools - they will be filtered per session based on route
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Configure per-session options to filter tools based on route category
        options.ConfigureSessionOptions = async (httpContext, mcpOptions, cancellationToken) =>
        {            
            // Determine tool category from route parameters
            var toolCategory = GetToolCategoryFromRoute(httpContext);

            // Get the tool collection that we can modify per session
            var toolCollection = mcpOptions.Capabilities?.Tools?.ToolCollection;
            if (toolCollection == null)
            {
                return;
            }

            // Clear tools (we add them to make sure the capability is initialized)
            toolCollection.Clear();

            // Get pre-populated tools for the requested category
            if (toolDictionary.TryGetValue(toolCategory.ToLower(), out var tools))
            {
                foreach (var tool in tools)
                {
                    toolCollection.Add(tool);
                }
            }
        };
    })
    .WithTools<ClockTool>()
    .WithTools<CalculatorTool>()
    .WithTools<UserInfoTool>();

// Add OpenTelemetry for observability
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(b => b.AddMeter("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

// Map MCP with route parameter for tool category filtering
app.MapMcp("/{toolCategory?}");

app.Run();

// Helper method for route-based tool category detection
static string GetToolCategoryFromRoute(HttpContext context)
{
    // Try to get tool category from route values
    if (context.Request.RouteValues.TryGetValue("toolCategory", out var categoryObj) && categoryObj is string category)
    {
        return string.IsNullOrEmpty(category) ? "all" : category;
    }

    // Default to "all" if no category specified or empty
    return "all";
}

// Helper method to populate the tool dictionary at startup
static void PopulateToolDictionary(ConcurrentDictionary<string, McpServerTool[]> toolDictionary)
{
    // Get tools for each category
    var clockTools = GetToolsForType<ClockTool>();
    var calculatorTools = GetToolsForType<CalculatorTool>();
    var userInfoTools = GetToolsForType<UserInfoTool>();
    McpServerTool[] allTools = [.. clockTools,
                                .. calculatorTools,
                                .. userInfoTools];

    // Populate the dictionary with tools for each category
    toolDictionary.TryAdd("clock", clockTools);
    toolDictionary.TryAdd("calculator", calculatorTools);
    toolDictionary.TryAdd("userinfo", userInfoTools);
    toolDictionary.TryAdd("all", allTools);
}

// Helper method to get tools for a specific type using reflection
static McpServerTool[] GetToolsForType<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)] T>()
{
    var tools = new List<McpServerTool>();
    var toolType = typeof(T);
    var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Any());

    foreach (var method in methods)
    {
        try
        {
            var tool = McpServerTool.Create(method, target: null, new McpServerToolCreateOptions());
            tools.Add(tool);
        }
        catch (Exception ex)
        {
            // Log error but continue with other tools
            Console.WriteLine($"Failed to add tool {toolType.Name}.{method.Name}: {ex.Message}");
        }
    }

    return [.. tools];
}