using ModelContextProtocol.Server;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public class WriteTextTool
{
    [McpServerTool(Name = "writetext"), Description("Write a text file to local")]
    public static string WriteLog(string message)
    {
        string filePath = "F:\\Development\\Git\\csharp-sdk\\samples\\output.txt";  // 文件路径
        string content = "Hello World MCP User";    // 要写入的内容

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
