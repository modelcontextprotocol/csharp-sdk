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
            var sessionInfo = GetSessionInfo(httpContext);

            // Get the tool collection that we can modify per session
            var toolCollection = mcpOptions.Capabilities?.Tools?.ToolCollection;
            if (toolCollection != null)
            {
                // Clear all tools first
                toolCollection.Clear();

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

            // Optional: Log the session configuration for debugging
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Configured MCP session for category '{ToolCategory}' from {SessionInfo}, {ToolCount} tools available", 
                toolCategory, sessionInfo, toolCollection?.Count ?? 0);
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

// Add middleware to log requests for demo purposes
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var toolCategory = GetToolCategoryFromRoute(context);
    var sessionInfo = GetSessionInfo(context);
    
    logger.LogInformation("Request for category '{ToolCategory}' from {SessionInfo}: {Method} {Path}", 
        toolCategory, sessionInfo, context.Request.Method, context.Request.Path);
    
    await next();
});

// Map MCP with route parameter for tool category filtering
app.MapMcp("/{toolCategory?}");

// Add endpoints to test different tool categories
app.MapGet("/", () => Results.Text(
    "MCP Per-Session Tools Demo\n" +
    "=========================\n" +
    "Available endpoints:\n" +
    "- /clock - MCP server with clock/time tools\n" +
    "- /calculator - MCP server with calculation tools\n" +
    "- /userinfo - MCP server with session/system info tools\n" +
    "- /all - MCP server with all tools (default)\n" +
    "\n" +
    "Test routes:\n" +
    "- /test-category/{category} - Test category detection\n"
));

app.MapGet("/test-category/{toolCategory?}", (string? toolCategory, HttpContext context) =>
{
    var detectedCategory = GetToolCategoryFromRoute(context);
    var sessionInfo = GetSessionInfo(context);
    
    return Results.Text($"Tool Category: {detectedCategory ?? "all (default)"}\n" +
                       $"Session Info: {sessionInfo}\n" +
                       $"Route Parameter: {toolCategory ?? "none"}\n" +
                       $"Message: MCP session would be configured for '{detectedCategory ?? "all"}' tools");
});

app.Run();

// Helper methods for route-based tool category detection
static string? GetToolCategoryFromRoute(HttpContext context)
{
    // Try to get tool category from route values
    if (context.Request.RouteValues.TryGetValue("toolCategory", out var categoryObj) && categoryObj is string category)
    {
        return string.IsNullOrEmpty(category) ? "all" : category;
    }
    
    // Fallback: try to extract from path
    var path = context.Request.Path.Value?.Trim('/');
    if (!string.IsNullOrEmpty(path))
    {
        var segments = path.Split('/');
        if (segments.Length > 0)
        {
            var firstSegment = segments[0].ToLower();
            if (firstSegment is "clock" or "calculator" or "userinfo" or "all")
            {
                return firstSegment;
            }
        }
    }
    
    // Default to "all" if no category specified
    return "all";
}

static string GetSessionInfo(HttpContext context)
{
    var userAgent = context.Request.Headers.UserAgent.ToString();
    var clientInfo = !string.IsNullOrEmpty(userAgent) ? userAgent[..Math.Min(userAgent.Length, 20)] + "..." : "Unknown";
    var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    
    return $"{clientInfo} ({remoteIp})";
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