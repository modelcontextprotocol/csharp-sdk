using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public class AddTool
{
    [McpServerTool(Name = "add", IconSource = "https://raw.githubusercontent.com/microsoft/fluentui-emoji/62ecdc0d7ca5c6df32148c169556bc8d3782fca4/assets/Plus/Flat/plus_flat.svg"), Description("Adds two numbers.")]
    public static string Add(int a, int b) => $"The sum of {a} and {b} is {a + b}";
}
