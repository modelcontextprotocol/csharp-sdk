using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpPerSessionTools.Tools;

/// <summary>
/// User information tools
/// </summary>
[McpServerToolType]
public sealed class UserInfoTool
{
    [McpServerTool, Description("Gets information about the current user in the MCP session.")]
    public static string GetUserInfo()
    {
        // Dummy user information for demonstration purposes
        return $"User Information:\n" +
               $"- User ID: {Guid.NewGuid():N}[..8] (simulated)\n" +
               $"- Username: User{new Random().Next(1, 1000)}\n" +
               $"- Roles: User, Guest\n" +
               $"- Last Login: {DateTime.Now.AddMinutes(-new Random().Next(1, 60)):HH:mm:ss}\n" +
               $"- Account Status: Active";
    }
}