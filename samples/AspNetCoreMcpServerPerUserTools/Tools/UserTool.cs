using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpServerPerUserTools.Tools;

/// <summary>
/// User-level tools available to authenticated users
/// </summary>
[McpServerToolType]
public sealed class UserTool
{
    [McpServerTool, Description("Gets personalized user information. Requires user authentication.")]
    public static string GetUserInfo(string? userId = null)
    {
        return $"User information for: {userId ?? "current user"}. Profile: Standard User";
    }

    [McpServerTool, Description("Performs basic calculations. Available to authenticated users.")]
    public static string Calculate([Description("Mathematical expression to evaluate")] string expression)
    {
        // Simple calculator for demo purposes
        try
        {
            // For demo, just handle basic addition/subtraction
            if (expression.Contains("+"))
            {
                var parts = expression.Split('+');
                if (parts.Length == 2 && double.TryParse(parts[0].Trim(), out var a) && double.TryParse(parts[1].Trim(), out var b))
                {
                    return $"{expression} = {a + b}";
                }
            }
            else if (expression.Contains("-"))
            {
                var parts = expression.Split('-');
                if (parts.Length == 2 && double.TryParse(parts[0].Trim(), out var a) && double.TryParse(parts[1].Trim(), out var b))
                {
                    return $"{expression} = {a - b}";
                }
            }
            return $"Cannot evaluate expression: {expression}. Try simple addition (a + b) or subtraction (a - b).";
        }
        catch
        {
            return $"Error evaluating: {expression}";
        }
    }
}