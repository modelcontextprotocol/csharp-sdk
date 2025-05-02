// filepath: c:\Users\ddelimarsky\source\csharp-sdk-anm\samples\SecureWeatherServer\Program.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Auth;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Add MCP server with OAuth authorization
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithOAuthAuthorization(metadata =>
    {
        // Configure the resource metadata
        metadata.AuthorizationServers.Add(new Uri("https://auth.example.com"));
        metadata.ScopesSupported.AddRange(new[] { "weather.read", "weather.write" });
        metadata.ResourceDocumentation = new Uri("https://docs.example.com/api/weather");
    });

// Build the app
var app = builder.Build();

// Enable CORS for development
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

// Configure the HTTP request pipeline
app.UseAuthentication();
app.UseAuthorization();

// Map MCP endpoints with authorization
app.MapMcpWithAuthorization();

// Define weather tool
var weatherTool = new McpTool("get_weather", "Get the current weather for a location")
    .WithParameter("location", "The location to get the weather for", typeof(string), required: true);

// Define weather server logic
app.UseMiddleware<ServerInvokeMiddleware>(options =>
{
    options.RegisterTool(weatherTool, async (McpToolInvokeParameters parameters, CancellationToken ct) =>
    {
        if (!parameters.TryGetParameterValue<string>("location", out var location))
        {
            return McpToolResult.Error("Location parameter is required");
        }

        // In a real implementation, you would get the weather for the location
        // For this example, we'll just return a random weather
        var weather = GetRandomWeather(location);
        
        return McpToolResult.Success(new
        {
            location,
            temperature = weather.Temperature,
            conditions = weather.Conditions,
            humidity = weather.Humidity,
            windSpeed = weather.WindSpeed
        });
    });
});

// Run the app
app.Run();

// Helper method to generate random weather
(double Temperature, string Conditions, int Humidity, double WindSpeed) GetRandomWeather(string location)
{
    var random = new Random();
    var conditions = new[] { "Sunny", "Cloudy", "Rainy", "Snowy", "Foggy", "Windy" };
    
    return (
        Temperature: Math.Round(random.NextDouble() * 40 - 10, 1), // -10 to 30 degrees
        Conditions: conditions[random.Next(conditions.Length)],
        Humidity: random.Next(30, 95),
        WindSpeed: Math.Round(random.NextDouble() * 30, 1)
    );
}

// Middleware to handle server invocations
public class ServerInvokeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly McpServerInvokeOptions _options;

    public ServerInvokeMiddleware(RequestDelegate next, McpServerInvokeOptions options)
    {
        _next = next;
        _options = options;
    }

    public ServerInvokeMiddleware(RequestDelegate next, Action<McpServerInvokeOptions> configureOptions)
        : this(next, new McpServerInvokeOptions(configureOptions))
    {
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Set up the MCP server with the registered tools
        if (context.Features.Get<IMcpServer>() is McpServer server)
        {
            foreach (var registration in _options.ToolRegistrations)
            {
                server.RegisterToolHandler(registration.Tool.Definition, registration.Handler);
            }
        }

        await _next(context);
    }
}

// Helper classes for tool registration
public class McpServerInvokeOptions
{
    public List<ToolRegistration> ToolRegistrations { get; } = new();

    public McpServerInvokeOptions() { }

    public McpServerInvokeOptions(Action<McpServerInvokeOptions> configure)
    {
        configure(this);
    }

    public void RegisterTool(McpTool tool, Func<McpToolInvokeParameters, CancellationToken, Task<McpToolResult>> handler)
    {
        ToolRegistrations.Add(new ToolRegistration(tool, handler));
    }
}

public class ToolRegistration
{
    public McpTool Tool { get; }
    public Func<McpToolInvokeParameters, CancellationToken, Task<McpToolResult>> Handler { get; }

    public ToolRegistration(
        McpTool tool,
        Func<McpToolInvokeParameters, CancellationToken, Task<McpToolResult>> handler)
    {
        Tool = tool;
        Handler = handler;
    }
}

// Helper class to simplify tool registration and parameter handling
public class McpTool
{
    public ToolDefinition Definition { get; }

    public McpTool(string name, string description)
    {
        Definition = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = new ToolParameterDefinition
            {
                Properties = {},
                Required = new List<string>()
            }
        };
    }

    public McpTool WithParameter(string name, string description, Type type, bool required = false)
    {
        Definition.Parameters.Properties[name] = new ToolPropertyDefinition
        {
            Description = description,
            Type = GetJsonSchemaType(type)
        };

        if (required)
        {
            Definition.Parameters.Required.Add(name);
        }

        return this;
    }

    private static string GetJsonSchemaType(Type type)
    {
        return type.Name.ToLowerInvariant() switch
        {
            "string" => "string",
            "int32" or "int64" or "int" or "long" or "double" or "float" or "decimal" => "number",
            "boolean" => "boolean",
            _ => "object"
        };
    }
}

// Helper class for the tool invocation parameters
public class McpToolInvokeParameters
{
    private readonly Dictionary<string, object?> _parameters;

    public McpToolInvokeParameters(Dictionary<string, object?> parameters)
    {
        _parameters = parameters;
    }

    public bool TryGetParameterValue<T>(string name, out T value)
    {
        if (_parameters.TryGetValue(name, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default!;
        return false;
    }

    public T? GetParameterValue<T>(string name)
    {
        if (_parameters.TryGetValue(name, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        return default;
    }
}

// Helper class for the tool result
public class McpToolResult
{
    public object? Result { get; }
    public string? Error { get; }
    public bool IsError => Error != null;

    private McpToolResult(object? result, string? error)
    {
        Result = result;
        Error = error;
    }

    public static McpToolResult Success(object? result = null) => new McpToolResult(result, null);
    public static McpToolResult Error(string error) => new McpToolResult(null, error);
}