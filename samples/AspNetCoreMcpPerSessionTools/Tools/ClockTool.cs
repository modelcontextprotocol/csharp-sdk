using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpPerSessionTools.Tools;

/// <summary>
/// Clock-related tools for time and date operations
/// </summary>
[McpServerToolType]
public sealed class ClockTool
{
    [McpServerTool, Description("Gets the current server time in various formats.")]
    public static string GetTime()
    {
        return $"Current server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC";
    }

    [McpServerTool, Description("Gets the current date in a specific format.")]
    public static string GetDate([Description("Date format (e.g., 'yyyy-MM-dd', 'MM/dd/yyyy')")] string format = "yyyy-MM-dd")
    {
        try
        {
            return $"Current date: {DateTime.Now.ToString(format)}";
        }
        catch (FormatException)
        {
            return $"Invalid format '{format}'. Using default: {DateTime.Now:yyyy-MM-dd}";
        }
    }

    [McpServerTool, Description("Converts time between timezones.")]
    public static string ConvertTimeZone(
        [Description("Source timezone (e.g., 'UTC', 'EST')")] string fromTimeZone = "UTC",
        [Description("Target timezone (e.g., 'PST', 'GMT')")] string toTimeZone = "PST")
    {
        // Simplified timezone conversion for demo purposes
        var now = DateTime.Now;
        return $"Time conversion from {fromTimeZone} to {toTimeZone}: {now:HH:mm:ss} (simulated)";
    }
}