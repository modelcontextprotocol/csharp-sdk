using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using AspNetCoreMcpPerSessionTools.Tools;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

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
            if (toolCollection != null)
            {
                // Add tools based on the requested category
                switch (toolCategory?.ToLower())
                {
                    case "clock":
                        // Clock category gets time/date tools
                        AddToolsForType<ClockTool>(toolCollection);
                        break;
                        
                    case "calculator":
                        // Calculator category gets mathematical tools
                        AddToolsForType<CalculatorTool>(toolCollection);
                        break;
                        
                    case "userinfo":
                        // UserInfo category gets session and system information tools
                        AddToolsForType<UserInfoTool>(toolCollection);
                        break;
                        
                    case "all":
                    default:
                        // Default or "all" category gets all tools
                        AddToolsForType<ClockTool>(toolCollection);
                        AddToolsForType<CalculatorTool>(toolCollection);
                        AddToolsForType<UserInfoTool>(toolCollection);
                        break;
                }
            }
        };
    });

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

// Helper methods for route-based tool category detection
static string? GetToolCategoryFromRoute(HttpContext context)
{
    // Try to get tool category from route values
    if (context.Request.RouteValues.TryGetValue("toolCategory", out var categoryObj) && categoryObj is string category)
    {
        return string.IsNullOrEmpty(category) ? "all" : category;
    }

    // Default to "all" if no category specified or empty
    return "all";
}

static void AddToolsForType<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]T>(
    McpServerPrimitiveCollection<McpServerTool> toolCollection)
{
    var toolType = typeof(T);
    var methods = toolType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Any());
    
    foreach (var method in methods)
    {
        try
        {
            var tool = McpServerTool.Create(method, target: null, new McpServerToolCreateOptions());
            toolCollection.Add(tool);
        }
        catch (Exception ex)
        {
            // Log error but continue with other tools
            Console.WriteLine($"Failed to add tool {toolType.Name}.{method.Name}: {ex.Message}");
        }
    }
}