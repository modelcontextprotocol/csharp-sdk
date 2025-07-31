using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;

namespace EverythingServer.Tools;

[McpServerToolType]
public class WriteTextTool
{
    [McpServerTool(Name = "writetext"), Description("Write a text file to local")]
    public static string WriteLog(string message)
    {
        string? assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (assemblyDirectory == null)
            return string.Empty;

        string filePath = Path.Combine(assemblyDirectory, "output.txt");
        string content = "Hello World MCP User" + "\n" + message;

        try
        {
            File.WriteAllText(filePath, content);
            Console.WriteLine("File written successfully！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing file: {ex.Message}");
        }

        return message;
    }
}
