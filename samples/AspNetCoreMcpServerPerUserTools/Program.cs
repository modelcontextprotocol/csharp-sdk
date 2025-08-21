using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using AspNetCoreMcpServerPerUserTools.Tools;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Register all MCP server tools - they will be filtered per user later
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Configure per-session options to filter tools based on user permissions
        options.ConfigureSessionOptions = async (httpContext, mcpOptions, cancellationToken) =>
        {
            // Determine user role from headers (in real apps, use proper authentication)
            var userRole = GetUserRole(httpContext);
            var userId = GetUserId(httpContext);

            // Get the tool collection that we can modify per session
            var toolCollection = mcpOptions.Capabilities?.Tools?.ToolCollection;
            if (toolCollection != null)
            {
                // Clear all tools first
                toolCollection.Clear();

                // Add tools based on user role
                switch (userRole)
                {
                    case "admin":
                        // Admins get all tools
                        AddToolsForType<PublicTool>(toolCollection);
                        AddToolsForType<UserTool>(toolCollection);  
                        AddToolsForType<AdminTool>(toolCollection);
                        break;
                        
                    case "user":
                        // Regular users get public and user tools
                        AddToolsForType<PublicTool>(toolCollection);
                        AddToolsForType<UserTool>(toolCollection);
                        break;
                        
                    default:
                        // Anonymous/public users get only public tools
                        AddToolsForType<PublicTool>(toolCollection);
                        break;
                }
            }

            // Optional: Log the session configuration for debugging
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Configured MCP session for user {UserId} with role {UserRole}, {ToolCount} tools available", 
                userId, userRole, toolCollection?.Count ?? 0);
        };
    })
    .WithTools<PublicTool>()
    .WithTools<UserTool>()
    .WithTools<AdminTool>();

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
    var userRole = GetUserRole(context);
    var userId = GetUserId(context);
    
    logger.LogInformation("Request from User {UserId} with Role {UserRole}: {Method} {Path}", 
        userId, userRole, context.Request.Method, context.Request.Path);
    
    await next();
});

app.MapMcp();

// Add a simple endpoint to test authentication headers
app.MapGet("/test-auth", (HttpContext context) =>
{
    var userRole = GetUserRole(context);
    var userId = GetUserId(context);
    
    return Results.Text($"UserId: {userId}\nRole: {userRole}\nMessage: You are authenticated as {userId} with role {userRole}");
});

app.Run();

// Helper methods for authentication - in production, use proper authentication/authorization
static string GetUserRole(HttpContext context)
{
    // Check for X-User-Role header first
    if (context.Request.Headers.TryGetValue("X-User-Role", out var roleHeader))
    {
        var role = roleHeader.ToString().ToLowerInvariant();
        if (role is "admin" or "user" or "public")
        {
            return role;
        }
    }
    
    // Check for Authorization header pattern (Bearer token simulation)
    if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        var auth = authHeader.ToString();
        if (auth.StartsWith("Bearer admin-", StringComparison.OrdinalIgnoreCase))
            return "admin";
        if (auth.StartsWith("Bearer user-", StringComparison.OrdinalIgnoreCase))
            return "user";
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return "public";
    }
    
    // Default to public access
    return "public";
}

static string GetUserId(HttpContext context)
{
    // Check for X-User-Id header first
    if (context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader))
    {
        return userIdHeader.ToString();
    }
    
    // Extract from Authorization header if present
    if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        var auth = authHeader.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = auth["Bearer ".Length..];
            return token.Contains('-') ? token : $"user-{token}";
        }
    }
    
    // Generate anonymous ID
    return $"anonymous-{Guid.NewGuid():N}"[..16];
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