using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public class AddTool
{
    [McpServerTool(Name = "add"), Description("Adds two numbers.")]
    public static string Add(int a, int b) => $"The sum of {a} and {b} is {a + b}";
}
