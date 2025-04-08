namespace ModelContextProtocol.Server;

/// <summary>
/// Used to attribute a type containing methods that should be exposed as MCP tools.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is used to mark a class containing methods that should be automatically
/// discovered and registered as MCP tools. When combined with discovery methods like
/// <see cref="Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions.WithToolsFromAssembly"/>,
/// it enables automatic registration of tools without explicitly listing each tool class.
/// </para>
/// <para>
/// Within a class marked with this attribute, individual methods that should be exposed as
/// tools must be marked with the <see cref="McpServerToolAttribute"/>.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// [McpServerToolType]
/// public class WeatherTools
/// {
///     [McpServerTool(Name = "getWeather")]
///     public static string GetWeather(string city)
///     {
///         return $"The weather in {city} is sunny.";
///     }
///     
///     [McpServerTool(Name = "getTemperature")]
///     public static int GetTemperature(string city)
///     {
///         // Get temperature logic here
///         return 72;
///     }
/// }
/// </code>
/// </para>
/// <para>
/// Registration in a dependency injection container:
/// <code>
/// // Scan assembly for all tool types
/// builder.Services.AddMcpServer()
///     .WithToolsFromAssembly();
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerToolTypeAttribute : Attribute;
