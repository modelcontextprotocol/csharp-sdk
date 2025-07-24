using ModelContextProtocol.Server;
using System.ComponentModel;

namespace UrlRoutingSseServer.Tools;

[McpServerToolType]
public sealed class MathTool
{
    [McpServerTool, Description("Adds two numbers together.")]
    [McpServerToolRoute("math", "utilities")]
    public static int Add(
        [Description("First number")] int a,
        [Description("Second number")] int b)
    {
        return a + b;
    }

    [McpServerTool, Description("Calculates factorial of a number.")]
    [McpServerToolRoute("math")]
    public static long Factorial([Description("Number to calculate factorial for")] int n)
    {
        if (n < 0) return -1;
        if (n == 0 || n == 1) return 1;

        long result = 1;
        for (int i = 2; i <= n; i++)
        {
            result *= i;
        }
        return result;
    }
}
