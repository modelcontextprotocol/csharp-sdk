using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DynamicToolFiltering.Tools;

/// <summary>
/// Public tools available to all users without authentication.
/// These represent the most basic functionality that doesn't require any authorization.
/// </summary>
public class PublicTools
{
    /// <summary>
    /// Get basic system information - available to all users.
    /// </summary>
    [McpServerTool(Name = "get_system_info", Description = "Get basic system information and API status")]
    public static async Task<CallToolResult> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken); // Simulate some work
        
        var systemInfo = new
        {
            Status = "online",
            Version = "1.0.0",
            Timestamp = DateTime.UtcNow.ToString("O"),
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount
        };

        return CallToolResult.FromContent(
            TextContent.Create($"System Information: {System.Text.Json.JsonSerializer.Serialize(systemInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Simple echo service for testing connectivity.
    /// </summary>
    [McpServerTool(Name = "echo", Description = "Echo back the provided message")]
    public static async Task<CallToolResult> EchoAsync(
        [Description("The message to echo back")] string message,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        
        return CallToolResult.FromContent(
            TextContent.Create($"Echo: {message} (timestamp: {DateTime.UtcNow:O})"));
    }

    /// <summary>
    /// Get current UTC time - useful for timezone-independent operations.
    /// </summary>
    [McpServerTool(Name = "get_utc_time", Description = "Get the current UTC timestamp")]
    public static async Task<CallToolResult> GetUtcTimeAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(5, cancellationToken);
        
        var timeInfo = new
        {
            UtcTime = DateTime.UtcNow.ToString("O"),
            UnixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DayOfWeek = DateTime.UtcNow.DayOfWeek.ToString(),
            IsWeekend = DateTime.UtcNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Time Information: {System.Text.Json.JsonSerializer.Serialize(timeInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }
}